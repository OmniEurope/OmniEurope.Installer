using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniEurope.Installer.Setup;

/// <summary>The part of the Windows Installer API (msi.dll) a setup program needs to run an MSI.</summary>
internal static class MsiApi
{
    public const uint Success = 0;
    public const uint UserExit = 1602;
    public const uint InstallFailure = 1603;
    public const uint MoreData = 234;
    public const uint NoMoreItems = 259;

    public const int UiLevelNone = 2;
    public const int InstallLevelDefault = 0;
    public const int InstallStateAbsent = 2;

    // INSTALLLOGMODE flags: what the external handler receives, and what the log records.
    public const uint LogFatalExit = 1 << 0;
    public const uint LogError = 1 << 1;
    public const uint LogWarning = 1 << 2;
    public const uint LogUser = 1 << 3;
    public const uint LogActionStart = 1 << 8;
    public const uint LogActionData = 1 << 9;
    public const uint LogProgress = 1 << 10;
    public const uint LogVerbose = 0x1FFF;
    public const uint LogAppendFlushEachLine = 1 | 2;

    // INSTALLMESSAGE types (high byte of the message type).
    public const uint MessageFatalExit = 0x00;
    public const uint MessageError = 0x01;
    public const uint MessageWarning = 0x02;
    public const uint MessageActionStart = 0x08;
    public const uint MessageActionData = 0x09;
    public const uint MessageProgress = 0x0A;

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    public delegate int InstallUiHandler(IntPtr context, uint messageType, [MarshalAs(UnmanagedType.LPWStr)] string? message);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiInstallProductW")]
    public static extern uint MsiInstallProduct(string packagePath, string commandLine);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiConfigureProductExW")]
    public static extern uint MsiConfigureProductEx(string productCode, int installLevel, int installState, string commandLine);

    [DllImport("msi.dll")]
    public static extern int MsiSetInternalUI(int uiLevel, IntPtr window);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiSetExternalUIW")]
    public static extern InstallUiHandler? MsiSetExternalUI(InstallUiHandler? handler, uint messageFilter, IntPtr context);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiEnableLogW")]
    public static extern uint MsiEnableLog(uint logMode, string? logFile, uint logAttributes);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiEnumRelatedProductsW")]
    public static extern uint MsiEnumRelatedProducts(string upgradeCode, uint reserved, uint index, StringBuilder productCode);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetProductInfoW")]
    public static extern uint MsiGetProductInfo(string productCode, string property, StringBuilder? value, ref uint length);

    [DllImport("msi.dll", CharSet = CharSet.Unicode, EntryPoint = "MsiGetComponentPathW")]
    public static extern int MsiGetComponentPath(string productCode, string componentId, StringBuilder? path, ref uint length);
}
