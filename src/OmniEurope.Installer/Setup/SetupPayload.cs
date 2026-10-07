using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace OmniEurope.Installer.Setup;

// Shared source (library net10.0 and setup host net472).
//
// Layout of a setup executable: the host program, then the payload appended at its end:
//   [host exe][config JSON][MSI][license RTF][trailer]
// trailer = config length, MSI length, license length (three little-endian Int64) + 8-byte magic.
// Data appended after the PE image is ignored by the Windows loader. Authenticode signing appends its
// certificate table after this trailer, so a signed setup would need the reader to skip it.

/// <summary>Where the carried parts sit inside a setup executable.</summary>
public sealed class SetupPayload
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("OESETUP1");
    private const int TrailerLength = 3 * sizeof(long) + 8;

    private SetupPayload(SetupConfig config, long msiOffset, long msiLength, long licenseOffset, long licenseLength)
    {
        Config = config;
        MsiOffset = msiOffset;
        MsiLength = msiLength;
        LicenseOffset = licenseOffset;
        LicenseLength = licenseLength;
    }

    /// <summary>The product configuration.</summary>
    public SetupConfig Config { get; }

    /// <summary>Offset of the MSI in the executable.</summary>
    public long MsiOffset { get; }

    /// <summary>Length of the MSI.</summary>
    public long MsiLength { get; }

    /// <summary>Offset of the license RTF.</summary>
    public long LicenseOffset { get; }

    /// <summary>Length of the license RTF; zero when none is carried.</summary>
    public long LicenseLength { get; }

    /// <summary>Appends the payload to <paramref name="output"/>, positioned at the end of the host program.</summary>
    public static void Append(Stream output, SetupConfig config, string msiPath, string? licensePath)
    {
        byte[] json = Serialize(config);
        output.Write(json, 0, json.Length);
        long msiLength = CopyFile(msiPath, output);
        long licenseLength = licensePath is null ? 0 : CopyFile(licensePath, output);
        var writer = new BinaryWriter(output, Encoding.UTF8);
        writer.Write((long)json.Length);
        writer.Write(msiLength);
        writer.Write(licenseLength);
        writer.Write(Magic);
        writer.Flush();
    }

    /// <summary>Reads the payload of a setup executable.</summary>
    /// <exception cref="InvalidDataException">The file carries no payload.</exception>
    public static SetupPayload Read(Stream input)
    {
        if (input.Length < TrailerLength)
        {
            throw new InvalidDataException("The setup program carries no product.");
        }

        input.Position = input.Length - TrailerLength;
        var reader = new BinaryReader(input, Encoding.UTF8);
        long configLength = reader.ReadInt64();
        long msiLength = reader.ReadInt64();
        long licenseLength = reader.ReadInt64();
        byte[] magic = reader.ReadBytes(Magic.Length);
        long payloadStart = input.Length - TrailerLength - licenseLength - msiLength - configLength;
        if (!IsMagic(magic) || configLength <= 0 || msiLength <= 0 || licenseLength < 0 || payloadStart < 0)
        {
            throw new InvalidDataException("The setup program carries no product.");
        }

        input.Position = payloadStart;
        SetupConfig config = Deserialize(reader.ReadBytes((int)configLength));
        long msiOffset = payloadStart + configLength;
        return new SetupPayload(config, msiOffset, msiLength, msiOffset + msiLength, licenseLength);
    }

    /// <summary>Copies <paramref name="length"/> bytes from <paramref name="offset"/> into a new file.</summary>
    public static void Extract(Stream input, long offset, long length, string destination)
    {
        input.Position = offset;
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[81920];
        long remaining = length;
        while (remaining > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("The setup program is truncated.");
            }

            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    /// <summary>Serializes the configuration exactly as the setup host reads it.</summary>
    public static byte[] Serialize(SetupConfig config)
    {
        using var buffer = new MemoryStream();
        new DataContractJsonSerializer(typeof(SetupConfig)).WriteObject(buffer, config);
        return buffer.ToArray();
    }

    private static SetupConfig Deserialize(byte[] json)
    {
        using var buffer = new MemoryStream(json);
        return (SetupConfig?)new DataContractJsonSerializer(typeof(SetupConfig)).ReadObject(buffer)
            ?? throw new InvalidDataException("The setup configuration is empty.");
    }

    private static long CopyFile(string path, Stream output)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        input.CopyTo(output);
        return input.Length;
    }

    private static bool IsMagic(byte[] value)
    {
        if (value.Length != Magic.Length)
        {
            return false;
        }

        for (int index = 0; index < Magic.Length; index++)
        {
            if (value[index] != Magic[index])
            {
                return false;
            }
        }

        return true;
    }
}
