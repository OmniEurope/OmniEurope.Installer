using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Administrator rights: the setup starts without them, and runs again with them (UAC prompt) only for a per-machine
/// operation.
/// </summary>
internal static class Elevation
{
    private const int Cancelled = 1223;

    /// <summary>Whether this process runs with administrator rights (an elevated token, not just an administrator account).</summary>
    public static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Runs this setup again with administrator rights and waits for it; its exit code, or null when the UAC prompt was declined.</summary>
    public static int? Relaunch(IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(Application.ExecutablePath, SetupCommandLine.Format(arguments))
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        try
        {
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == Cancelled)
        {
            return null;
        }
    }
}
