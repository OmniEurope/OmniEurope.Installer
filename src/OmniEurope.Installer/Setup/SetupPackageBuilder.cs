using OmniEurope.Installer.Building;
using OmniEurope.Installer.Msi;
using OmniEurope.Installer.Product;

namespace OmniEurope.Installer.Setup;

/// <summary>Assembles a setup executable: the host program, the product's icon, then the MSI and its configuration.</summary>
public static class SetupPackageBuilder
{
    /// <summary>Writes the setup executable described by <paramref name="request"/>.</summary>
    /// <exception cref="InvalidDataException">The definition has no setup section.</exception>
    public static void Build(ProductDefinition definition, SetupBuildRequest request)
    {
        SetupDefinition setup = definition.Setup
            ?? throw new InvalidDataException($"The definition of {definition.Name} has no 'setup' section.");
        SetupConfig config = CreateConfig(definition, setup, request.MsiPath);

        string outputPath = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        string workPath = outputPath + ".tmp";
        try
        {
            File.Copy(request.HostExecutable, workPath, overwrite: true);
            // Icon first: rewriting the resources drops any data appended after the PE image.
            IconResources.Copy(request.IconSource, workPath);
            using (var output = new FileStream(workPath, FileMode.Append, FileAccess.Write, FileShare.None))
            {
                SetupPayload.Append(output, config, request.MsiPath, request.LicensePath);
            }

            File.Move(workPath, outputPath, overwrite: true);
        }
        finally
        {
            File.Delete(workPath);
        }
    }

    internal static SetupConfig CreateConfig(ProductDefinition definition, SetupDefinition setup, string msiPath)
    {
        using MsiDatabase msi = MsiDatabase.OpenReadOnly(msiPath);
        string upgradeCode = MsiNames.Format(definition.UpgradeCode);
        string? msiUpgradeCode = ReadProperty(msi, "UpgradeCode");
        if (msiUpgradeCode != upgradeCode)
        {
            throw new InvalidDataException($"{msiPath} was not built from the {definition.Name} definition (UpgradeCode {msiUpgradeCode}).");
        }

        return new SetupConfig
        {
            ProductName = definition.Name,
            Manufacturer = definition.Manufacturer,
            ProductVersion = ReadProperty(msi, "ProductVersion") ?? string.Empty,
            ProductCode = ReadProperty(msi, "ProductCode") ?? string.Empty,
            UpgradeCode = upgradeCode,
            MainExecutable = definition.MainExecutable.Replace('/', '\\'),
            InstallDirectory = [.. definition.InstallDirectory],
            SettingsKey = setup.SettingsKey,
            LanguageProperty = setup.LanguageProperty ?? string.Empty,
            Options = [.. setup.Options.Select(option => new SetupOption
            {
                Property = option.Property,
                SettingName = option.SettingName,
                LabelFr = option.LabelFr,
                LabelEn = option.LabelEn,
                DefaultChecked = definition.Properties.TryGetValue(option.Property, out string? value) && value == "1",
            })],
            LegacyBundleUpgradeCodes = [.. setup.LegacyBundleUpgradeCodes.Select(MsiNames.Format)],
            LicenseNoticeFr = setup.LicenseNoticeFr ?? string.Empty,
            MainComponentId = MainComponentId(msi, definition.MainExecutable),
        };
    }

    private static string MainComponentId(MsiDatabase msi, string mainExecutable)
    {
        string fileId = MsiNames.Identifier('f', mainExecutable.Replace('/', '\\'));
        string? component = msi.Query("SELECT `Component_` FROM `File` WHERE `File` = ?", fileId).SingleOrDefault()?[0];
        return component is null
            ? string.Empty
            : msi.Query("SELECT `ComponentId` FROM `Component` WHERE `Component` = ?", component).Single()[0] ?? string.Empty;
    }

    private static string? ReadProperty(MsiDatabase msi, string name) =>
        msi.Query("SELECT `Value` FROM `Property` WHERE `Property` = ?", name).SingleOrDefault()?[0];
}
