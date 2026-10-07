namespace OmniEurope.Installer.Product;

/// <summary>Kind of registry value.</summary>
public enum RegistryValueType
{
    /// <summary>REG_SZ; the value is a Windows Installer formatted string.</summary>
    String,

    /// <summary>REG_DWORD.</summary>
    Integer,
}
