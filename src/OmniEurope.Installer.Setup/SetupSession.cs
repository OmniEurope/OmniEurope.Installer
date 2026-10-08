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
        Scope = Installed?.Scope;
    }

    public SetupConfig Config { get; }

    public string MsiPath { get; }

    public string? LicensePath { get; }

    public string LogPath { get; }

    public InstalledProduct? Installed { get; }

    public SetupMode Mode { get; }

    /// <summary>
    /// Who the operation is for: the installed product's context, or the one chosen for a first installation (command
    /// line or wizard); null until chosen.
    /// </summary>
    public InstallScope? Scope { get; set; }

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
            string fileName = SetupFileNames.Safe(config.ProductName);
            string msiPath = Path.Combine(workFolder, $"{fileName}-{config.ProductVersion}.msi");
            SetupPayload.Extract(input, payload.MsiOffset, payload.MsiLength, msiPath);
            string? licensePath = null;
            if (payload.LicenseLength > 0)
            {
                licensePath = Path.Combine(workFolder, "license.rtf");
                SetupPayload.Extract(input, payload.LicenseOffset, payload.LicenseLength, licensePath);
            }

            string log = logPath ?? Path.Combine(Path.GetTempPath(), $"{fileName}-Setup-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            return new SetupSession(config, msiPath, licensePath, workFolder, log);
        }
        catch
        {
            // No session owns the folder yet: a damaged payload or a full disk must not leave a half MSI behind.
            Directory.Delete(workFolder, recursive: true);
            throw;
        }
    }

    /// <summary>The default folder of a first installation in <paramref name="scope"/>: Program Files, or the user's Programs folder.</summary>
    public string DefaultInstallFolder(InstallScope scope) => InstallChoices.Defaults(Config, ProgramsFolder(scope)).InstallFolder;

    /// <summary>The defaults of <paramref name="scope"/>, overridden by what the installation in that context recorded.</summary>
    public InstallChoices InitialChoices(InstallScope scope)
    {
        InstallChoices choices = InstallChoices.Defaults(Config, ProgramsFolder(scope));
        using RegistryKey? settings = OpenSettings(scope);
        if (settings is not null)
        {
            choices.ApplySettings(name => settings.GetValue(name) as string, Config);
        }

        return choices;
    }

    /// <summary>Runs <paramref name="action"/>; returns the Windows Installer result (0 or 3010 on success, see <see cref="MsiCommandLines"/>).</summary>
    public uint Run(SetupAction action, InstallChoices choices, IDictionary<string, string> overrides)
    {
        InstallScope scope = Scope ?? throw new InvalidOperationException("The installation context is not chosen.");
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
                SetupAction.Repair => operation.Repair(MsiPath, choices.ToCommandLine(Config, overrides), scope),
                _ => operation.Install(MsiPath, choices.ToCommandLine(Config, overrides), scope),
            };
        }
        finally
        {
            _insideMsi = false;
        }

        Report($"Windows Installer result: {result}");
        if (MsiCommandLines.IsSuccess(result))
        {
            RemoveLegacyBundles();
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

    private static string ProgramsFolder(InstallScope scope) => scope == InstallScope.PerUser
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        : Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    // The MSI records the choices in the installation's hive: HKCU per user, HKLM per machine (64-bit view, as its
    // 64-bit components write them). An older per-machine installation recorded them under HKCU, for the account that
    // ran the setup: read when HKLM holds none, so that its choices carry over.
    private RegistryKey? OpenSettings(InstallScope scope)
    {
        if (scope == InstallScope.PerUser)
        {
            return Registry.CurrentUser.OpenSubKey(Config.SettingsKey);
        }

        using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return machine.OpenSubKey(Config.SettingsKey) ?? Registry.CurrentUser.OpenSubKey(Config.SettingsKey);
    }

    // A former setup's entry lives under HKLM: removing it needs administrator rights, which only a per-machine run has.
    private void RemoveLegacyBundles()
    {
        if (Config.LegacyBundleUpgradeCodes.Length == 0)
        {
            return;
        }

        if (Elevation.IsElevated())
        {
            LegacyBundles.Remove(Config.LegacyBundleUpgradeCodes, Report);
        }
        else
        {
            Report("Former setup entries are left: removing them needs administrator rights.");
        }
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
