namespace OmniEurope.Installer.Setup;

/// <summary>What running the carried MSI means on this machine.</summary>
internal enum SetupMode
{
    /// <summary>The product is not installed.</summary>
    Install,

    /// <summary>An older version is installed: the MSI replaces it.</summary>
    Upgrade,

    /// <summary>Same version, but another build (other ProductCode): the MSI replaces it.</summary>
    SameVersion,

    /// <summary>This very MSI is installed: only repair or uninstall make sense.</summary>
    Repair,

    /// <summary>A newer version is installed: the MSI refuses to run.</summary>
    Downgrade,
}
