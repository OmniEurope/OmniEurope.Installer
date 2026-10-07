using OmniEurope.Installer.Cabinet;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class CabinetWriterTests
{
    [Fact]
    public void Windows_expand_restores_every_file_byte_for_byte()
    {
        using var temp = new TempFolder();
        var random = new Random(42);
        byte[] incompressible = new byte[CabinetWriter.BlockSize * 3 + 17];
        random.NextBytes(incompressible);
        byte[] repetitive = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("OmniEurope installer ", 9000)));
        var contents = new Dictionary<string, byte[]>
        {
            ["fEmpty"] = [],
            ["fSmall"] = "hello"u8.ToArray(),
            ["fRandom"] = incompressible,
            ["fRepeat"] = repetitive,
        };
        var entries = contents.Select(pair => new CabinetEntry(pair.Key, temp.WriteFile(Path.Combine("in", pair.Key), pair.Value))).ToList();
        string cabinet = temp.Combine("test.cab");

        CabinetWriter.Write(cabinet, entries);

        string output = temp.Combine("out");
        Directory.CreateDirectory(output);
        (int exitCode, string log) = ProcessRunner.Run("expand.exe", cabinet, "-F:*", output);
        exitCode.ShouldBe(0, log);
        foreach ((string name, byte[] expected) in contents)
        {
            File.ReadAllBytes(Path.Combine(output, name)).ShouldBe(expected, $"{name} differs after extraction");
        }
    }

    [Fact]
    public void Incompressible_block_falls_back_to_a_stored_deflate_block_within_the_MSZIP_limit()
    {
        byte[] data = new byte[CabinetWriter.BlockSize];
        new Random(7).NextBytes(data);

        byte[] block = CabinetWriter.CompressBlock(data);

        block.Length.ShouldBeLessThanOrEqualTo(CabinetWriter.BlockSize + 12);
        block[..2].ShouldBe("CK"u8.ToArray());
    }

    [Fact]
    public void Checksum_xors_little_endian_words_and_packs_the_tail_high_byte_first()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0xAA, 0xBB, 0xCC];

        uint checksum = CabinetWriter.Checksum(data, 0);

        checksum.ShouldBe(0x04030201u ^ 0x00AABBCCu);
    }

    [Fact]
    public void An_empty_entry_list_is_rejected()
    {
        using var temp = new TempFolder();

        Should.Throw<ArgumentException>(() => CabinetWriter.Write(temp.Combine("x.cab"), []));
    }
}
