namespace OmniEurope.Installer.Setup;

/// <summary>Finds the setup host program: the one named by the caller, or the one bundled with the tool.</summary>
public static class SetupHostLocator
{
    /// <summary>File name of the setup host shipped next to the tool assembly.</summary>
    public const string BundledFileName = "OmniEurope.Installer.Setup.exe";

    /// <summary>
    /// Returns <paramref name="explicitHost"/> when given (its existence is checked by the builder), otherwise the host
    /// bundled in <paramref name="toolDirectory"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No host was given and none is bundled.</exception>
    public static string Resolve(string? explicitHost, string toolDirectory)
    {
        if (!string.IsNullOrEmpty(explicitHost))
        {
            return explicitHost;
        }

        string bundled = Path.Combine(toolDirectory, BundledFileName);
        return File.Exists(bundled)
            ? bundled
            : throw new InvalidOperationException(
                $"No setup host: --host was not given and the bundled {BundledFileName} was not found in {toolDirectory}. "
                + "Pass --host <OmniEurope.Installer.Setup.exe>.");
    }
}
