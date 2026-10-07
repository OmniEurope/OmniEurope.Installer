namespace OmniEurope.Installer.Cabinet;

/// <summary>A file stored in a cabinet under <paramref name="Name"/>.</summary>
/// <param name="Name">The name inside the cabinet (for an MSI, the File table key).</param>
/// <param name="SourcePath">The file on disk.</param>
internal sealed record CabinetEntry(string Name, string SourcePath);
