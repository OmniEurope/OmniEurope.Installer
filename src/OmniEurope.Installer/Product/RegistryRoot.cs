namespace OmniEurope.Installer.Product;

/// <summary>Registry hive.</summary>
public enum RegistryRoot
{
    /// <summary>HKEY_CURRENT_USER.</summary>
    CurrentUser,

    /// <summary>HKEY_LOCAL_MACHINE.</summary>
    LocalMachine,

    /// <summary>The installation's own hive: HKEY_CURRENT_USER for a per-user installation, HKEY_LOCAL_MACHINE for a per-machine one.</summary>
    Installation,
}
