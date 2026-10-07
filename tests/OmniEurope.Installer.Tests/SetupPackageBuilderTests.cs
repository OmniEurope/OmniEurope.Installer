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
        payload.Config.Options.Single().DefaultChecked.ShouldBeTrue();
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
            Properties = new Dictionary<string, string> { ["INSTALL_STARTUP"] = "1" },
            Setup = new SetupDefinition
            {
                SettingsKey = @"Software\OmniEurope\App\Installer",
                Options = [new SetupOptionDefinition { Property = "INSTALL_STARTUP", SettingName = "Startup", LabelFr = "Démarrer", LabelEn = "Start" }],
                LegacyBundleUpgradeCodes = [Guid.Parse("C4D19E62-7B3A-4E85-A0F7-2B6D8C1E5A93")],
            },
        };
        MsiBuildResult result = MsiPackageBuilder.Build(definition, new MsiBuildRequest(source, "2.1.0", _temp.Combine("app.msi")));
        return (definition, result.OutputPath);
    }
}
