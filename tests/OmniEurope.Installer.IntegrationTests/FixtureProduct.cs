using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using OmniEurope.Installer.Building;
using OmniEurope.Installer.Product;
using OmniEurope.Installer.Setup;
using OmniEurope.Installer.Tests;

namespace OmniEurope.Installer.IntegrationTests;

/// <summary>
/// A throwaway product with its own UpgradeCode, folder, Start menu entry and settings key,
/// built in versions 1.0.0 and 2.0.0 as real setup executables. The scenarios install it machine-wide,
/// so they run only elevated and on request (CI, or a disposable machine), and fail otherwise.
/// </summary>
public sealed class FixtureProduct : IDisposable
{
    public const string Name = "OmniEurope Installer Fixture";
    public const string MainExecutable = "FixtureApp.exe";
    public const string DataFile = "data.txt";
    public const string SettingsKey = @"Software\OmniEurope\InstallerFixture\Installer";
    public const string OptInVariable = "OE_INSTALLER_INTEGRATION";

    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string LegacyEntryName = "OmniEurope.Installer.Fixture.FormerBundle";

    private static readonly Guid UpgradeCode = Guid.Parse("8F3B6C41-2D7E-4A95-B1C8-5E0F9A7D3C26");
    private static readonly Guid LegacyBundleCode = Guid.Parse("2E9A7C50-1B4D-4F83-96E2-7D0C5A3B8F14");

    private readonly TempFolder _temp = new();

    public FixtureProduct()
    {
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
        {
            throw new InvalidOperationException(
                $"These tests install a product machine-wide: set {OptInVariable}=1 on a disposable machine (CI, Windows Sandbox) to run them.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new InvalidOperationException("These tests install a per-machine MSI: run them elevated.");
        }

        HostExecutable = typeof(FixtureProduct).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "SetupHost").Value!;
        if (!File.Exists(HostExecutable))
        {
            throw new FileNotFoundException("Build the solution first: the setup host is missing.", HostExecutable);
        }

        RemoveLeftovers();
        SetupV1 = BuildSetup("1.0.0");
        SetupV2 = BuildSetup("2.0.0");
    }

    public string HostExecutable { get; }

    public string SetupV1 { get; }

    public string SetupV2 { get; }

    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OmniEurope.Tests", "Fixture");

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Name, $"{Name}.lnk");

    /// <summary>The "Installed apps" entries of the fixture, as (ProductCode, DisplayVersion).</summary>
    public static IReadOnlyList<(string ProductCode, string Version)> Entries()
    {
        var entries = new List<(string, string)>();
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            foreach (string name in uninstall?.GetSubKeyNames() ?? [])
            {
                using RegistryKey? entry = uninstall!.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") as string == Name)
                {
                    entries.Add((name, entry.GetValue("DisplayVersion") as string ?? string.Empty));
                }
            }
        }

        return entries;
    }

    public static string? RecordedInstallFolder()
    {
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return settings?.GetValue("InstallFolder") as string;
    }

    /// <summary>
    /// An Installed apps entry as a former setup bundle leaves it: the fixture's legacy BundleUpgradeCode and
    /// a quiet uninstall command that, as the real one would, removes the entry itself.
    /// </summary>
    public static void CreateLegacyEntry()
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey entry = root.CreateSubKey($@"{UninstallKey}\{LegacyEntryName}");
        entry.SetValue("DisplayName", $"{Name} (former setup)");
        entry.SetValue("BundleUpgradeCode", new[] { LegacyBundleCode.ToString("B").ToUpperInvariant() }, RegistryValueKind.MultiString);
        string reg = Path.Combine(Environment.SystemDirectory, "reg.exe");
        entry.SetValue("QuietUninstallString", $"\"{reg}\" delete \"HKLM\\{UninstallKey}\\{LegacyEntryName}\" /f /reg:64");
    }

    public static bool LegacyEntryExists()
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? entry = root.OpenSubKey($@"{UninstallKey}\{LegacyEntryName}");
        return entry is not null;
    }

    /// <summary>Runs a setup quietly; returns its exit code and its log (UTF-16, as Windows Installer writes it).</summary>
    public (int ExitCode, string Log) Run(string setup, params string[] arguments)
    {
        string log = _temp.Combine($"setup-{Guid.NewGuid():N}.log");
        (int exitCode, _) = ProcessRunner.Run(setup, ["/quiet", "/log", log, .. arguments]);
        string text = File.Exists(log) ? File.ReadAllText(log, Encoding.Unicode) : string.Empty;
        return (exitCode, text);
    }

    public void Dispose()
    {
        RemoveLeftovers();
        _temp.Dispose();
    }

    private static string FixtureAppSource => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private string BuildSetup(string version)
    {
        // The application is a renamed copy of cmd.exe: it can run, hold its own image and be closed.
        string source = _temp.Combine($"publish-{version}");
        Directory.CreateDirectory(source);
        File.Copy(FixtureAppSource, Path.Combine(source, MainExecutable));
        File.WriteAllText(Path.Combine(source, DataFile), $"version {version}");
        var definition = new ProductDefinition
        {
            Name = Name,
            Manufacturer = "OmniEurope",
            UpgradeCode = UpgradeCode,
            MainExecutable = MainExecutable,
            InstallDirectory = ["OmniEurope.Tests", "Fixture"],
            DowngradeErrorMessage = "A newer version of the fixture is already installed.",
            Shortcuts =
            [
                new ShortcutDefinition
                {
                    Id = "StartMenuShortcut",
                    Location = ShortcutLocation.ProgramMenu,
                    Folder = Name,
                    Name = Name,
                    RegistryKey = @"Software\OmniEurope\InstallerFixture",
                    RegistryName = "StartMenuShortcut",
                },
            ],
            RegistryComponents =
            [
                new RegistryComponentDefinition
                {
                    Id = "InstallerSettings",
                    Root = RegistryRoot.CurrentUser,
                    Key = SettingsKey,
                    Values = [new RegistryValueDefinition { Name = "InstallFolder", Type = RegistryValueType.String, Value = "[INSTALLFOLDER]" }],
                },
            ],
            Setup = new SetupDefinition { SettingsKey = SettingsKey, LegacyBundleUpgradeCodes = [LegacyBundleCode] },
        };
        MsiBuildResult msi = MsiPackageBuilder.Build(definition, new MsiBuildRequest(source, version, _temp.Combine($"fixture-{version}.msi")));
        string setup = _temp.Combine($"fixture-{version}-Setup.exe");
        string icon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        SetupPackageBuilder.Build(definition, new SetupBuildRequest(HostExecutable, msi.OutputPath, null, icon, setup));
        return setup;
    }

    // A previous run that stopped half-way must not leave the fixture installed for the next scenario.
    private void RemoveLeftovers()
    {
        string folder = InstallFolder + Path.DirectorySeparatorChar;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainExecutable)))
        {
            using (process)
            {
                // Only the fixture's own instances: a program of the same name elsewhere is left alone.
                string? path;
                try
                {
                    path = process.MainModule?.FileName;
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone, or not ours to inspect: either way not a fixture instance to stop.
                    continue;
                }

                if (path is not null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && !process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
        }

        foreach ((string productCode, _) in Entries())
        {
            ProcessRunner.Run("msiexec.exe", "/x", productCode, "/qn", "REBOOT=ReallySuppress");
        }

        using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        root.DeleteSubKeyTree($@"{UninstallKey}\{LegacyEntryName}", throwOnMissingSubKey: false);
    }
}
