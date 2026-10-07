using System.IO.Enumeration;

namespace OmniEurope.Installer.Building;

/// <summary>The files of a published folder, in a stable order (ordinal, case-insensitive).</summary>
internal static class SourceTree
{
    public static IReadOnlyList<SourceFile> Collect(string root, IReadOnlyList<string> excludedPatterns)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Published folder not found: {root}");
        }

        string fullRoot = Path.GetFullPath(root);
        var files = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(Path.GetFileName(path), excludedPatterns))
            .Select(path => new FileInfo(path))
            .Select(info => new SourceFile(Path.GetRelativePath(fullRoot, info.FullName), info.FullName, info.Length))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No file to install in {root}.");
        }

        return files;
    }

    private static bool IsExcluded(string fileName, IReadOnlyList<string> patterns) =>
        patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true));
}
