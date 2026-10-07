namespace OmniEurope.Installer.Cabinet;

/// <summary>
/// Reads several files as one continuous stream, the way a cabinet folder stores them, and checks
/// that each file still has the length recorded in the cabinet's file table.
/// </summary>
internal sealed class ConcatenatedFileReader : IDisposable
{
    private readonly IReadOnlyList<FileInfo> _files;
    private int _index = -1;
    private FileStream? _current;
    private long _readFromCurrent;

    public ConcatenatedFileReader(IReadOnlyList<FileInfo> files)
    {
        _files = files;
    }

    /// <summary>Fills <paramref name="buffer"/> as far as the remaining data allows; returns the byte count.</summary>
    public int Fill(Span<byte> buffer)
    {
        int filled = 0;
        while (filled < buffer.Length)
        {
            if (_current is null && !OpenNext())
            {
                break;
            }

            int read = _current!.Read(buffer[filled..]);
            if (read == 0)
            {
                CloseCurrent();
                continue;
            }

            filled += read;
            _readFromCurrent += read;
        }

        return filled;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
    }

    private bool OpenNext()
    {
        if (_index + 1 >= _files.Count)
        {
            return false;
        }

        _index++;
        _current = new FileStream(_files[_index].FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
        _readFromCurrent = 0;
        return true;
    }

    private void CloseCurrent()
    {
        FileInfo file = _files[_index];
        _current!.Dispose();
        _current = null;
        if (_readFromCurrent != file.Length)
        {
            throw new InvalidOperationException($"{file.FullName} changed during the build: {_readFromCurrent} bytes read, {file.Length} expected.");
        }
    }
}
