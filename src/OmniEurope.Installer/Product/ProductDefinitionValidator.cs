using System.Text.RegularExpressions;

namespace OmniEurope.Installer.Product;

/// <summary>Rejects a definition that would produce an invalid or ambiguous MSI.</summary>
internal static partial class ProductDefinitionValidator
{
    /// <summary>Properties the package sets itself; a definition cannot override them.</summary>
    public static readonly IReadOnlySet<string> ReservedProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "ALLUSERS", "ARPNOMODIFY", "ARPNOREPAIR", "ARPPRODUCTICON", "INSTALLFOLDER", "Manufacturer", "ProductCode", "ProductLanguage",
        "ProductName", "ProductVersion", "REINSTALLMODE", "SecureCustomProperties", "UpgradeCode",
        "OE_UPGRADE_DETECTED", "OE_DOWNGRADE_DETECTED",
    };

    public static void Validate(ProductDefinition definition, string source)
    {
        var errors = new List<string>();
        ValidateIdentity(errors, definition);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        ValidateShortcuts(errors, ids, definition.Shortcuts);
        ValidateRegistry(errors, ids, definition.RegistryComponents);
        if (definition.Setup is not null)
        {
            ValidateSetup(errors, definition.Setup, definition.Properties);
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException($"{source}: {string.Join("; ", errors)}.");
        }
    }

    private static void ValidateIdentity(List<string> errors, ProductDefinition definition)
    {
        Require(errors, !string.IsNullOrWhiteSpace(definition.Name), "name is empty");
        Require(errors, !string.IsNullOrWhiteSpace(definition.Manufacturer), "manufacturer is empty");
        Require(errors, definition.UpgradeCode != Guid.Empty, "upgradeCode is empty");
        Require(errors, IsRelativeFile(definition.MainExecutable), "mainExecutable must be a file path relative to the published folder");
        Require(errors, definition.InstallDirectory.Count > 0, "installDirectory needs at least one folder");
        Require(errors, definition.InstallDirectory.All(IsFolderName), "installDirectory holds an invalid folder name");
        Require(errors, !string.IsNullOrWhiteSpace(definition.DowngradeErrorMessage), "downgradeErrorMessage is empty");

        foreach (string property in definition.Properties.Keys)
        {
            Require(errors, IsPublicProperty(property), $"property '{property}' must be a public (upper-case) identifier");
            Require(errors, !ReservedProperties.Contains(property), $"property '{property}' is set by the package itself");
        }
    }

    private static void ValidateShortcuts(List<string> errors, HashSet<string> ids, IReadOnlyList<ShortcutDefinition> shortcuts)
    {
        foreach (ShortcutDefinition shortcut in shortcuts)
        {
            ValidateId(errors, ids, shortcut.Id);
            Require(errors, !string.IsNullOrWhiteSpace(shortcut.Name), $"shortcut '{shortcut.Id}' has no name");
            Require(errors, shortcut.Folder is null || shortcut.Location == ShortcutLocation.ProgramMenu, $"shortcut '{shortcut.Id}': only a Start menu shortcut has a folder");
            Require(errors, shortcut.Folder is null || IsFolderName(shortcut.Folder), $"shortcut '{shortcut.Id}' has an invalid folder name");
        }
    }

    private static void ValidateRegistry(List<string> errors, HashSet<string> ids, IReadOnlyList<RegistryComponentDefinition> components)
    {
        foreach (RegistryComponentDefinition component in components)
        {
            ValidateId(errors, ids, component.Id);
            Require(errors, component.Values.Count > 0, $"registry component '{component.Id}' has no value");
            Require(errors, !string.IsNullOrWhiteSpace(component.Key), $"registry component '{component.Id}' has no key");
            foreach (RegistryValueDefinition value in component.Values.Where(value => value.Type == RegistryValueType.Integer))
            {
                Require(errors, int.TryParse(value.Value, out _), $"registry component '{component.Id}': '{value.Value}' is not an integer");
            }
        }
    }

    private static void ValidateSetup(List<string> errors, SetupDefinition setup, IReadOnlyDictionary<string, string> properties)
    {
        Require(errors, !string.IsNullOrWhiteSpace(setup.SettingsKey), "setup.settingsKey is empty");
        Require(errors, setup.LanguageProperty is null || properties.ContainsKey(setup.LanguageProperty),
            $"setup.languageProperty '{setup.LanguageProperty}' is not declared in properties");
        foreach (SetupOptionDefinition option in setup.Options)
        {
            Require(errors, properties.ContainsKey(option.Property), $"setup option '{option.Property}' is not declared in properties");
            Require(errors, !string.IsNullOrWhiteSpace(option.LabelFr) && !string.IsNullOrWhiteSpace(option.LabelEn),
                $"setup option '{option.Property}' needs a French and an English label");
        }
    }

    private static void ValidateId(List<string> errors, HashSet<string> ids, string id)
    {
        Require(errors, IdentifierPattern().IsMatch(id) && id.Length <= 60, $"'{id}' is not a valid identifier (letters, digits, '_' and '.', 60 characters at most)");
        Require(errors, ids.Add(id), $"identifier '{id}' is used twice");
    }

    private static bool IsRelativeFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split('\\', '/').Contains("..");

    private static bool IsFolderName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not "." and not "..";

    private static bool IsPublicProperty(string name) => PublicPropertyPattern().IsMatch(name);

    private static void Require(List<string> errors, bool condition, string error)
    {
        if (!condition)
        {
            errors.Add(error);
        }
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Z_][A-Z0-9_.]*$")]
    private static partial Regex PublicPropertyPattern();
}
