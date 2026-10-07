namespace OmniEurope.Installer.Msi;

/// <summary>
/// A Windows Installer API call failed. The message carries the Win32 error code and, when Windows
/// Installer recorded one, its extended error record.
/// </summary>
public sealed class MsiException : Exception
{
    /// <summary>Creates the exception for a failed call.</summary>
    /// <param name="operation">What was being done, for the message.</param>
    /// <param name="errorCode">The Win32 error code returned by msi.dll.</param>
    public MsiException(string operation, uint errorCode)
        : base($"{operation} failed with Windows Installer error {errorCode}{LastErrorDetail()}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>The Win32 error code returned by msi.dll.</summary>
    public uint ErrorCode { get; }

    internal static void ThrowOnError(uint result, string operation)
    {
        if (result != MsiNative.Success)
        {
            throw new MsiException(operation, result);
        }
    }

    private static string LastErrorDetail()
    {
        uint record = MsiNative.MsiGetLastErrorRecord();
        if (record == 0)
        {
            return ".";
        }

        try
        {
            string? text = FormatRecord(record);
            return text is null ? "." : $": {text}";
        }
        finally
        {
            MsiNative.MsiCloseHandle(record);
        }
    }

    private static string? FormatRecord(uint record)
    {
        uint length = 0;
        MsiNative.MsiFormatRecord(0, record, new char[1], ref length);
        length++;
        char[] buffer = new char[length];
        uint result = MsiNative.MsiFormatRecord(0, record, buffer, ref length);
        return result == MsiNative.Success ? new string(buffer, 0, (int)length) : null;
    }
}
