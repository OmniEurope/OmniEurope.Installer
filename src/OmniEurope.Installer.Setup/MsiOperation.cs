using System;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Runs one Windows Installer operation without any Windows Installer window: progress, step names
/// and errors come back through an external UI handler, and everything goes to a verbose log.
/// Blocking: call it from a background thread when a window must stay responsive.
/// </summary>
internal sealed class MsiOperation
{
    private const uint HandledMessages = MsiApi.LogFatalExit | MsiApi.LogError | MsiApi.LogWarning | MsiApi.LogUser
        | MsiApi.LogActionStart | MsiApi.LogActionData | MsiApi.LogProgress;

    private readonly string _logPath;
    private readonly MsiProgress _progress = new();

    public MsiOperation(string logPath)
    {
        _logPath = logPath;
    }

    /// <summary>Raised with the overall percentage (null before Windows Installer announces a total).</summary>
    public event Action<int?>? ProgressChanged;

    /// <summary>Raised with each step name and each error or warning text.</summary>
    public event Action<string>? Message;

    public uint Install(string msiPath, string properties) => Run(() => MsiApi.MsiInstallProduct(msiPath, MsiCommandLines.Install(properties)));

    /// <summary>Reinstalls every file of the very product the MSI describes.</summary>
    public uint Repair(string msiPath) => Run(() => MsiApi.MsiInstallProduct(msiPath, MsiCommandLines.Repair()));

    public uint Uninstall(string productCode) =>
        Run(() => MsiApi.MsiConfigureProductEx(productCode, MsiApi.InstallLevelDefault, MsiApi.InstallStateAbsent, MsiCommandLines.Uninstall()));

    private uint Run(Func<uint> call)
    {
        MsiApi.InstallUiHandler handler = OnMessage;
        int previousLevel = MsiApi.MsiSetInternalUI(MsiApi.UiLevelNone, IntPtr.Zero);
        MsiApi.InstallUiHandler? previousHandler = MsiApi.MsiSetExternalUI(handler, HandledMessages, IntPtr.Zero);
        MsiApi.MsiEnableLog(MsiApi.LogVerbose, _logPath, MsiApi.LogAppendFlushEachLine);
        try
        {
            return call();
        }
        finally
        {
            MsiApi.MsiSetExternalUI(previousHandler, 0, IntPtr.Zero);
            MsiApi.MsiSetInternalUI(previousLevel, IntPtr.Zero);
            // The native side holds only a function pointer: the delegate must outlive the call.
            GC.KeepAlive(handler);
        }
    }

    private int OnMessage(IntPtr context, uint messageType, string? message)
    {
        uint type = messageType >> 24;
        switch (type)
        {
            case MsiApi.MessageProgress:
                _progress.OnProgress(message ?? string.Empty);
                ProgressChanged?.Invoke(_progress.Percent);
                break;
            case MsiApi.MessageActionData:
                _progress.OnActionData();
                ProgressChanged?.Invoke(_progress.Percent);
                break;
            case MsiApi.MessageActionStart:
            case MsiApi.MessageError:
            case MsiApi.MessageWarning:
            case MsiApi.MessageFatalExit:
                if (!string.IsNullOrWhiteSpace(message))
                {
                    Message?.Invoke(message!);
                }

                break;
        }

        // 0: let Windows Installer apply its default for this message (with no UI, an error ends the operation).
        return 0;
    }
}
