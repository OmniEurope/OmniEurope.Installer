using System.Text;

namespace OmniEurope.Installer.Setup;

/// <summary>The installed version of a product, found through its UpgradeCode, and the context it is installed in.</summary>
internal sealed class InstalledProduct
{
    // Per machine first: it is visible to every user, a per-user installation only to the current one.
    private static readonly (int Context, InstallScope Scope)[] Contexts =
    {
        (MsiApi.ContextMachine, InstallScope.PerMachine),
        (MsiApi.ContextUserUnmanaged, InstallScope.PerUser),
        (MsiApi.ContextUserManaged, InstallScope.PerUser),
    };

    private InstalledProduct(string productCode, string version, InstallScope scope)
    {
        ProductCode = productCode;
        Version = version;
        Scope = scope;
    }

    public string ProductCode { get; }

    public string Version { get; }

    /// <summary>Per user (for the current user) or per machine; an update, repair or uninstall stays in it.</summary>
    public InstallScope Scope { get; }

    /// <summary>The first product sharing <paramref name="upgradeCode"/> installed per machine or for the current user, or null.</summary>
    public static InstalledProduct? Find(string upgradeCode)
    {
        var productCode = new StringBuilder(39);
        for (uint index = 0; MsiApi.MsiEnumRelatedProducts(upgradeCode, 0, index, productCode) == MsiApi.Success; index++)
        {
            string code = productCode.ToString();
            foreach ((int context, InstallScope scope) in Contexts)
            {
                string? version = ProductInfo(code, context, "VersionString");
                if (version is not null)
                {
                    return new InstalledProduct(code, version, scope);
                }
            }
        }

        return null;
    }

    /// <summary>The installed path of a component of a product, or null.</summary>
    public static string? ComponentPath(string productCode, string componentId)
    {
        uint length = 0;
        MsiApi.MsiGetComponentPath(productCode, componentId, null, ref length);
        if (length == 0)
        {
            return null;
        }

        var path = new StringBuilder((int)length + 1);
        length++;
        int state = MsiApi.MsiGetComponentPath(productCode, componentId, path, ref length);
        const int InstallStateLocal = 3;
        return state == InstallStateLocal ? path.ToString() : null;
    }

    // A null user SID is the current user for the per-user contexts, and is required for the machine context.
    private static string? ProductInfo(string productCode, int context, string property)
    {
        uint length = 0;
        if (MsiApi.MsiGetProductInfoEx(productCode, null, context, property, null, ref length) != MsiApi.Success)
        {
            return null;
        }

        var value = new StringBuilder((int)length + 1);
        length++;
        return MsiApi.MsiGetProductInfoEx(productCode, null, context, property, value, ref length) == MsiApi.Success ? value.ToString() : null;
    }
}
