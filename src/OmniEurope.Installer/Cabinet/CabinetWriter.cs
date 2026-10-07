using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OmniEurope.Installer.Cabinet;

/// <summary>
/// Writes a single-folder Microsoft cabinet (MS-CAB) compressed with MSZIP: every 32 KiB block is
/// "CK" followed by a raw deflate stream produced by .NET. Each block is compressed on its own: a block
/// that never refers to earlier data decodes identically whether or not the decoder keeps the previous
/// block's window.
/// </summary>
internal static class CabinetWriter
{
    public const int BlockSize = 32768;

    private const int HeaderSize = 36;
    private const int FolderSize = 8;
    private const int FileFixedSize = 16;
    private const int DataHeaderSize = 8;
    private const ushort CompressionMsZip = 1;
    private const ushort NameIsUtf8 = 0x80;

    public static void Write(string cabinetPath, IReadOnlyList<CabinetEntry> entries)
    {
        if (entries.Count == 0 || entries.Count > ushort.MaxValue)
        {
            throw new ArgumentException($"A cabinet holds between 1 and {ushort.MaxValue} files, not {entries.Count}.", nameof(entries));
        }

        var files = entries.Select(entry => (Entry: entry, Info: new FileInfo(entry.SourcePath))).ToList();
        long totalBytes = files.Sum(file => file.Info.Length);
        long blockCount = Math.Max(1, (totalBytes + BlockSize - 1) / BlockSize);
        if (totalBytes > uint.MaxValue || blockCount > ushort.MaxValue)
        {
            throw new ArgumentException($"{totalBytes} bytes exceed what a single cabinet folder can hold.", nameof(entries));
        }

        using var output = new FileStream(cabinetPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        int fileTableSize = files.Sum(file => FileFixedSize + EncodeName(file.Entry.Name).Length + 1);
        uint firstDataOffset = (uint)(HeaderSize + FolderSize + fileTableSize);

        WriteHeader(writer, (ushort)files.Count);
        writer.Write(firstDataOffset);
        writer.Write((ushort)blockCount);
        writer.Write(CompressionMsZip);

        uint folderOffset = 0;
        foreach ((CabinetEntry entry, FileInfo info) in files)
        {
            WriteFileEntry(writer, entry.Name, info, folderOffset);
            folderOffset += (uint)info.Length;
        }

        WriteDataBlocks(writer, files.Select(file => file.Info).ToList(), blockCount);

        writer.Flush();
        uint cabinetSize = (uint)output.Length;
        output.Position = 8;
        writer.Write(cabinetSize);
    }

    private static void WriteHeader(BinaryWriter writer, ushort fileCount)
    {
        writer.Write("MSCF"u8);
        writer.Write(0u);              // reserved1
        writer.Write(0u);              // cbCabinet, patched once the size is known
        writer.Write(0u);              // reserved2
        writer.Write((uint)(HeaderSize + FolderSize)); // coffFiles
        writer.Write(0u);              // reserved3
        writer.Write((byte)3);         // versionMinor
        writer.Write((byte)1);         // versionMajor
        writer.Write((ushort)1);       // cFolders
        writer.Write(fileCount);
        writer.Write((ushort)0);       // flags
        writer.Write((ushort)0);       // setID
        writer.Write((ushort)0);       // iCabinet
    }

    private static void WriteFileEntry(BinaryWriter writer, string name, FileInfo info, uint folderOffset)
    {
        byte[] encodedName = EncodeName(name);
        (ushort date, ushort time) = ToDosDateTime(info.LastWriteTime);
        writer.Write((uint)info.Length);
        writer.Write(folderOffset);
        writer.Write((ushort)0);       // iFolder
        writer.Write(date);
        writer.Write(time);
        writer.Write(IsAscii(name) ? (ushort)0 : NameIsUtf8);
        writer.Write(encodedName);
        writer.Write((byte)0);
    }

    private static void WriteDataBlocks(BinaryWriter writer, IReadOnlyList<FileInfo> files, long expectedBlocks)
    {
        byte[] block = new byte[BlockSize];
        Span<byte> sizes = stackalloc byte[4];
        long written = 0;
        using var source = new ConcatenatedFileReader(files);
        int filled;
        while ((filled = source.Fill(block)) > 0 || written == 0)
        {
            byte[] compressed = CompressBlock(block.AsSpan(0, filled));
            BinaryPrimitives.WriteUInt16LittleEndian(sizes, (ushort)compressed.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(sizes[2..], (ushort)filled);
            writer.Write(Checksum(sizes, Checksum(compressed, 0)));
            writer.Write(sizes);
            writer.Write(compressed);
            written++;
            if (filled == 0)
            {
                break;
            }
        }

        if (written != expectedBlocks)
        {
            throw new InvalidOperationException($"Wrote {written} cabinet blocks, expected {expectedBlocks}: a source file changed during the build.");
        }
    }

    /// <summary>One MSZIP block: "CK" then deflate data, falling back to a stored deflate block when compression would grow the data.</summary>
    internal static byte[] CompressBlock(ReadOnlySpan<byte> data)
    {
        using var buffer = new MemoryStream();
        buffer.Write("CK"u8);
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(data);
        }

        // A stored block costs 5 bytes of framing; anything larger means deflate expanded the data.
        if (buffer.Length <= data.Length + 7)
        {
            return buffer.ToArray();
        }

        byte[] stored = new byte[2 + 5 + data.Length];
        stored[0] = (byte)'C';
        stored[1] = (byte)'K';
        stored[2] = 0x01; // BFINAL = 1, BTYPE = 00 (stored)
        BinaryPrimitives.WriteUInt16LittleEndian(stored.AsSpan(3), (ushort)data.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(stored.AsSpan(5), (ushort)~data.Length);
        data.CopyTo(stored.AsSpan(7));
        return stored;
    }

    /// <summary>The MS-CAB checksum: XOR of little-endian 32-bit words, the tail packed big-end first.</summary>
    internal static uint Checksum(ReadOnlySpan<byte> data, uint seed)
    {
        uint checksum = seed;
        int words = data.Length / 4;
        for (int index = 0; index < words; index++)
        {
            checksum ^= BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(index * 4, 4));
        }

        uint tail = 0;
        ReadOnlySpan<byte> rest = data[(words * 4)..];
        foreach (byte value in rest)
        {
            tail = (tail << 8) | value;
        }

        return checksum ^ tail;
    }

    private static (ushort Date, ushort Time) ToDosDateTime(DateTime value)
    {
        DateTime clamped = value.Year < 1980 ? new DateTime(1980, 1, 1) : value;
        ushort date = (ushort)(((clamped.Year - 1980) << 9) | (clamped.Month << 5) | clamped.Day);
        ushort time = (ushort)((clamped.Hour << 11) | (clamped.Minute << 5) | (clamped.Second / 2));
        return (date, time);
    }

    private static byte[] EncodeName(string name) => IsAscii(name) ? Encoding.ASCII.GetBytes(name) : Encoding.UTF8.GetBytes(name);

    private static bool IsAscii(string name) => name.All(char.IsAscii);
}
