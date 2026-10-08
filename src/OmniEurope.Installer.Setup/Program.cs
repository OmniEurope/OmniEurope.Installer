using System;
using System.IO;
using System.Windows.Forms;

namespace OmniEurope.Installer.Setup;

internal static class Program
{
    private const int InvalidParameter = 87;
    private const int UnknownProduct = 1605;
    private const int AnotherVersionInstalled = 1638;
    private const int InstallFailure = 1603;

    [STAThread]
    private static int Main(string[] args)
    {
        SetupCommandLine commandLine;
        try
        {
            commandLine = SetupCommandLine.Parse(args);
        }
        catch (ArgumentException exception)
        {
            if (!Array.Exists(args, argument => argument.Equals("/quiet", StringComparison.OrdinalIgnoreCase) || argument.Equals("/q", StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(exception.Message, "Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return InvalidParameter;
        }

        try
        {
            using SetupSession session = SetupSession.Open(Application.ExecutablePath, commandLine.LogPath);
            if (commandLine.RenderPagesFolder is not null)
            {
                Application.EnableVisualStyles();
                WizardForm.RenderPages(session, commandLine, Path.GetFullPath(commandLine.RenderPagesFolder));
                return 0;
            }

            session.Scope = SetupDecision.Scope(session.Installed?.Scope, commandLine.Scope, commandLine.Quiet);
            if (commandLine.Quiet && QuietRefusal(session, commandLine) is int refusal)
            {
                return refusal;
            }

            // The setup starts without administrator rights; a per-machine operation runs again with them (UAC), the
            // wizard's own choice included (see WizardForm). Declining the prompt cancels.
            if (session.Scope is InstallScope scope && SetupDecision.NeedsElevation(scope, Elevation.IsElevated()))
            {
                return Elevation.Relaunch(SetupCommandLine.ForElevation(args, commandLine.Language)) ?? (int)MsiApi.UserExit;
            }

            if (commandLine.Quiet)
            {
                return RunQuiet(session, commandLine);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new WizardForm(session, commandLine, args);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // A damaged setup file, a full disk or an unwritable log: a Windows Installer failure code,
            // and a message unless the caller asked for no window (a deployment script reads the code).
            if (!commandLine.Quiet)
            {
                MessageBox.Show(exception.Message, "Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            Console.Error.WriteLine($"Setup failed: {exception.Message}");
            return InstallFailure;
        }
    }

    /// <summary>No window: install, update, repair or (with /uninstall) remove, with the recorded or default choices.</summary>
    private static int RunQuiet(SetupSession session, SetupCommandLine commandLine)
    {
        InstallChoices choices = session.InitialChoices(session.Scope!.Value);
        if (commandLine.Uninstall)
        {
            return (int)session.Run(SetupAction.Uninstall, choices, commandLine.Properties);
        }

        SetupAction action = session.Mode == SetupMode.Repair ? SetupAction.Repair : SetupAction.Install;
        return (int)session.Run(action, choices, commandLine.Properties);
    }

    /// <summary>A quiet run with nothing to do: answered before asking for administrator rights.</summary>
    private static int? QuietRefusal(SetupSession session, SetupCommandLine commandLine) =>
        commandLine.Uninstall && session.Installed is null ? UnknownProduct
        : !commandLine.Uninstall && session.Mode == SetupMode.Downgrade ? AnotherVersionInstalled
        : null;
}
