using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OmniEurope.Installer.Building;

/// <summary>
/// Deterministic names: the same input gives the same identifier, component GUID and short file name
/// from one build to the next, which Windows Installer's component rules require across versions.
/// </summary>
internal static partial class MsiNames
{
    /// <summary>An MSI identifier: <paramref name="prefix"/> followed by 32 hex digits of a hash of <paramref name="key"/>.</summary>
    public static string Identifier(char prefix, string key) => prefix + Convert.ToHexString(Hash(key))[..32];

    /// <summary>A GUID derived from <paramref name="scope"/> and <paramref name="key"/> (case-insensitive), formatted for MSI.</summary>
    public static string StableGuid(Guid scope, string key)
    {
        byte[] hash = Hash(scope.ToString("D") + "|" + key);
        byte[] bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5 layout (name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 4122 variant
        return Format(new Guid(bytes, bigEndian: true));
    }

    /// <summary>A GUID in the upper-case braced form Windows Installer expects.</summary>
    public static string Format(Guid guid) => guid.ToString("B").ToUpperInvariant();

    /// <summary>
    /// The Filename column value for <paramref name="longName"/>: the name alone when it is already a
    /// valid 8.3 name, otherwise <c>short|long</c> with a short name unique among <paramref name="takenShortNames"/>.
    /// </summary>
    public static string FileName(string longName, ISet<string> takenShortNames)
    {
        if (ShortNamePattern().IsMatch(longName) && takenShortNames.Add(longName.ToUpperInvariant()))
        {
            return longName;
        }

        string extension = SanitizeShort(Path.GetExtension(longName).TrimStart('.'));
        extension = extension.Length > 3 ? extension[..3] : extension;
        for (int attempt = 0; ; attempt++)
        {
            string stem = Convert.ToHexString(Hash($"{longName}|{attempt}"))[..8].ToLowerInvariant();
            string shortName = extension.Length > 0 ? $"{stem}.{extension}" : stem;
            if (takenShortNames.Add(shortName.ToUpperInvariant()))
            {
                return $"{shortName}|{longName}";
            }
        }
    }

    private static string SanitizeShort(string value) => new(value.Where(character => char.IsAsciiLetterOrDigit(character) || character == '_').ToArray());

    private static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));

    [GeneratedRegex(@"^[A-Za-z0-9_\-!#$%&'()@^`{}~]{1,8}(\.[A-Za-z0-9_\-!#$%&'()@^`{}~]{1,3})?$")]
    private static partial Regex ShortNamePattern();
}
