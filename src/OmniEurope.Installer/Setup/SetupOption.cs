using System.Runtime.Serialization;

namespace OmniEurope.Installer.Setup;

/// <summary>A check box mapped to a public property: "1" when checked, empty when not.</summary>
[DataContract]
public sealed class SetupOption
{
    /// <summary>Public property set by the box.</summary>
    [DataMember(Order = 1)]
    public string Property { get; set; } = string.Empty;

    /// <summary>Value name under <see cref="SetupConfig.SettingsKey"/> that remembers the choice.</summary>
    [DataMember(Order = 2)]
    public string SettingName { get; set; } = string.Empty;

    /// <summary>French label.</summary>
    [DataMember(Order = 3)]
    public string LabelFr { get; set; } = string.Empty;

    /// <summary>English label.</summary>
    [DataMember(Order = 4)]
    public string LabelEn { get; set; } = string.Empty;

    /// <summary>State on a first installation.</summary>
    [DataMember(Order = 5)]
    public bool DefaultChecked { get; set; }
}
