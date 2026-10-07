namespace OmniEurope.Installer.Setup;

/// <summary>
/// The command lines given to Windows Installer, and how its result reads. Every operation suppresses
/// the automatic restart: without it, a file still in use makes a quiet Windows Installer restart the
/// computer on its own (1641). The setup reports 3010 instead and lets the user restart.
/// </summary>
internal static class MsiCommandLines
{
    public const string NoAutomaticRestart = "REBOOT=ReallySuppress";

    public const uint Success = 0;

    public const uint SuccessRestartRequired = 3010;

    /// <summary>The install's property assignments, the restart suppression last so that no override replaces it.</summary>
    public static string Install(string properties) =>
        properties.Length == 0 ? NoAutomaticRestart : $"{properties} {NoAutomaticRestart}";

    /// <summary>Reinstalls every file of the very product the MSI describes.</summary>
    public static string Repair() => $"REINSTALL=ALL REINSTALLMODE=amus {NoAutomaticRestart}";

    public static string Uninstall() => NoAutomaticRestart;

    public static bool IsSuccess(uint result) => result == Success || result == SuccessRestartRequired;

    /// <summary>Done, but files in use are replaced only when the computer restarts.</summary>
    public static bool RestartRequired(uint result) => result == SuccessRestartRequired;
}
