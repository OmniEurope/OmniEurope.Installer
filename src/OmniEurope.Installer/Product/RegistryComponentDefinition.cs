namespace OmniEurope.Installer.Product;

/// <summary>Registry values installed together under one key; the first value is the component's key path.</summary>
public sealed record RegistryComponentDefinition
{
    /// <summary>Identifier, also the component name.</summary>
    public required string Id { get; init; }

    /// <summary>Windows Installer condition; the values are written only when it is true.</summary>
    public string? Condition { get; init; }

    /// <summary>Hive.</summary>
    public required RegistryRoot Root { get; init; }

    /// <summary>Key under the hive.</summary>
    public required string Key { get; init; }

    /// <summary>Values written under <see cref="Key"/>; at least one.</summary>
    public required IReadOnlyList<RegistryValueDefinition> Values { get; init; }
}
