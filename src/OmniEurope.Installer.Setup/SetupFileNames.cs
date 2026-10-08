using System.IO;
using System.Linq;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// File names derived from the product name, which is a display name: "Acme/App" is valid there, but as a
/// file name it would point into a folder that does not exist.
/// </summary>
internal static class SetupFileNames
{
    /// <summary><paramref name="name"/> with every character a file name cannot hold replaced by '_'.</summary>
    public static string Safe(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
}
