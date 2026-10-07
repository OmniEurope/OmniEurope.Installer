namespace OmniEurope.Installer.Msi;

/// <summary>A file whose bytes are stored in a binary (OBJECT) column.</summary>
/// <param name="FilePath">The file read by Windows Installer when the row is inserted.</param>
internal sealed record MsiStream(string FilePath);
