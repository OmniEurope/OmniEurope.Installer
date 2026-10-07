namespace OmniEurope.Installer.Building;

/// <summary>A published file, its path relative to the published folder using '\' separators.</summary>
internal sealed record SourceFile(string RelativePath, string FullPath, long Length)
{
    public string RelativeDirectory => Path.GetDirectoryName(RelativePath) ?? string.Empty;

    public string Name => Path.GetFileName(RelativePath);
}
