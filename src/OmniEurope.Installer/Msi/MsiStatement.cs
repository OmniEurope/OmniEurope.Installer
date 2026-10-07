namespace OmniEurope.Installer.Msi;

/// <summary>
/// A prepared msi.dll view. One statement can be executed many times with different parameters,
/// which keeps inserting hundreds of rows into a table cheap.
/// </summary>
internal sealed class MsiStatement : IDisposable
{
    private readonly string _sql;
    private uint _view;

    public MsiStatement(uint view, string sql)
    {
        _view = view;
        _sql = sql;
    }

    public void Execute(params object?[] parameters)
    {
        RunWith(parameters);
        MsiNative.MsiViewClose(View);
    }

    public IReadOnlyList<string?[]> ReadAll(params object?[] parameters)
    {
        RunWith(parameters);
        var rows = new List<string?[]>();
        try
        {
            while (TryFetch(out uint record))
            {
                try
                {
                    uint fieldCount = MsiNative.MsiRecordGetFieldCount(record);
                    var row = new string?[fieldCount];
                    for (uint field = 1; field <= fieldCount; field++)
                    {
                        row[field - 1] = MsiNative.MsiRecordIsNull(record, field) ? null : ReadString(record, field);
                    }

                    rows.Add(row);
                }
                finally
                {
                    MsiNative.MsiCloseHandle(record);
                }
            }
        }
        finally
        {
            MsiNative.MsiViewClose(View);
        }

        return rows;
    }

    public byte[] ReadSingleStream(params object?[] parameters)
    {
        RunWith(parameters);
        try
        {
            if (!TryFetch(out uint record))
            {
                throw new InvalidOperationException($"No row returned by '{_sql}'.");
            }

            try
            {
                uint length = MsiNative.MsiRecordDataSize(record, 1);
                byte[] buffer = new byte[length];
                MsiException.ThrowOnError(MsiNative.MsiRecordReadStream(record, 1, buffer, ref length), $"Reading the stream of '{_sql}'");
                return buffer[..(int)length];
            }
            finally
            {
                MsiNative.MsiCloseHandle(record);
            }
        }
        finally
        {
            MsiNative.MsiViewClose(View);
        }
    }

    public void Dispose()
    {
        if (_view != 0)
        {
            MsiNative.MsiCloseHandle(_view);
            _view = 0;
        }
    }

    private uint View => _view != 0 ? _view : throw new ObjectDisposedException(nameof(MsiStatement));

    private void RunWith(object?[] parameters)
    {
        uint record = parameters.Length == 0 ? 0 : CreateRecord(parameters);
        try
        {
            MsiException.ThrowOnError(MsiNative.MsiViewExecute(View, record), $"Executing '{_sql}'");
        }
        finally
        {
            if (record != 0)
            {
                MsiNative.MsiCloseHandle(record);
            }
        }
    }

    private bool TryFetch(out uint record)
    {
        uint result = MsiNative.MsiViewFetch(View, out record);
        if (result == MsiNative.ErrorNoMoreItems)
        {
            return false;
        }

        MsiException.ThrowOnError(result, $"Fetching from '{_sql}'");
        return true;
    }

    private uint CreateRecord(object?[] parameters)
    {
        uint record = MsiNative.MsiCreateRecord((uint)parameters.Length);
        if (record == 0)
        {
            throw new InvalidOperationException($"MsiCreateRecord returned no record for '{_sql}'.");
        }

        try
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                SetField(record, (uint)index + 1, parameters[index]);
            }

            return record;
        }
        catch
        {
            MsiNative.MsiCloseHandle(record);
            throw;
        }
    }

    private void SetField(uint record, uint field, object? value)
    {
        uint result = value switch
        {
            null => MsiNative.Success,
            string text => MsiNative.MsiRecordSetString(record, field, text),
            int number => MsiNative.MsiRecordSetInteger(record, field, number),
            MsiStream stream => MsiNative.MsiRecordSetStream(record, field, stream.FilePath),
            _ => throw new ArgumentException($"Unsupported parameter type {value.GetType().Name} for '{_sql}'.", nameof(value)),
        };
        MsiException.ThrowOnError(result, $"Binding parameter {field} of '{_sql}'");
    }

    private static string ReadString(uint record, uint field)
    {
        uint length = 0;
        char[] probe = new char[1];
        uint result = MsiNative.MsiRecordGetString(record, field, probe, ref length);
        if (result == MsiNative.Success)
        {
            return string.Empty;
        }

        if (result != MsiNative.ErrorMoreData)
        {
            throw new MsiException($"Reading field {field}", result);
        }

        length++;
        char[] buffer = new char[length];
        MsiException.ThrowOnError(MsiNative.MsiRecordGetString(record, field, buffer, ref length), $"Reading field {field}");
        return new string(buffer, 0, (int)length);
    }
}
