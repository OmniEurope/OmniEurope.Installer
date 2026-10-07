using OmniEurope.Installer.Msi;
using OmniEurope.Installer.Product;

namespace OmniEurope.Installer.Building;

/// <summary>
/// The Directory table: the install folder chain under 64-bit Program Files, every sub-folder of the
/// published folder, and the shortcut folders. Also owns the short names taken in each folder, since
/// files and sub-folders of one folder share the same 8.3 namespace.
/// </summary>
internal sealed class DirectoryLayout
{
    public const string TargetDir = "TARGETDIR";
    public const string InstallFolder = "INSTALLFOLDER";
    public const string ProgramFiles = "ProgramFiles64Folder";
    public const string ProgramMenu = "ProgramMenuFolder";
    public const string Desktop = "DesktopFolder";

    private readonly List<(string Id, string? Parent, string DefaultDir)> _rows = [];
    private readonly Dictionary<string, string> _sourceDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _menuFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _shortNames = new(StringComparer.Ordinal);

    public DirectoryLayout(IReadOnlyList<string> installChain)
    {
        _rows.Add((TargetDir, null, "SourceDir"));
        _rows.Add((ProgramFiles, TargetDir, "."));
        _rows.Add((ProgramMenu, TargetDir, "."));
        _rows.Add((Desktop, TargetDir, "."));

        string parent = ProgramFiles;
        for (int index = 0; index < installChain.Count; index++)
        {
            bool last = index == installChain.Count - 1;
            string id = last ? InstallFolder : MsiNames.Identifier('d', "install:" + string.Join('\\', installChain.Take(index + 1)));
            Add(id, parent, installChain[index]);
            parent = id;
        }

        _sourceDirectories[string.Empty] = InstallFolder;
    }

    /// <summary>The directory id of a folder relative to the published folder, created on first use.</summary>
    public string ForSourceDirectory(string relativeDirectory)
    {
        if (_sourceDirectories.TryGetValue(relativeDirectory, out string? id))
        {
            return id;
        }

        string parent = ForSourceDirectory(Path.GetDirectoryName(relativeDirectory) ?? string.Empty);
        id = MsiNames.Identifier('d', "source:" + relativeDirectory);
        Add(id, parent, Path.GetFileName(relativeDirectory));
        _sourceDirectories[relativeDirectory] = id;
        return id;
    }

    /// <summary>The directory id where <paramref name="shortcut"/> is created.</summary>
    public string ForShortcut(ShortcutDefinition shortcut)
    {
        if (shortcut.Location == ShortcutLocation.Desktop)
        {
            return Desktop;
        }

        if (shortcut.Folder is null)
        {
            return ProgramMenu;
        }

        if (!_menuFolders.TryGetValue(shortcut.Folder, out string? id))
        {
            id = MsiNames.Identifier('d', "menu:" + shortcut.Folder);
            Add(id, ProgramMenu, shortcut.Folder);
            _menuFolders[shortcut.Folder] = id;
        }

        return id;
    }

    /// <summary>The short names already used in a directory.</summary>
    public ISet<string> ShortNames(string directoryId)
    {
        if (!_shortNames.TryGetValue(directoryId, out HashSet<string>? names))
        {
            names = new HashSet<string>(StringComparer.Ordinal);
            _shortNames[directoryId] = names;
        }

        return names;
    }

    public void Write(MsiDatabase database)
    {
        using MsiStatement insert = database.Prepare(MsiSchema.InsertStatement("Directory", "Directory", "Directory_Parent", "DefaultDir"));
        foreach ((string id, string? parent, string defaultDir) in _rows)
        {
            insert.Execute(id, parent, defaultDir);
        }
    }

    private void Add(string id, string parent, string name)
    {
        _rows.Add((id, parent, MsiNames.FileName(name, ShortNames(parent))));
    }
}
