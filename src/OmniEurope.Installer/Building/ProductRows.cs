using OmniEurope.Installer.Msi;
using OmniEurope.Installer.Product;

namespace OmniEurope.Installer.Building;

/// <summary>
/// Rows that come from the product definition rather than from the published files: properties,
/// major upgrade, shortcuts and registry values.
/// </summary>
internal sealed class ProductRows
{
    public const string FeatureName = "Main";
    public const string UpgradeDetected = "OE_UPGRADE_DETECTED";
    public const string DowngradeDetected = "OE_DOWNGRADE_DETECTED";

    // Component attributes: 64-bit component, and key path in the Registry table.
    private const int Component64Bit = 256;
    private const int ComponentRegistryKeyPath = 4;
    private const int RemoveOnUninstall = 2;
    private const int UpgradeMigrateFeatures = 1;
    private const int UpgradeOnlyDetect = 2;
    private const int UpgradeVersionMaxInclusive = 0x200;

    private readonly ProductDefinition _definition;
    private readonly string _iconName;

    public ProductRows(ProductDefinition definition)
    {
        _definition = definition;
        _iconName = "ProductIcon" + Path.GetExtension(definition.MainExecutable);
    }

    public List<string> ComponentIds { get; } = [];

    public void WriteProperties(MsiDatabase database, string productCode, string version)
    {
        var secure = new List<string> { UpgradeDetected, DowngradeDetected, DirectoryLayout.InstallFolder };
        secure.AddRange(_definition.Properties.Keys);
        var properties = new List<(string Name, string Value)>
        {
            ("ProductCode", productCode),
            ("ProductName", _definition.Name),
            ("ProductVersion", version),
            ("ProductLanguage", "1033"),
            ("Manufacturer", _definition.Manufacturer),
            ("UpgradeCode", MsiNames.Format(_definition.UpgradeCode)),
            ("ALLUSERS", "1"),
            ("ARPPRODUCTICON", _iconName),
            ("SecureCustomProperties", string.Join(';', secure)),
            // Costing runs before RemoveExistingProducts: with the default "omus", a file already on disk with a
            // higher version (an upgrade that ships an older dependency) is skipped, then deleted with the old
            // product, leaving the install incomplete. "a" copies every file; they are all private to INSTALLFOLDER.
            ("REINSTALLMODE", "amus"),
            // Windows keeps a copy of the MSI without its embedded cabinet: a repair or change started from
            // "Installed apps" would ask for the original file. Those run through the setup program instead.
            ("ARPNOREPAIR", "1"),
            ("ARPNOMODIFY", "1"),
        };
        properties.AddRange(_definition.Properties.Select(pair => (pair.Key, pair.Value)));

        using MsiStatement insert = database.Prepare(MsiSchema.InsertStatement("Property", "Property", "Value"));
        foreach ((string name, string value) in properties)
        {
            insert.Execute(name, value);
        }
    }

    /// <summary>Major upgrade: replace any older or equal version, refuse to install over a newer one.</summary>
    public void WriteUpgrade(MsiDatabase database, string version)
    {
        string upgradeCode = MsiNames.Format(_definition.UpgradeCode);
        using (MsiStatement insert = database.Prepare(MsiSchema.InsertStatement("Upgrade", "UpgradeCode", "VersionMin", "VersionMax", "Language", "Attributes", "Remove", "ActionProperty")))
        {
            insert.Execute(upgradeCode, null, version, null, UpgradeMigrateFeatures | UpgradeVersionMaxInclusive, null, UpgradeDetected);
            insert.Execute(upgradeCode, version, null, null, UpgradeOnlyDetect, null, DowngradeDetected);
        }

        database.Execute(MsiSchema.InsertStatement("LaunchCondition", "Condition", "Description"),
            $"Installed OR NOT {DowngradeDetected}", _definition.DowngradeErrorMessage);
    }

    public void WriteIcon(MsiDatabase database, string mainExecutablePath)
    {
        database.Execute(MsiSchema.InsertStatement("Icon", "Name", "Data"), _iconName, new MsiStream(mainExecutablePath));
    }

    public void WriteShortcuts(MsiDatabase database, DirectoryLayout directories)
    {
        string target = $"[{DirectoryLayout.InstallFolder}]{_definition.MainExecutable.Replace('/', '\\')}";
        using MsiStatement shortcuts = database.Prepare(MsiSchema.InsertStatement("Shortcut",
            "Shortcut", "Directory_", "Name", "Component_", "Target", "Description", "Icon_", "IconIndex", "WkDir"));
        using MsiStatement removals = database.Prepare(MsiSchema.InsertStatement("RemoveFile", "FileKey", "Component_", "FileName", "DirProperty", "InstallMode"));
        foreach (ShortcutDefinition shortcut in _definition.Shortcuts)
        {
            string directory = directories.ForShortcut(shortcut);
            string keyPath = shortcut.Id + "_KeyPath";
            WriteComponent(database, shortcut.Id, directory, shortcut.Condition, keyPath);
            WriteRegistryValue(database, keyPath, 1, shortcut.RegistryKey, shortcut.RegistryName, "#1", shortcut.Id);
            // Windows Installer appends ".lnk" itself.
            string name = MsiNames.FileName(shortcut.Name, directories.ShortNames(directory));
            shortcuts.Execute(shortcut.Id + "_Shortcut", directory, name, shortcut.Id, target, shortcut.Description, _iconName, 0, DirectoryLayout.InstallFolder);
            if (shortcut.Folder is not null)
            {
                removals.Execute(shortcut.Id + "_Folder", shortcut.Id, null, directory, RemoveOnUninstall);
            }
        }
    }

    public void WriteRegistry(MsiDatabase database)
    {
        foreach (RegistryComponentDefinition component in _definition.RegistryComponents)
        {
            int root = component.Root == RegistryRoot.CurrentUser ? 1 : 2;
            for (int index = 0; index < component.Values.Count; index++)
            {
                RegistryValueDefinition value = component.Values[index];
                WriteRegistryValue(database, $"{component.Id}_{index}", root, component.Key, value.Name, Encode(value), component.Id);
            }

            WriteComponent(database, component.Id, DirectoryLayout.InstallFolder, component.Condition, $"{component.Id}_0");
        }
    }

    public void WriteFeature(MsiDatabase database)
    {
        database.Execute(MsiSchema.InsertStatement("Feature", "Feature", "Title", "Display", "Level", "Directory_", "Attributes"),
            FeatureName, _definition.Name, 1, 1, DirectoryLayout.InstallFolder, 0);
        using MsiStatement insert = database.Prepare(MsiSchema.InsertStatement("FeatureComponents", "Feature_", "Component_"));
        foreach (string component in ComponentIds)
        {
            insert.Execute(FeatureName, component);
        }
    }

    /// <summary>A component holding one installed file, keyed by that file.</summary>
    public void WriteFileComponent(MsiStatement insert, string componentId, string relativePath, string directory, string fileId)
    {
        insert.Execute(componentId, MsiNames.StableGuid(_definition.UpgradeCode, "file:" + relativePath), directory, Component64Bit, null, fileId);
        ComponentIds.Add(componentId);
    }

    private void WriteComponent(MsiDatabase database, string id, string directory, string? condition, string registryKeyPath)
    {
        database.Execute(MsiSchema.InsertStatement("Component", "Component", "ComponentId", "Directory_", "Attributes", "Condition", "KeyPath"),
            id, MsiNames.StableGuid(_definition.UpgradeCode, "component:" + id), directory, Component64Bit | ComponentRegistryKeyPath, condition, registryKeyPath);
        ComponentIds.Add(id);
    }

    private static void WriteRegistryValue(MsiDatabase database, string id, int root, string key, string? name, string value, string component)
    {
        database.Execute(MsiSchema.InsertStatement("Registry", "Registry", "Root", "Key", "Name", "Value", "Component_"),
            id, root, key, name, value, component);
    }

    /// <summary>Registry table encoding: "#n" is a DWORD, so a string starting with '#' is escaped as "##".</summary>
    private static string Encode(RegistryValueDefinition value) => value.Type switch
    {
        RegistryValueType.Integer => "#" + value.Value,
        _ => value.Value.StartsWith('#') ? "#" + value.Value : value.Value,
    };
}
