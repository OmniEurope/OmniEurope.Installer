namespace OmniEurope.Installer.Product;

/// <summary>A check box of the folder page.</summary>
public sealed record SetupOptionDefinition
{
    /// <summary>Public property, declared in <see cref="ProductDefinition.Properties"/>; its default "1" means checked.</summary>
    public required string Property { get; init; }

    /// <summary>Value name under <see cref="SetupDefinition.SettingsKey"/> that remembers the choice.</summary>
    public required string SettingName { get; init; }

    /// <summary>French label.</summary>
    public required string LabelFr { get; init; }

    /// <summary>English label.</summary>
    public required string LabelEn { get; init; }
}
