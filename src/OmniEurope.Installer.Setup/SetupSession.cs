using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// One run of the setup program: the carried MSI extracted to a temporary folder, the product found
/// on the machine, and the operation itself (close the product, run the MSI, remove a former setup's
/// entry). Shared by the wizard and the quiet mode.
/// </summary>
internal sealed class SetupSession : IDisposable
{
    private readonly string _workFolder;
    private bool _insideMsi;

    private SetupSession(SetupConfig config, string msiPath, string? licensePath, string workFolder, string logPath)
    {
        Config = config;
        MsiPath = msiPath;
        LicensePath = licensePath;
        _workFolder = workFolder;
        LogPath = logPath;
        Installed = InstalledProduct.Find(config.UpgradeCode);
        Mode = SetupDecision.Decide(Installed?.ProductCode, Installed?.Version, config.ProductCode, config.ProductVersion);
    }

    public SetupConfig Config { get; }

    public string MsiPath { get; }

    public string? LicensePath { get; }

    public string LogPath { get; }

    public InstalledProduct? Installed { get; }

    public SetupMode Mode { get; }

    /// <summary>Raised from the operation's thread with the overall percentage.</summary>
    public event Action<int?>? ProgressChanged;

    /// <summary>Raised from the operation's thread with each step, error and setup message.</summary>
    public event Action<string>? Message;

    /// <summary>Reads the payload of <paramref name="setupExecutable"/> and extracts the MSI and license.</summary>
    public static SetupSession Open(string setupExecutable, string? logPath)
    {
        string workFolder = Path.Combine(Path.GetTempPath(), "OmniEurope.Installer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workFolder);
        try
        {
            using var input = new FileStream(setupExecutable, FileMode.Open, FileAccess.Read, FileShare.Read);
            SetupPayload payload = SetupPayload.Read(input);
            SetupConfig config = payload.Config;
            string msiPath = Path.Combine(workFolder, $"{config.ProductName}-{config.ProductVersion}.msi");
            SetupPayload.Extract(input, payload.MsiOffset, payload.MsiLength, msiPath);
            string? licensePath = null;
            if (payload.LicenseLength > 0)
            {
                licensePath = Path.Combine(workFolder, "license.rtf");
                SetupPayload.Extract(input, payload.LicenseOffset, payload.LicenseLength, licensePath);
            }

            string log = logPath ?? Path.Combine(Path.GetTempPath(), $"{config.ProductName}-Setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            return new SetupSession(config, msiPath, licensePath, workFolder, log);
        }
        catch
        {
            // No session owns the folder yet: a damaged payload or a full disk must not leave a half MSI behind.
            Directory.Delete(workFolder, recursive: true);
            throw;
        }
    }

    /// <summary>The defaults, overridden by what the previous installation recorded.</summary>
    public InstallChoices InitialChoices()
    {
        string programFiles = Environment.GetEnvironmentVariable("ProgramW6432")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        InstallChoices choices = InstallChoices.Defaults(Config, programFiles);
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(Config.SettingsKey);
        if (settings is not null)
        {
            choices.ApplySettings(name => settings.GetValue(name) as string, Config);
        }

        return choices;
    }

    /// <summary>Runs <paramref name="action"/>; returns the Windows Installer result (0 or 3010 on success, see <see cref="MsiCommandLines"/>).</summary>
    public uint Run(SetupAction action, InstallChoices choices, IDictionary<string, string> overrides)
    {
        string? installFolder = InstalledFolder() ?? choices.InstallFolder;
        RunningApplication.Close(installFolder, Config.MainExecutable, Report);

        var operation = new MsiOperation(LogPath);
        operation.ProgressChanged += percent => ProgressChanged?.Invoke(percent);
        operation.Message += Report;
        _insideMsi = true;
        uint result;
        try
        {
            result = action switch
            {
                SetupAction.Uninstall => operation.Uninstall(Installed?.ProductCode ?? Config.ProductCode),
                SetupAction.Repair => operation.Repair(MsiPath),
                _ => operation.Install(MsiPath, choices.ToCommandLine(Config, overrides)),
            };
        }
        finally
        {
            _insideMsi = false;
        }

        Report($"Windows Installer result: {result}");
        if (MsiCommandLines.IsSuccess(result))
        {
            LegacyBundles.Remove(Config.LegacyBundleUpgradeCodes, Report);
        }

        return result;
    }

    /// <summary>The installed main executable, resolved by Windows Installer; null when not installed.</summary>
    public string? InstalledExecutable() =>
        Config.MainComponentId.Length == 0 ? null : InstalledProduct.ComponentPath(Config.ProductCode, Config.MainComponentId);

    /// <summary>Starts the installed product through Explorer, so it runs without the setup's administrator rights.</summary>
    public bool LaunchProduct()
    {
        string? executable = InstalledExecutable();
        if (executable is null || !File.Exists(executable))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{executable}\"") { UseShellExecute = false })?.Dispose();
        return true;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workFolder, recursive: true);
        }
        catch (IOException)
        {
            // Windows Installer may still hold the package for a moment; %TEMP% cleanup takes it later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string? InstalledFolder()
    {
        if (Installed is null || Config.MainComponentId.Length == 0)
        {
            return null;
        }

        string? executable = InstalledProduct.ComponentPath(Installed.ProductCode, Config.MainComponentId);
        return executable is null ? null : Path.GetDirectoryName(executable);
    }

    // The setup's own lines go to the log too; while Windows Installer runs, it writes the log itself.
    private void Report(string message)
    {
        Message?.Invoke(message);
        if (!_insideMsi)
        {
            // UTF-16, like the verbose log Windows Installer appends to this same file.
            File.AppendAllText(LogPath, $"=== OmniEurope.Installer.Setup {DateTime.Now:HH:mm:ss}: {message}{Environment.NewLine}", System.Text.Encoding.Unicode);
        }
    }
}
