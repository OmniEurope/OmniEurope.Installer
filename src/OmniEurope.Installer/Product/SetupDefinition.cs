namespace OmniEurope.Installer.Product;

/// <summary>The wizard of the setup executable: which choices it offers and what it cleans up.</summary>
public sealed record SetupDefinition
{
    /// <summary>HKCU key where the MSI records the choices (see the registry components); read to prefill the wizard.</summary>
    public required string SettingsKey { get; init; }

    /// <summary>Public property receiving "fr" or "en"; null when the product has no language choice.</summary>
    public string? LanguageProperty { get; init; }

    /// <summary>Check boxes of the folder page; each maps to a public property set to "1" or left empty.</summary>
    public IReadOnlyList<SetupOptionDefinition> Options { get; init; } = [];

    /// <summary>BundleUpgradeCodes of former setup bundles whose orphan "Installed apps" entry is removed after installing.</summary>
    public IReadOnlyList<Guid> LegacyBundleUpgradeCodes { get; init; } = [];

    /// <summary>French note shown above the license text, e.g. that the English text is the only binding one.</summary>
    public string? LicenseNoticeFr { get; init; }
}
