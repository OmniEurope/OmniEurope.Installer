using System;
using System.IO;
using System.Security.Cryptography;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// An extracted file held open for the whole setup run: others may read it (Windows Installer opens the package
/// read-only), nobody may write, replace, rename or delete it while the handle lives. Its SHA-256 is checked when it
/// is locked and again by <see cref="Verify"/> right before use.
/// </summary>
internal sealed class LockedFile : IDisposable
{
    private readonly FileStream _stream;
    private readonly byte[] _sha256;

    private LockedFile(FileStream stream, byte[] sha256)
    {
        _stream = stream;
        _sha256 = sha256;
    }

    /// <summary>The full path of the file.</summary>
    public string Path => _stream.Name;

    /// <summary>
    /// Opens <paramref name="path"/> read-only, denying writes and deletion, and records its SHA-256. With
    /// <paramref name="expectedSha256"/>, the content must match it: the file was written, closed, then reopened here,
    /// so this check covers anything done to it in between.
    /// </summary>
    /// <exception cref="InvalidDataException">The content does not match <paramref name="expectedSha256"/>.</exception>
    public static LockedFile Lock(string path, byte[]? expectedSha256)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            byte[] sha256 = Sha256(stream);
            if (expectedSha256 is not null && !SameHash(sha256, expectedSha256))
            {
                throw new InvalidDataException($"{path} does not match the file carried by the setup program.");
            }

            return new LockedFile(stream, sha256);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Hashes the file again through the held handle and compares with the hash recorded when locked.</summary>
    /// <exception cref="InvalidDataException">The content changed.</exception>
    public void Verify()
    {
        if (!SameHash(Sha256(_stream), _sha256))
        {
            throw new InvalidDataException($"{Path} changed after it was extracted: the setup stops.");
        }
    }

    /// <summary>SHA-256 of the whole of <paramref name="stream"/>, read from its start.</summary>
    public static byte[] Sha256(Stream stream)
    {
        stream.Position = 0;
        using var algorithm = SHA256.Create();
        return algorithm.ComputeHash(stream);
    }

    /// <summary>Byte-wise equality of two hashes.</summary>
    public static bool SameHash(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        int difference = 0;
        for (int index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }

        return difference == 0;
    }

    /// <summary>Releases the handle: the file can be deleted afterwards.</summary>
    public void Dispose() => _stream.Dispose();
}
