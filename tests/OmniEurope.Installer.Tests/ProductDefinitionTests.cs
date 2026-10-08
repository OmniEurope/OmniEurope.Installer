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

    [Theory]
    [InlineData("\"installDirectory\": [\"OmniEurope\", \"App\"]", "\"installDirectory\": null")]
    [InlineData("\"name\": \"App\"", "\"name\": null")]
    public void A_null_for_a_required_field_is_a_load_error(string field, string replacement)
    {
        string message = LoadInvalid(MinimalDefinition.Replace(field, replacement));

        message.ShouldContain(replacement.Split(':')[0].Trim('"'));
    }

    [Fact]
    public void A_null_entry_in_a_list_is_a_load_error()
    {
        // With a setup section, the settings-root check walks the same list and must skip the null as well.
        string message = LoadInvalid(MinimalDefinition.Replace("\"registryComponents\": [",
            "\"setup\": { \"settingsKey\": \"Software\\\\App\" }, \"registryComponents\": [ null,"));

        message.ShouldContain("registryComponents holds a null entry");
    }

    [Theory]
    [InlineData("\"root\": 99")]
    [InlineData("\"root\": 1")]
    [InlineData("\"root\": \"Elsewhere\"")]
    public void A_registry_root_must_be_a_known_name(string root)
    {
        LoadInvalid(MinimalDefinition.Replace("\"root\": \"CurrentUser\"", root)).ShouldContain("root");
    }

    [Fact]
    public void The_setup_settings_must_follow_the_installation_context()
    {
        string json = MinimalDefinition
            .Replace("\"key\": \"Software_A\"", "\"key\": \"Software\\\\App\\\\Installer\"")
            .Replace("\"registryComponents\"", "\"setup\": { \"settingsKey\": \"Software\\\\App\\\\Installer\" }, \"registryComponents\"");

        LoadInvalid(json).ShouldContain("'Settings' records setup.settingsKey under CurrentUser; use Installation");
    }

    [Theory]
    [InlineData("\"mainExecutable\": \"App.exe\"", "\"mainExecutable\": \"..\\\\App.exe\"", "mainExecutable")]
    [InlineData("\"mainExecutable\": \"App.exe\"", "\"mainExecutable\": \"C:\\\\App.exe\"", "mainExecutable")]
    [InlineData("\"installDirectory\": [\"OmniEurope\", \"App\"]", "\"installDirectory\": []", "at least one folder")]
    [InlineData("\"installDirectory\": [\"OmniEurope\", \"App\"]", "\"installDirectory\": [\"OmniEurope\", \"..\"]", "invalid folder name")]
    [InlineData("\"installDirectory\": [\"OmniEurope\", \"App\"]", "\"installDirectory\": [\"Omni:Europe\"]", "invalid folder name")]
    [InlineData("\"installDirectory\": [\"OmniEurope\", \"App\"]", "\"installDirectory\": [\".\"]", "invalid folder name")]
    [InlineData("\"id\": \"Settings\"", "\"id\": \"9 settings\"", "not a valid identifier")]
    public void A_path_folder_or_identifier_that_would_break_the_package_is_refused(string field, string replacement, string expected)
    {
        LoadInvalid(MinimalDefinition.Replace(field, replacement)).ShouldContain(expected);
    }

    [Fact]
    public void A_file_holding_json_null_is_not_a_definition()
    {
        LoadInvalid("null").ShouldContain("holds no product definition");
    }

    [Fact]
    public void A_setup_option_needs_a_declared_property_and_both_labels()
    {
        string json = MinimalDefinition.Replace("\"registryComponents\"",
            "\"setup\": { \"settingsKey\": \"Software\\\\App\", \"options\": [ { \"property\": \"UNDECLARED\", \"settingName\": \"S\", \"labelFr\": \"\", \"labelEn\": \"Label\" } ] }, \"registryComponents\"");

        string message = LoadInvalid(json);

        message.ShouldContain("setup option 'UNDECLARED' is not declared in properties");
        message.ShouldContain("needs a French and an English label");
    }

    private const string MinimalDefinition = """
        {
          "name": "App", "manufacturer": "OmniEurope", "upgradeCode": "11111111-2222-3333-4444-555555555555",
          "mainExecutable": "App.exe", "installDirectory": ["OmniEurope", "App"], "downgradeErrorMessage": "Newer.",
          "registryComponents": [
            { "id": "Settings", "root": "CurrentUser", "key": "Software_A", "values": [ { "type": "String", "value": "v" } ] }
          ]
        }
        """;

    /// <summary>Loads <paramref name="json"/>, which must fail as an invalid definition (never another exception), and returns the message.</summary>
    private static string LoadInvalid(string json)
    {
        using var temp = new TempFolder();
        string path = temp.WriteFile("product.json", System.Text.Encoding.UTF8.GetBytes(json));

        return Should.Throw<InvalidDataException>(() => ProductDefinition.Load(path)).Message;
    }
}
