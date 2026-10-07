namespace OmniEurope.Installer.Setup;

/// <summary>What to assemble into a setup executable.</summary>
/// <param name="HostExecutable">The built setup host (OmniEurope.Installer.Setup.exe).</param>
/// <param name="MsiPath">The MSI to carry, built from the same product definition.</param>
/// <param name="LicensePath">License text (RTF) shown by the wizard; null for none.</param>
/// <param name="IconSource">Executable whose icon brands the setup program, usually the product's main executable.</param>
/// <param name="OutputPath">The setup executable to write; an existing file is replaced.</param>
public sealed record SetupBuildRequest(string HostExecutable, string MsiPath, string? LicensePath, string IconSource, string OutputPath);
