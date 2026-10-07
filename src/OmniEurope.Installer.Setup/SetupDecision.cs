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
}
