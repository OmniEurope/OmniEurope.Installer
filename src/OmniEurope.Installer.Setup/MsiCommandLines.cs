using System.Linq;

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

    private const string Reinstall = "REINSTALL=ALL REINSTALLMODE=amus";

    /// <summary>
    /// The installation context of the dual-purpose package: ALLUSERS=2 with MSIINSTALLPERUSER=1 installs for the
    /// current user without administrator rights, ALLUSERS=1 for every user of the machine.
    /// </summary>
    public static string Context(InstallScope scope) =>
        scope == InstallScope.PerUser ? "ALLUSERS=2 MSIINSTALLPERUSER=1" : "ALLUSERS=1";

    /// <summary>
    /// The install's property assignments, then the context and the restart suppression, last so that no override
    /// replaces them.
    /// </summary>
    public static string Install(string properties, InstallScope scope) => Join(properties, Context(scope), NoAutomaticRestart);

    /// <summary>
    /// Reinstalls every file of the very product the MSI describes, in the context it is installed in, with the given
    /// property assignments; the context, the reinstall settings and the restart suppression come last.
    /// </summary>
    public static string Repair(string properties, InstallScope scope) => Join(properties, Context(scope), Reinstall, NoAutomaticRestart);

    public static string Uninstall() => NoAutomaticRestart;

    public static bool IsSuccess(uint result) => result == Success || result == SuccessRestartRequired;

    /// <summary>Done, but files in use are replaced only when the computer restarts.</summary>
    public static bool RestartRequired(uint result) => result == SuccessRestartRequired;

    private static string Join(params string[] parts) => string.Join(" ", parts.Where(part => part.Length > 0));
}
