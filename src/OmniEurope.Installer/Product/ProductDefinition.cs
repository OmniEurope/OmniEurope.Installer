using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniEurope.Installer.Product;

/// <summary>
/// What an MSI installs: the product identity, where its published files go, and the shortcuts and
/// registry values that come with them. Loaded from a JSON file with <see cref="Load"/>.
/// </summary>
public sealed record ProductDefinition
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Product name shown in "Installed apps".</summary>
    public required string Name { get; init; }

    /// <summary>Publisher shown in "Installed apps".</summary>
    public required string Manufacturer { get; init; }

    /// <summary>Identity shared by every version of the product; a new version replaces any installed one with the same code.</summary>
    public required Guid UpgradeCode { get; init; }

    /// <summary>Executable relative to the published folder: shortcut target and icon source.</summary>
    public required string MainExecutable { get; init; }

    /// <summary>Folder chain under 64-bit Program Files, e.g. <c>["OmniEurope", "Sample App"]</c>; the last one is <c>INSTALLFOLDER</c>.</summary>
    public required IReadOnlyList<string> InstallDirectory { get; init; }

    /// <summary>Message shown when a newer version is already installed.</summary>
    public required string DowngradeErrorMessage { get; init; }

    /// <summary>Public properties with their default values; an installer passes other values on the command line.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();

    /// <summary>File name patterns (<c>*</c> and <c>?</c>) left out of the published folder.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>Shortcuts to <see cref="MainExecutable"/>.</summary>
    public IReadOnlyList<ShortcutDefinition> Shortcuts { get; init; } = [];

    /// <summary>Registry values, grouped in components that install together.</summary>
    public IReadOnlyList<RegistryComponentDefinition> RegistryComponents { get; init; } = [];

    /// <summary>The installation wizard of the setup executable; null when the product ships the MSI alone.</summary>
    public SetupDefinition? Setup { get; init; }

    /// <summary>Reads and validates a product definition file.</summary>
    /// <exception cref="InvalidDataException">The file is not a valid definition.</exception>
    public static ProductDefinition Load(string path)
    {
        ProductDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<ProductDefinition>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path}: {exception.Message}", exception);
        }

        if (definition is null)
        {
            throw new InvalidDataException($"{path}: the file holds no product definition.");
        }

        ProductDefinitionValidator.Validate(definition, path);
        return definition;
    }
}
