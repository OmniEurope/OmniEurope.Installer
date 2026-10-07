namespace OmniEurope.Installer.Building;

/// <summary>The MSI that was written.</summary>
/// <param name="OutputPath">Full path of the MSI.</param>
/// <param name="ProductCode">Product code of this build (new for every build).</param>
/// <param name="FileCount">Number of installed files.</param>
public sealed record MsiBuildResult(string OutputPath, Guid ProductCode, int FileCount);
