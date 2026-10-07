using OmniEurope.Installer.Product;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class ProductDefinitionTests
{
    private static readonly string SampleDefinition = Path.Combine(AppContext.BaseDirectory, "Samples", "product.json");

    [Fact]
    public void A_complete_definition_with_comments_loads_every_section()
    {
        ProductDefinition definition = ProductDefinition.Load(SampleDefinition);

        definition.UpgradeCode.ShouldBe(Guid.Parse("3B8E2F14-6C0D-4A7B-9E51-2D4C7A9F0B63"));
        definition.InstallDirectory.ShouldBe(["OmniEurope", "Sample App"]);
        definition.Shortcuts.Select(shortcut => shortcut.Id).ShouldBe(["DesktopShortcut", "StartMenuShortcut"]);
        definition.RegistryComponents.Single(component => component.Id == "StartupRegistryEntry").Condition.ShouldBe("INSTALL_STARTUP");
        definition.Properties["INSTALL_LANGUAGE"].ShouldBe("en");
        definition.Setup.ShouldNotBeNull().Options.Select(option => option.SettingName).ShouldBe(["DesktopShortcut", "Startup"]);
    }

    [Fact]
    public void An_unknown_field_is_rejected_rather_than_ignored()
    {
        using var temp = new TempFolder();
        string json = File.ReadAllText(SampleDefinition).Replace("\"name\": \"Sample App\",", "\"name\": \"Sample App\", \"nmae\": \"typo\",");
        string path = temp.WriteFile("product.json", System.Text.Encoding.UTF8.GetBytes(json));

        Should.Throw<InvalidDataException>(() => ProductDefinition.Load(path)).Message.ShouldContain("nmae");
    }

    [Fact]
    public void Reserved_properties_and_duplicate_ids_are_reported_together()
    {
        using var temp = new TempFolder();
        string json = """
        {
          "name": "App", "manufacturer": "OmniEurope", "upgradeCode": "11111111-2222-3333-4444-555555555555",
          "mainExecutable": "App.exe", "installDirectory": ["OmniEurope", "App"], "downgradeErrorMessage": "Newer.",
          "properties": { "ProductCode": "x" },
          "registryComponents": [
            { "id": "Same", "root": "CurrentUser", "key": "Software_A", "values": [ { "type": "Integer", "value": "abc" } ] },
            { "id": "Same", "root": "CurrentUser", "key": "Software_B", "values": [ { "type": "String", "value": "v" } ] }
          ]
        }
        """;
        string path = temp.WriteFile("product.json", System.Text.Encoding.UTF8.GetBytes(json));

        string message = Should.Throw<InvalidDataException>(() => ProductDefinition.Load(path)).Message;

        message.ShouldContain("'ProductCode'");
        message.ShouldContain("'Same' is used twice");
        message.ShouldContain("'abc' is not an integer");
    }
}
