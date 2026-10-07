namespace OmniEurope.Installer.Product;

/// <summary>A shortcut to the main executable. Its presence is tracked by a per-user registry value.</summary>
public sealed record ShortcutDefinition
{
    /// <summary>Identifier, also the component name; letters, digits, underscores and periods.</summary>
    public required string Id { get; init; }

    /// <summary>Where the shortcut goes.</summary>
    public required ShortcutLocation Location { get; init; }

    /// <summary>Start menu sub-folder, removed on uninstall; only for <see cref="ShortcutLocation.ProgramMenu"/>.</summary>
    public string? Folder { get; init; }

    /// <summary>Shortcut file name without extension.</summary>
    public required string Name { get; init; }

    /// <summary>Tooltip text.</summary>
    public string? Description { get; init; }

    /// <summary>Windows Installer condition; the shortcut is installed only when it is true.</summary>
    public string? Condition { get; init; }

    /// <summary>HKCU key of the value that records the shortcut (its key path).</summary>
    public required string RegistryKey { get; init; }

    /// <summary>Name of the value that records the shortcut.</summary>
    public required string RegistryName { get; init; }
}
