using System;

namespace OmniEurope.Installer.Setup;

/// <summary>Pure decision from the installed product and the carried MSI.</summary>
internal static class SetupDecision
{
    public static SetupMode Decide(string? installedProductCode, string? installedVersion, string packageProductCode, string packageVersion)
    {
        if (string.IsNullOrEmpty(installedProductCode))
        {
            return SetupMode.Install;
        }

        if (string.Equals(installedProductCode, packageProductCode, StringComparison.OrdinalIgnoreCase))
        {
            return SetupMode.Repair;
        }

        // Compared as versions, not strings: "1.9.0" is older than "1.10.0".
        if (!Version.TryParse(installedVersion, out Version? installed) || !Version.TryParse(packageVersion, out Version? package))
        {
            return SetupMode.Upgrade;
        }

        int comparison = package.CompareTo(installed);
        return comparison > 0 ? SetupMode.Upgrade : comparison == 0 ? SetupMode.SameVersion : SetupMode.Downgrade;
    }

    /// <summary>
    /// Whether the maintenance page may go on: always for an uninstall (a newer installed version can be
    /// removed, as with /quiet /uninstall), otherwise unless the installed version is newer.
    /// </summary>
    public static bool CanProceed(SetupMode mode, bool uninstall) => uninstall || mode != SetupMode.Downgrade;

    /// <summary>
    /// The context of the operation: an installed product keeps its own (changing it means uninstalling first);
    /// otherwise the one asked on the command line; otherwise per machine for a quiet run, as a deployment script or
    /// a Group Policy expects. Null when the wizard must ask.
    /// </summary>
    public static InstallScope? Scope(InstallScope? installed, InstallScope? requested, bool quiet) =>
        installed ?? requested ?? (quiet ? InstallScope.PerMachine : null);

    /// <summary>A per-machine operation needs administrator rights; a per-user one never does.</summary>
    public static bool NeedsElevation(InstallScope scope, bool elevated) => scope == InstallScope.PerMachine && !elevated;
}
