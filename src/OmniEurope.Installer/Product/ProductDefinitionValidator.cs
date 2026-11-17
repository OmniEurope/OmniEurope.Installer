using System.Text;
using System.Text.RegularExpressions;

namespace OmniEurope.Installer.Product;

/// <summary>Rejects a definition that would produce an invalid or ambiguous MSI.</summary>
internal static partial class ProductDefinitionValidator
{
    /// <summary>Properties the package sets itself; a definition cannot override them.</summary>
    public static readonly IReadOnlySet<string> ReservedProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        "ALLUSERS", "ARPNOMODIFY", "ARPNOREPAIR", "ARPPRODUCTICON", "INSTALLFOLDER", "Manufacturer", "MSIINSTALLPERUSER", "ProductCode", "ProductLanguage",
        "ProductName", "ProductVersion", "REINSTALLMODE", "SecureCustomProperties", "UpgradeCode",
        "OE_UPGRADE_DETECTED", "OE_DOWNGRADE_DETECTED",
    };

    public static void Validate(ProductDefinition definition, string source)
    {
        var errors = new List<string>();
        ValidateIdentity(errors, definition);
        ValidateCodePage(errors, definition);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        ValidateShortcuts(errors, ids, definition.Shortcuts);
        ValidateRegistry(errors, ids, definition.RegistryComponents);
        if (definition.Setup is not null)
        {
            ValidateSetup(errors, definition.Setup, definition.Properties);
            ValidateSettingsRoot(errors, definition.Setup, definition.RegistryComponents);
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
        Require(errors, definition.InstallDirectory.All(IsFileName), "installDirectory holds an invalid folder name");
        Require(errors, !string.IsNullOrWhiteSpace(definition.DowngradeErrorMessage), "downgradeErrorMessage is empty");
        Require(errors, definition.Exclude.All(pattern => pattern is not null), "exclude holds a null entry");

        foreach ((string property, string value) in definition.Properties)
        {
            Require(errors, IsPublicProperty(property), $"property '{property}' must be a public (upper-case) identifier");
            Require(errors, !ReservedProperties.Contains(property), $"property '{property}' is set by the package itself");
            Require(errors, value is not null, $"property '{property}' has no value");
        }
    }

    private static void ValidateShortcuts(List<string> errors, HashSet<string> ids, IReadOnlyList<ShortcutDefinition> shortcuts)
    {
        foreach (ShortcutDefinition shortcut in Entries(errors, shortcuts, "shortcuts"))
        {
            ValidateId(errors, ids, shortcut.Id);
            Require(errors, Enum.IsDefined(shortcut.Location), $"shortcut '{shortcut.Id}' has an unknown location");
            Require(errors, IsFileName(shortcut.Name), $"shortcut '{shortcut.Id}' has an invalid name (it must be a valid Windows file name)");
            Require(errors, shortcut.Folder is null || shortcut.Location == ShortcutLocation.ProgramMenu, $"shortcut '{shortcut.Id}': only a Start menu shortcut has a folder");
            Require(errors, shortcut.Folder is null || IsFileName(shortcut.Folder), $"shortcut '{shortcut.Id}' has an invalid folder name");
        }
    }

    private static void ValidateRegistry(List<string> errors, HashSet<string> ids, IReadOnlyList<RegistryComponentDefinition> components)
    {
        foreach (RegistryComponentDefinition component in Entries(errors, components, "registryComponents"))
        {
            ValidateId(errors, ids, component.Id);
            Require(errors, Enum.IsDefined(component.Root), $"registry component '{component.Id}' has an unknown root");
            Require(errors, component.Values.Count > 0, $"registry component '{component.Id}' has no value");
            Require(errors, !string.IsNullOrWhiteSpace(component.Key), $"registry component '{component.Id}' has no key");
            foreach (RegistryValueDefinition value in Entries(errors, component.Values, $"registry component '{component.Id}' values"))
            {
                Require(errors, Enum.IsDefined(value.Type), $"registry component '{component.Id}' has a value of unknown type");
                Require(errors, value.Type != RegistryValueType.Integer || int.TryParse(value.Value, out _),
                    $"registry component '{component.Id}': '{value.Value}' is not an integer");
            }
        }
    }

    private static void ValidateSetup(List<string> errors, SetupDefinition setup, IReadOnlyDictionary<string, string> properties)
    {
        Require(errors, !string.IsNullOrWhiteSpace(setup.SettingsKey), "setup.settingsKey is empty");
        Require(errors, setup.LanguageProperty is null || properties.ContainsKey(setup.LanguageProperty),
            $"setup.languageProperty '{setup.LanguageProperty}' is not declared in properties");
        foreach (SetupOptionDefinition option in Entries(errors, setup.Options, "setup.options"))
        {
            Require(errors, properties.ContainsKey(option.Property), $"setup option '{option.Property}' is not declared in properties");
            Require(errors, !string.IsNullOrWhiteSpace(option.LabelFr) && !string.IsNullOrWhiteSpace(option.LabelEn),
                $"setup option '{option.Property}' needs a French and an English label");
        }
    }

    /// <summary>
    /// The recorded choices belong to the installation: HKCU for a per-user one, HKLM for a per-machine one, where the
    /// setup reads them back. A fixed hive would put them where the other kind of installation never looks.
    /// </summary>
    private static void ValidateSettingsRoot(List<string> errors, SetupDefinition setup, IReadOnlyList<RegistryComponentDefinition> components)
    {
        foreach (RegistryComponentDefinition component in components.Where(component => component is not null
            && component.Root != RegistryRoot.Installation
            && string.Equals(component.Key?.Trim('\\'), setup.SettingsKey?.Trim('\\'), StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"registry component '{component.Id}' records setup.settingsKey under {component.Root}; use Installation");
        }
    }

    private static void ValidateId(List<string> errors, HashSet<string> ids, string id)
    {
        Require(errors, IdentifierPattern().IsMatch(id) && id.Length <= 60, $"'{id}' is not a valid identifier (letters, digits, '_' and '.', 60 characters at most)");
        Require(errors, ids.Add(id), $"identifier '{id}' is used twice");
    }

    /// <summary>The entries to validate further; a JSON <c>null</c> in the array is reported instead of dereferenced.</summary>
    private static IEnumerable<T> Entries<T>(List<string> errors, IReadOnlyList<T> items, string name)
        where T : class
    {
        Require(errors, items.All(item => item is not null), $"{name} holds a null entry");
        return items.Where(item => item is not null);
    }

    private static bool IsRelativeFile(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split('\\', '/').Contains("..");

    private const int MaxFileNameLength = 100;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A name usable as a Windows file or folder name: no invalid character, no trailing dot or space, no reserved device name.</summary>
    private static bool IsFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxFileNameLength || name is "." or "..")
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name[^1] is '.' or ' ' || name[0] == ' ')
        {
            return false;
        }

        // A device name is reserved with any extension too: "CON.txt" opens the console.
        int dot = name.IndexOf('.');
        return !ReservedDeviceNames.Contains((dot < 0 ? name : name[..dot]).TrimEnd(' '));
    }

    /// <summary>
    /// The MSI database is written with code page 1252, so any text stored in it must be representable there;
    /// anything else would be stored as U+007F (DEL), silently.
    /// </summary>
    private static void ValidateCodePage(List<string> errors, ProductDefinition definition)
    {
        Encoding windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

        void Check(string field, string? text)
        {
            if (text is null)
            {
                return;
            }

            try
            {
                windows1252.GetBytes(text);
            }
            catch (EncoderFallbackException)
            {
                errors.Add($"{field} holds characters outside the Windows-1252 code page of the MSI");
            }
        }

        Check("name", definition.Name);
        Check("manufacturer", definition.Manufacturer);
        Check("downgradeErrorMessage", definition.DowngradeErrorMessage);
        Check("mainExecutable", definition.MainExecutable);
        foreach (string folder in definition.InstallDirectory)
        {
            Check("installDirectory", folder);
        }

        foreach ((string property, string value) in definition.Properties)
        {
            Check($"property '{property}'", property);
            Check($"property '{property}'", value);
        }

        foreach (ShortcutDefinition shortcut in definition.Shortcuts.Where(shortcut => shortcut is not null))
        {
            string field = $"shortcut '{shortcut.Id}'";
            Check(field, shortcut.Name);
            Check(field, shortcut.Folder);
            Check(field, shortcut.Description);
            Check(field, shortcut.Condition);
            Check(field, shortcut.RegistryKey);
            Check(field, shortcut.RegistryName);
        }

        foreach (RegistryComponentDefinition component in definition.RegistryComponents.Where(component => component is not null))
        {
            string field = $"registry component '{component.Id}'";
            Check(field, component.Key);
            Check(field, component.Condition);
            foreach (RegistryValueDefinition value in component.Values.Where(value => value is not null))
            {
                Check(field, value.Name);
                Check(field, value.Value);
            }
        }
    }

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
