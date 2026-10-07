namespace OmniEurope.Installer.Building;

/// <summary>What to build: the published folder to install, the product version and the MSI to write.</summary>
/// <param name="SourceDirectory">The published application folder.</param>
/// <param name="Version">Product version, <c>major.minor.build</c> (major and minor up to 255, build up to 65535).</param>
/// <param name="OutputPath">The MSI file to create; an existing file is replaced.</param>
public sealed record MsiBuildRequest(string SourceDirectory, string Version, string OutputPath);
