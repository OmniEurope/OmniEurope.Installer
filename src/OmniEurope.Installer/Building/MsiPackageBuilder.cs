using System.Diagnostics;
using System.Text.RegularExpressions;
using OmniEurope.Installer.Cabinet;
using OmniEurope.Installer.Msi;
using OmniEurope.Installer.Product;

namespace OmniEurope.Installer.Building;

/// <summary>
/// Builds a per-machine, 64-bit MSI from a product definition and a published folder, with
/// msi.dll for the database and an embedded MSZIP cabinet for the files.
/// </summary>
public static partial class MsiPackageBuilder
{
    private const int FileVital = 512;
    private const string CabinetName = "product.cab";

    // Summary information property ids.
    private const uint PidCodepage = 1;
    private const uint PidTitle = 2;
    private const uint PidSubject = 3;
    private const uint PidAuthor = 4;
    private const uint PidKeywords = 5;
    private const uint PidComments = 6;
    private const uint PidTemplate = 7;
    private const uint PidRevisionNumber = 9;
    private const uint PidCreated = 12;
    private const uint PidLastSaved = 13;
    private const uint PidPageCount = 14;
    private const uint PidWordCount = 15;
    private const uint PidApplicationName = 18;
    private const uint PidSecurity = 19;

    /// <summary>Builds the MSI described by <paramref name="definition"/> for <paramref name="request"/>.</summary>
    /// <exception cref="ArgumentException">The version is not a valid product version.</exception>
    /// <exception cref="MsiException">Windows Installer rejected the database.</exception>
    public static MsiBuildResult Build(ProductDefinition definition, MsiBuildRequest request)
    {
        ValidateVersion(request.Version);
        IReadOnlyList<SourceFile> files = SourceTree.Collect(request.SourceDirectory, definition.Exclude);
        SourceFile mainExecutable = files.FirstOrDefault(file => string.Equals(file.RelativePath, definition.MainExecutable.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Main executable '{definition.MainExecutable}' is not in {request.SourceDirectory}.");

        string outputPath = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        string workFolder = Directory.CreateTempSubdirectory("oe-msi-").FullName;
        try
        {
            Guid productCode = Guid.NewGuid();
            string databasePath = Path.Combine(workFolder, "package.msi");
            WriteDatabase(definition, request.Version, files, mainExecutable, productCode, databasePath, workFolder);
            File.Move(databasePath, outputPath, overwrite: true);
            return new MsiBuildResult(outputPath, productCode, files.Count);
        }
        finally
        {
            Directory.Delete(workFolder, recursive: true);
        }
    }

    private static void WriteDatabase(ProductDefinition definition, string version, IReadOnlyList<SourceFile> files,
        SourceFile mainExecutable, Guid productCode, string databasePath, string workFolder)
    {
        var directories = new DirectoryLayout(definition.InstallDirectory);
        var rows = new ProductRows(definition);
        var cabinetEntries = new List<CabinetEntry>(files.Count);

        using MsiDatabase database = MsiDatabase.Create(databasePath);
        database.SetCodepage(1252);
        foreach (string statement in MsiSchema.CreateStatements)
        {
            database.Execute(statement);
        }

        WriteFiles(database, rows, directories, files, cabinetEntries);
        rows.WriteProperties(database, MsiNames.Format(productCode), version);
        rows.WriteUpgrade(database, version);
        rows.WriteIcon(database, mainExecutable.FullPath);
        rows.WriteRegistry(database);
        rows.WriteShortcuts(database, directories);
        rows.WriteFeature(database);
        // Last: files and shortcuts create their directories on first use.
        directories.Write(database);
        StandardSequences.Write(database);

        string cabinetPath = Path.Combine(workFolder, CabinetName);
        CabinetWriter.Write(cabinetPath, cabinetEntries);
        database.Execute(MsiSchema.InsertStatement("Media", "DiskId", "LastSequence", "Cabinet"), 1, files.Count, "#" + CabinetName);
        database.Execute(MsiSchema.InsertStatement("_Streams", "Name", "Data"), CabinetName, new MsiStream(cabinetPath));

        database.SetSummaryInformation(SummaryInformation(definition));
        database.Commit();
    }

    private static void WriteFiles(MsiDatabase database, ProductRows rows, DirectoryLayout directories,
        IReadOnlyList<SourceFile> files, List<CabinetEntry> cabinetEntries)
    {
        using MsiStatement components = database.Prepare(MsiSchema.InsertStatement("Component", "Component", "ComponentId", "Directory_", "Attributes", "Condition", "KeyPath"));
        using MsiStatement fileRows = database.Prepare(MsiSchema.InsertStatement("File", "File", "Component_", "FileName", "FileSize", "Version", "Attributes", "Sequence"));
        for (int index = 0; index < files.Count; index++)
        {
            SourceFile file = files[index];
            if (file.Length > int.MaxValue)
            {
                throw new InvalidOperationException($"{file.FullPath} is larger than Windows Installer's 2 GiB file limit.");
            }

            string directory = directories.ForSourceDirectory(file.RelativeDirectory);
            string fileId = MsiNames.Identifier('f', file.RelativePath);
            string componentId = MsiNames.Identifier('c', file.RelativePath);
            rows.WriteFileComponent(components, componentId, file.RelativePath, directory, fileId);
            string name = MsiNames.FileName(file.Name, directories.ShortNames(directory));
            fileRows.Execute(fileId, componentId, name, (int)file.Length, FileVersion(file.FullPath), FileVital, index + 1);
            cabinetEntries.Add(new CabinetEntry(fileId, file.FullPath));
        }
    }

    /// <summary>The PE file version, or null for an unversioned file (Windows Installer then compares by date and hash).</summary>
    internal static string? FileVersion(string path)
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
        if (info.FileVersion is null)
        {
            return null;
        }

        return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
    }

    private static Dictionary<uint, object> SummaryInformation(ProductDefinition definition)
    {
        DateTime now = DateTime.UtcNow;
        return new Dictionary<uint, object>
        {
            [PidCodepage] = (short)1252,
            [PidTitle] = "Installation Database",
            [PidSubject] = definition.Name,
            [PidAuthor] = definition.Manufacturer,
            [PidKeywords] = "Installer",
            [PidComments] = $"This installer database contains the logic and data required to install {definition.Name}.",
            [PidTemplate] = "x64;1033",
            [PidRevisionNumber] = MsiNames.Format(Guid.NewGuid()),
            [PidCreated] = now,
            [PidLastSaved] = now,
            [PidPageCount] = 500,
            [PidWordCount] = 2,
            [PidApplicationName] = "OmniEurope.Installer",
            [PidSecurity] = 2,
        };
    }

    private static void ValidateVersion(string version)
    {
        Match match = VersionPattern().Match(version);
        bool valid = match.Success
            && int.Parse(match.Groups[1].Value) <= 255
            && int.Parse(match.Groups[2].Value) <= 255
            && int.Parse(match.Groups[3].Value) <= 65535;
        if (!valid)
        {
            throw new ArgumentException($"'{version}' is not a product version: expected major.minor.build with major and minor up to 255 and build up to 65535.", nameof(version));
        }
    }

    [GeneratedRegex(@"^(\d{1,3})\.(\d{1,3})\.(\d{1,5})$")]
    private static partial Regex VersionPattern();
}
