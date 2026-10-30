using OmniEurope.Installer.Building;
using OmniEurope.Installer.Product;
using OmniEurope.Installer.Setup;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class SetupPackageBuilderTests : IDisposable
{
    private static readonly string IconSource = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
    private static readonly string HostStandIn = Path.Combine(Environment.SystemDirectory, "expand.exe");

    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_setup_carries_the_msi_license_and_configuration_after_the_host()
    {
        (ProductDefinition definition, string msi) = BuildMsi();
        string license = _temp.WriteFile("license.rtf", @"{\rtf1 License}"u8.ToArray());
        string output = _temp.Combine("out", "App-Setup.exe");

        SetupPackageBuilder.Build(definition, new SetupBuildRequest(HostStandIn, msi, license, IconSource, output));

        using var input = File.OpenRead(output);
        SetupPayload payload = SetupPayload.Read(input);
        payload.Config.ProductName.ShouldBe("App");
        payload.Config.ProductVersion.ShouldBe("2.1.0");
        payload.Config.UpgradeCode.ShouldBe("{6D1C2B6E-4F0A-4C47-9C4E-0E0F3C2D1A55}");
        payload.Config.MainComponentId.ShouldBe(MsiNames.StableGuid(definition.UpgradeCode, "file:Sample.exe"));
        payload.Config.Options.Select(option => option.DefaultChecked).ShouldBe([true, false]);
        payload.Config.LegacyBundleUpgradeCodes.ShouldBe(["{C4D19E62-7B3A-4E85-A0F7-2B6D8C1E5A93}"]);
        string extracted = _temp.Combine("extracted.msi");
        SetupPayload.Extract(input, payload.MsiOffset, payload.MsiLength, extracted);
        File.ReadAllBytes(extracted).ShouldBe(File.ReadAllBytes(msi));
        string extractedLicense = _temp.Combine("extracted.rtf");
        SetupPayload.Extract(input, payload.LicenseOffset, payload.LicenseLength, extractedLicense);
        File.ReadAllText(extractedLicense).ShouldBe(@"{\rtf1 License}");
    }

    [Fact]
    public void The_setup_shows_the_icon_of_the_product()
    {
        (ProductDefinition definition, string msi) = BuildMsi();
        string output = _temp.Combine("App-Setup.exe");

        SetupPackageBuilder.Build(definition, new SetupBuildRequest(HostStandIn, msi, null, IconSource, output));

        var (expectedGroup, expectedImages) = IconResources.ReadFirstGroup(IconSource);
        var (group, images) = IconResources.ReadFirstGroup(output);
        group.ShouldBe(expectedGroup);
        images.Select(image => image.Data).ShouldBe(expectedImages.Select(image => image.Data));
        using var input = File.OpenRead(output);
        SetupPayload.Read(input).LicenseLength.ShouldBe(0);
    }

    [Fact]
    public void The_public_setup_builder_rejects_the_same_invalid_definition_as_json_without_writing_output()
    {
        (ProductDefinition valid, string msi) = BuildMsi();
        ProductDefinition definition = valid with { InstallDirectory = [] };
        string json = _temp.Combine("invalid.json");
        File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(definition,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            }));
        string output = _temp.Combine("out", "App-Setup.exe");

        Should.Throw<InvalidDataException>(() => ProductDefinition.Load(json)).Message.ShouldContain("installDirectory");
        Should.Throw<InvalidDataException>(() => SetupPackageBuilder.Build(definition, new SetupBuildRequest(HostStandIn, msi, null, IconSource, output)))
            .Message.ShouldContain("installDirectory");
        File.Exists(output).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(output)).ShouldBeFalse();
    }

    [Fact]
    public void An_msi_of_another_product_is_refused()
    {
        (ProductDefinition definition, string msi) = BuildMsi();
        ProductDefinition other = definition with { UpgradeCode = Guid.NewGuid() };

        Should.Throw<InvalidDataException>(() => SetupPackageBuilder.Build(other, new SetupBuildRequest(HostStandIn, msi, null, IconSource, _temp.Combine("x.exe"))));
    }

    [Fact]
    public void A_file_without_payload_is_refused()
    {
        using var input = File.OpenRead(HostStandIn);

        Should.Throw<InvalidDataException>(() => SetupPayload.Read(input));
    }

    [Fact]
    public void A_damaged_configuration_reads_as_a_damaged_setup()
    {
        // A valid trailer around a truncated configuration: the host must see the InvalidDataException it maps to 1603.
        byte[] config = System.Text.Encoding.UTF8.GetBytes("{\"ProductName\":\"App\",\"Opti");
        using var input = new MemoryStream();
        input.Write(config);
        input.WriteByte(0x4D);
        var writer = new BinaryWriter(input);
        writer.Write((long)config.Length);
        writer.Write(1L);
        writer.Write(0L);
        writer.Write("OESETUP1"u8.ToArray());
        writer.Flush();

        Should.Throw<InvalidDataException>(() => SetupPayload.Read(input)).Message.ShouldContain("damaged");
    }

    [Fact]
    public void An_icon_source_without_icon_or_resources_and_a_missing_target_are_refused()
    {
        string text = _temp.WriteFile("notes.txt", "not an executable"u8.ToArray());
        string withoutIcon = Path.Combine(Environment.SystemDirectory, "kernel32.dll");

        Should.Throw<InvalidOperationException>(() => IconResources.ReadFirstGroup(text)).Message.ShouldContain("Cannot read the resources");
        Should.Throw<InvalidOperationException>(() => IconResources.ReadFirstGroup(withoutIcon)).Message.ShouldContain("has no icon");
        Should.Throw<InvalidOperationException>(() => IconResources.Copy(IconSource, _temp.Combine("missing.exe"))).Message.ShouldContain("Cannot open");
    }

    [Fact]
    public void A_definition_without_a_setup_section_is_refused()
    {
        (ProductDefinition definition, string msi) = BuildMsi();

        Should.Throw<InvalidDataException>(() => SetupPackageBuilder.Build(definition with { Setup = null }, new SetupBuildRequest(HostStandIn, msi, null, IconSource, _temp.Combine("x.exe"))))
            .Message.ShouldContain("no 'setup' section");
    }

    [Fact]
    public void A_file_shorter_than_the_trailer_or_an_empty_configuration_is_refused()
    {
        using var tiny = new MemoryStream(new byte[10]);
        Should.Throw<InvalidDataException>(() => SetupPayload.Read(tiny));

        byte[] config = "null"u8.ToArray();
        using var input = new MemoryStream();
        input.Write(config);
        input.WriteByte(0x4D);
        var writer = new BinaryWriter(input);
        writer.Write((long)config.Length);
        writer.Write(1L);
        writer.Write(0L);
        writer.Write("OESETUP1"u8.ToArray());
        writer.Flush();
        Should.Throw<InvalidDataException>(() => SetupPayload.Read(input)).Message.ShouldContain("empty");
    }

    [Fact]
    public void Payload_lengths_that_overflow_are_rejected_as_a_damaged_setup()
    {
        using var input = new MemoryStream(new byte[64]);
        input.Position = 32;
        using var writer = new BinaryWriter(input, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(long.MaxValue);
        writer.Write(long.MaxValue);
        writer.Write(0L);
        writer.Write("OESETUP1"u8.ToArray());
        writer.Flush();

        Should.Throw<InvalidDataException>(() => SetupPayload.Read(input));
    }

    [Theory]
    [InlineData(0L, 1L, 0L)]
    [InlineData(1L, 0L, 0L)]
    [InlineData(1L, 1L, -1L)]
    [InlineData(2147483648L, 1L, 0L)]
    [InlineData(1L, long.MaxValue, 0L)]
    [InlineData(1L, 1L, long.MaxValue)]
    public void Impossible_payload_part_lengths_are_refused(long configLength, long msiLength, long licenseLength)
    {
        using var input = new MemoryStream(new byte[64]);
        input.Position = 32;
        using var writer = new BinaryWriter(input, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(configLength);
        writer.Write(msiLength);
        writer.Write(licenseLength);
        writer.Write("OESETUP1"u8.ToArray());
        writer.Flush();

        Should.Throw<InvalidDataException>(() => SetupPayload.Read(input));
    }

    [Fact]
    public void A_truncated_part_is_not_extracted_half_way_silently()
    {
        using var input = new MemoryStream(new byte[5]);

        Should.Throw<EndOfStreamException>(() => SetupPayload.Extract(input, 0, 10, _temp.Combine("part.bin")));
    }

    private (ProductDefinition Definition, string Msi) BuildMsi()
    {
        string source = _temp.Combine("publish");
        Directory.CreateDirectory(source);
        File.Copy(HostStandIn, Path.Combine(source, "Sample.exe"), overwrite: true);
        var definition = new ProductDefinition
        {
            Name = "App",
            Manufacturer = "OmniEurope",
            UpgradeCode = Guid.Parse("6D1C2B6E-4F0A-4C47-9C4E-0E0F3C2D1A55"),
            MainExecutable = "Sample.exe",
            InstallDirectory = ["OmniEurope", "App"],
            DowngradeErrorMessage = "Newer.",
            Properties = new Dictionary<string, string> { ["INSTALL_STARTUP"] = "1", ["INSTALL_DESKTOP_SHORTCUT"] = string.Empty },
            Setup = new SetupDefinition
            {
                SettingsKey = @"Software\OmniEurope\App\Installer",
                Options =
                [
                    new SetupOptionDefinition { Property = "INSTALL_STARTUP", SettingName = "Startup", LabelFr = "Démarrer", LabelEn = "Start" },
                    new SetupOptionDefinition { Property = "INSTALL_DESKTOP_SHORTCUT", SettingName = "DesktopShortcut", LabelFr = "Bureau", LabelEn = "Desktop" },
                ],
                LegacyBundleUpgradeCodes = [Guid.Parse("C4D19E62-7B3A-4E85-A0F7-2B6D8C1E5A93")],
            },
        };
        MsiBuildResult result = MsiPackageBuilder.Build(definition, new MsiBuildRequest(source, "2.1.0", _temp.Combine("app.msi")));
        return (definition, result.OutputPath);
    }
}
