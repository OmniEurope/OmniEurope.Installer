using System.Runtime.InteropServices;

namespace OmniEurope.Installer.Msi;

/// <summary>
/// Windows Installer API (<c>msi.dll</c>, part of Windows). Handles are MSIHANDLE (32-bit unsigned).
/// </summary>
internal static partial class MsiNative
{
    public const uint Success = 0;
    public const uint ErrorMoreData = 234;
    public const uint ErrorNoMoreItems = 259;
    public const int NullInteger = unchecked((int)0x80000000);

    // MsiOpenDatabase persist modes: integer constants passed in the LPCWSTR slot.
    public const nint OpenReadOnly = 0;
    public const nint OpenTransact = 1;
    public const nint OpenCreate = 3;

    // Summary information property types (VARENUM).
    public const uint VtI2 = 2;
    public const uint VtI4 = 3;
    public const uint VtLpstr = 30;
    public const uint VtFiletime = 64;

    [LibraryImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiOpenDatabase(string databasePath, nint persist, out uint database);

    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiDatabaseOpenView(uint database, string query, out uint view);

    [LibraryImport("msi.dll")]
    public static partial uint MsiViewExecute(uint view, uint record);

    [LibraryImport("msi.dll")]
    public static partial uint MsiViewFetch(uint view, out uint record);

    [LibraryImport("msi.dll")]
    public static partial uint MsiViewClose(uint view);

    [LibraryImport("msi.dll")]
    public static partial uint MsiCloseHandle(uint handle);

    [LibraryImport("msi.dll")]
    public static partial uint MsiCreateRecord(uint fieldCount);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordSetStringW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiRecordSetString(uint record, uint field, string? value);

    [LibraryImport("msi.dll")]
    public static partial uint MsiRecordSetInteger(uint record, uint field, int value);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordSetStreamW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiRecordSetStream(uint record, uint field, string filePath);

    [LibraryImport("msi.dll", EntryPoint = "MsiRecordGetStringW")]
    public static partial uint MsiRecordGetString(uint record, uint field, [Out] char[]? buffer, ref uint length);

    [LibraryImport("msi.dll")]
    public static partial int MsiRecordGetInteger(uint record, uint field);

    [LibraryImport("msi.dll")]
    public static partial uint MsiRecordGetFieldCount(uint record);

    [LibraryImport("msi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MsiRecordIsNull(uint record, uint field);

    [LibraryImport("msi.dll")]
    public static partial uint MsiRecordDataSize(uint record, uint field);

    [LibraryImport("msi.dll")]
    public static partial uint MsiRecordReadStream(uint record, uint field, [Out] byte[] buffer, ref uint length);

    [LibraryImport("msi.dll")]
    public static partial uint MsiDatabaseCommit(uint database);

    [LibraryImport("msi.dll", EntryPoint = "MsiDatabaseImportW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiDatabaseImport(uint database, string folderPath, string fileName);

    [LibraryImport("msi.dll", EntryPoint = "MsiGetSummaryInformationW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiGetSummaryInformation(uint database, string? databasePath, uint updateCount, out uint summaryInfo);

    [LibraryImport("msi.dll", EntryPoint = "MsiSummaryInfoSetPropertyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint MsiSummaryInfoSetProperty(uint summaryInfo, uint property, uint dataType, int intValue, ref long fileTime, string? stringValue);

    [LibraryImport("msi.dll", EntryPoint = "MsiSummaryInfoGetPropertyW")]
    public static partial uint MsiSummaryInfoGetProperty(uint summaryInfo, uint property, out uint dataType, out int intValue, out long fileTime, [Out] char[]? buffer, ref uint length);

    [LibraryImport("msi.dll")]
    public static partial uint MsiSummaryInfoPersist(uint summaryInfo);

    [LibraryImport("msi.dll")]
    public static partial uint MsiGetLastErrorRecord();

    [LibraryImport("msi.dll", EntryPoint = "MsiFormatRecordW")]
    public static partial uint MsiFormatRecord(uint install, uint record, [Out] char[]? buffer, ref uint length);
}
