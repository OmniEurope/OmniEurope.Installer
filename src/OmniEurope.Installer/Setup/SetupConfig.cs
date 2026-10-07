using System.Runtime.Serialization;

namespace OmniEurope.Installer.Setup;

// Shared source: compiled into the library (net10.0) and linked into the setup host (net472), so both
// sides read and write exactly the same JSON through DataContractJsonSerializer.

/// <summary>What the setup executable needs to know about the product and the MSI it carries.</summary>
[DataContract]
public sealed class SetupConfig
{
    /// <summary>Product name shown by the wizard.</summary>
    [DataMember(Order = 1)]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Publisher.</summary>
    [DataMember(Order = 2)]
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>Version of the carried MSI (major.minor.build).</summary>
    [DataMember(Order = 3)]
    public string ProductVersion { get; set; } = string.Empty;

    /// <summary>ProductCode of the carried MSI, braced.</summary>
    [DataMember(Order = 4)]
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>UpgradeCode shared by every version, braced.</summary>
    [DataMember(Order = 5)]
    public string UpgradeCode { get; set; } = string.Empty;

    /// <summary>Executable relative to the install folder, launched at the end and closed before installing.</summary>
    [DataMember(Order = 6)]
    public string MainExecutable { get; set; } = string.Empty;

    /// <summary>Default folder chain under 64-bit Program Files.</summary>
    [DataMember(Order = 7)]
    public string[] InstallDirectory { get; set; } = [];

    /// <summary>HKCU key where the MSI records the choices, read back to prefill the wizard.</summary>
    [DataMember(Order = 8)]
    public string SettingsKey { get; set; } = string.Empty;

    /// <summary>Public property receiving "fr" or "en"; empty when the product has no language choice.</summary>
    [DataMember(Order = 9)]
    public string LanguageProperty { get; set; } = string.Empty;

    /// <summary>Check boxes of the folder page.</summary>
    [DataMember(Order = 10)]
    public SetupOption[] Options { get; set; } = [];

    /// <summary>BundleUpgradeCodes of former setup bundles whose orphan entry is removed after installing.</summary>
    [DataMember(Order = 11)]
    public string[] LegacyBundleUpgradeCodes { get; set; } = [];

    /// <summary>French note shown above the license text; empty for none.</summary>
    [DataMember(Order = 12)]
    public string LicenseNoticeFr { get; set; } = string.Empty;

    /// <summary>ComponentId of the main executable: Windows Installer resolves its installed path from it.</summary>
    [DataMember(Order = 13)]
    public string MainComponentId { get; set; } = string.Empty;
}
