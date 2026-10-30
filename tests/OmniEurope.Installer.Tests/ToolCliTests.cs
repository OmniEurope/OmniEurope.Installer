using System.Reflection;
using OmniEurope.Installer.Setup;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class ToolCliTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private static string Tool => Metadata("InstallerTool");
    private static string Host => Metadata("SetupHost");
    private static string Definition => Path.Combine(AppContext.BaseDirectory, "Samples", "product.json");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_misspelled_license_option_is_refused_before_writing_a_setup()
    {
        string msi = BuildMsi();
        string output = _temp.Combine("rejected.exe");

        (int code, string text) = Run("setup", "--definition", Definition, "--msi", msi, "--host", Host,
            "--icon", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "--licence", _temp.Combine("missing.rtf"), "--output", output);

        code.ShouldBe(2, text);
        text.ShouldContain("Unknown option: --licence");
        File.Exists(output).ShouldBeFalse();
    }

    [Fact]
    public void Valid_msi_and_setup_commands_carry_the_requested_license()
    {
        string msi = BuildMsi();
        string license = _temp.WriteFile("license.rtf", "{\\rtf1 License}"u8.ToArray());
        string output = _temp.Combine("valid.exe");

        (int code, string text) = Run("setup", "--definition", Definition, "--msi", msi, "--host", Host,
            "--icon", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "--license", license, "--output", output);

        code.ShouldBe(0, text);
        using var input = File.OpenRead(output);
        SetupPayload payload = SetupPayload.Read(input);
        payload.Config.ProductName.ShouldBe("Sample App");
        payload.LicenseLength.ShouldBe(new FileInfo(license).Length);
        string extracted = _temp.Combine("extracted.rtf");
        SetupPayload.Extract(input, payload.LicenseOffset, payload.LicenseLength, extracted);
        File.ReadAllBytes(extracted).ShouldBe(File.ReadAllBytes(license));
    }

    [Fact]
    public void An_unknown_msi_option_is_refused_before_reading_inputs()
    {
        string output = _temp.Combine("invalid.msi");
        (int code, string text) = Run("msi", "--definition", "missing.json", "--source", "missing", "--version", "1.0.0",
            "--output", output, "--license", "unexpected.rtf");

        code.ShouldBe(2, text);
        text.ShouldContain("Unknown option: --license");
        File.Exists(output).ShouldBeFalse();
    }

    [Fact]
    public void The_real_host_returns_1603_for_overflowing_payload_lengths_without_installing()
    {
        string damaged = _temp.Combine("damaged.exe");
        File.Copy(Host, damaged);
        using (var file = new FileStream(damaged, FileMode.Append))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write(long.MaxValue);
            writer.Write(long.MaxValue);
            writer.Write(0L);
            writer.Write("OESETUP1"u8.ToArray());
        }

        // The invalid trailer is rejected before MSI extraction, product lookup or elevation.
        ProcessRunner.Run(damaged, "/quiet").ExitCode.ShouldBe(1603);
    }

    private string BuildMsi()
    {
        string source = _temp.Combine("publish");
        Directory.CreateDirectory(source);
        File.Copy(Path.Combine(Environment.SystemDirectory, "expand.exe"), Path.Combine(source, "SampleApp.exe"));
        string output = _temp.Combine("sample.msi");
        (int code, string text) = Run("msi", "--definition", Definition, "--source", source, "--version", "1.0.0", "--output", output);
        code.ShouldBe(0, text);
        File.Exists(output).ShouldBeTrue();
        return output;
    }

    private static (int ExitCode, string Output) Run(params string[] arguments) => ProcessRunner.Run("dotnet", [Tool, .. arguments]);

    private static string Metadata(string key) => typeof(ToolCliTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == key).Value!;
}
