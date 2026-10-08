namespace OmniEurope.Installer.Setup;

/// <summary>Who an installation is for: the current user only (no administrator rights), or every user of the machine.</summary>
internal enum InstallScope
{
    PerUser,
    PerMachine,
}
