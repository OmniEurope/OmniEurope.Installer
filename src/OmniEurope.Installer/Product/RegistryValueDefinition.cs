namespace OmniEurope.Installer.Product;

/// <summary>One registry value.</summary>
public sealed record RegistryValueDefinition
{
    /// <summary>Value name; null for the key's default value.</summary>
    public string? Name { get; init; }

    /// <summary>Value kind.</summary>
    public required RegistryValueType Type { get; init; }

    /// <summary>Value data; may reference properties such as <c>[INSTALLFOLDER]</c>.</summary>
    public required string Value { get; init; }
}
