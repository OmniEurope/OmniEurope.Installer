using System.Text;

namespace OmniEurope.Installer.Msi;

/// <summary>
/// An MSI database opened through msi.dll. Values bound to SQL parameters are <see cref="string"/>,
/// <see cref="int"/>, <see cref="MsiStream"/> or <c>null</c> (SQL NULL).
/// </summary>
internal sealed class MsiDatabase : IDisposable
{
    private uint _handle;

    private MsiDatabase(uint handle)
    {
        _handle = handle;
    }

    public static MsiDatabase Create(string path)
    {
        MsiException.ThrowOnError(MsiNative.MsiOpenDatabase(path, MsiNative.OpenCreate, out uint handle), $"Creating {path}");
        return new MsiDatabase(handle);
    }

    public static MsiDatabase OpenReadOnly(string path)
    {
        MsiException.ThrowOnError(MsiNative.MsiOpenDatabase(path, MsiNative.OpenReadOnly, out uint handle), $"Opening {path}");
        return new MsiDatabase(handle);
    }

    /// <summary>
    /// Sets the database code page. Windows Installer only accepts it through an import of the
    /// special <c>_ForceCodepage</c> table.
    /// </summary>
    public void SetCodepage(int codepage)
    {
        string folder = Directory.CreateTempSubdirectory("oe-msi-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "codepage.idt"), $"\r\n\r\n{codepage}\t_ForceCodepage\r\n", Encoding.ASCII);
            MsiException.ThrowOnError(MsiNative.MsiDatabaseImport(Handle, folder, "codepage.idt"), $"Setting code page {codepage}");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    public void Execute(string sql, params object?[] parameters)
    {
        using var statement = Prepare(sql);
        statement.Execute(parameters);
    }

    public MsiStatement Prepare(string sql)
    {
        MsiException.ThrowOnError(MsiNative.MsiDatabaseOpenView(Handle, sql, out uint view), $"Preparing '{sql}'");
        return new MsiStatement(view, sql);
    }

    /// <summary>Runs a SELECT and returns every row, each field as its string form or null.</summary>
    public IReadOnlyList<string?[]> Query(string sql, params object?[] parameters)
    {
        using var statement = Prepare(sql);
        return statement.ReadAll(parameters);
    }

    /// <summary>Reads the binary column of the single row a SELECT returns.</summary>
    public byte[] ReadStream(string sql, params object?[] parameters)
    {
        using var statement = Prepare(sql);
        return statement.ReadSingleStream(parameters);
    }

    public void SetSummaryInformation(IReadOnlyDictionary<uint, object> properties)
    {
        MsiException.ThrowOnError(
            MsiNative.MsiGetSummaryInformation(Handle, null, (uint)properties.Count, out uint summary),
            "Opening the summary information");
        try
        {
            foreach ((uint property, object value) in properties)
            {
                MsiException.ThrowOnError(SetSummaryProperty(summary, property, value), $"Setting summary property {property}");
            }

            MsiException.ThrowOnError(MsiNative.MsiSummaryInfoPersist(summary), "Saving the summary information");
        }
        finally
        {
            MsiNative.MsiCloseHandle(summary);
        }
    }

    /// <summary>Reads one summary property: a string, an int, or a <see cref="DateTime"/> (UTC).</summary>
    public object? GetSummaryProperty(uint property)
    {
        MsiException.ThrowOnError(MsiNative.MsiGetSummaryInformation(Handle, null, 0, out uint summary), "Opening the summary information");
        try
        {
            uint length = 0;
            uint result = MsiNative.MsiSummaryInfoGetProperty(summary, property, out uint type, out int intValue, out long fileTime, null, ref length);
            if (type == MsiNative.VtLpstr)
            {
                length++;
                char[] buffer = new char[length];
                result = MsiNative.MsiSummaryInfoGetProperty(summary, property, out _, out _, out _, buffer, ref length);
                MsiException.ThrowOnError(result, $"Reading summary property {property}");
                return new string(buffer, 0, (int)length);
            }

            MsiException.ThrowOnError(result, $"Reading summary property {property}");
            return type switch
            {
                MsiNative.VtI2 or MsiNative.VtI4 => intValue,
                MsiNative.VtFiletime => DateTime.FromFileTimeUtc(fileTime),
                _ => null,
            };
        }
        finally
        {
            MsiNative.MsiCloseHandle(summary);
        }
    }

    public void Commit()
    {
        MsiException.ThrowOnError(MsiNative.MsiDatabaseCommit(Handle), "Committing the database");
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            MsiNative.MsiCloseHandle(_handle);
            _handle = 0;
        }
    }

    private uint Handle => _handle != 0 ? _handle : throw new ObjectDisposedException(nameof(MsiDatabase));

    private static uint SetSummaryProperty(uint summary, uint property, object value)
    {
        long fileTime = 0;
        switch (value)
        {
            case string text:
                return MsiNative.MsiSummaryInfoSetProperty(summary, property, MsiNative.VtLpstr, 0, ref fileTime, text);
            case short number:
                return MsiNative.MsiSummaryInfoSetProperty(summary, property, MsiNative.VtI2, number, ref fileTime, null);
            case int number:
                return MsiNative.MsiSummaryInfoSetProperty(summary, property, MsiNative.VtI4, number, ref fileTime, null);
            case DateTime time:
                fileTime = time.ToFileTimeUtc();
                return MsiNative.MsiSummaryInfoSetProperty(summary, property, MsiNative.VtFiletime, 0, ref fileTime, null);
            default:
                throw new ArgumentException($"Unsupported summary value type {value.GetType().Name} for property {property}.", nameof(value));
        }
    }
}
