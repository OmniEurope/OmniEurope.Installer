using System.Text;

namespace OmniEurope.Installer.Setup;

/// <summary>The installed version of a product, found through its UpgradeCode.</summary>
internal sealed class InstalledProduct
{
    private InstalledProduct(string productCode, string version)
    {
        ProductCode = productCode;
        Version = version;
    }

    public string ProductCode { get; }

    public string Version { get; }

    /// <summary>The first installed product sharing <paramref name="upgradeCode"/>, or null.</summary>
    public static InstalledProduct? Find(string upgradeCode)
    {
        var productCode = new StringBuilder(39);
        if (MsiApi.MsiEnumRelatedProducts(upgradeCode, 0, 0, productCode) != MsiApi.Success)
        {
            return null;
        }

        string code = productCode.ToString();
        return new InstalledProduct(code, ProductInfo(code, "VersionString") ?? string.Empty);
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

    private static string? ProductInfo(string productCode, string property)
    {
        uint length = 0;
        if (MsiApi.MsiGetProductInfo(productCode, property, null, ref length) != MsiApi.Success)
        {
            return null;
        }

        var value = new StringBuilder((int)length + 1);
        length++;
        return MsiApi.MsiGetProductInfo(productCode, property, value, ref length) == MsiApi.Success ? value.ToString() : null;
    }
}
