using OmniEurope.Installer.Building;
using OmniEurope.Installer.Msi;
using OmniEurope.Installer.Product;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class MsiPackageBuilderTests : IDisposable
{
    private static readonly Guid UpgradeCode = Guid.Parse("6D1C2B6E-4F0A-4C47-9C4E-0E0F3C2D1A55");

    private readonly TempFolder _temp = new();
    private readonly string _source;

    public MsiPackageBuilderTests()
    {
        _source = _temp.Combine("publish");
        Directory.CreateDirectory(_source);
        File.Copy(Path.Combine(Environment.SystemDirectory, "expand.exe"), Path.Combine(_source, "Sample.exe"));
        _temp.WriteFile(@"publish\readme.txt", "read me"u8.ToArray());
        _temp.WriteFile(@"publish\wwwroot\css\app.css", "body { color: red; }"u8.ToArray());
        byte[] satellite = new byte[100_000];
        new Random(3).NextBytes(satellite);
        _temp.WriteFile(@"publish\fr\Sample.resources.dll", satellite);
        _temp.WriteFile(@"publish\old.msi", "excluded"u8.ToArray());
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_database_holds_the_product_identity_files_and_major_upgrade()
    {
        MsiBuildResult result = Build("1.2.3");

        result.FileCount.ShouldBe(4);
        using MsiDatabase database = MsiDatabase.OpenReadOnly(result.OutputPath);
        Property(database, "ProductVersion").ShouldBe("1.2.3");
        Property(database, "UpgradeCode").ShouldBe("{6D1C2B6E-4F0A-4C47-9C4E-0E0F3C2D1A55}");
        Property(database, "ProductCode").ShouldBe(result.ProductCode.ToString("B").ToUpperInvariant());
        Property(database, "ALLUSERS").ShouldBe("2");
        Property(database, "INSTALL_STARTUP").ShouldBe("1");
        Property(database, "REINSTALLMODE").ShouldBe("amus");
        Property(database, "ARPNOREPAIR").ShouldBe("1");
        database.Query("SELECT `File` FROM `File`").Count.ShouldBe(4);
        database.Query("SELECT `Version` FROM `File` WHERE `FileName` = ?", "Sample.exe").ShouldHaveSingleItem()[0].ShouldNotBeNull();

        var upgrade = database.Query("SELECT `VersionMin`, `VersionMax`, `Attributes`, `ActionProperty` FROM `Upgrade`")
            .OrderBy(row => row[3]).ToList();
        upgrade[0].ShouldBe(["1.2.3", null, "2", "OE_DOWNGRADE_DETECTED"], ignoreOrder: false);
        upgrade[1].ShouldBe([null, "1.2.3", "513", "OE_UPGRADE_DETECTED"], ignoreOrder: false);
    }

    [Fact]
    public void Shortcuts_registry_and_summary_follow_the_definition()
    {
        MsiBuildResult result = Build("1.2.3");

        using MsiDatabase database = MsiDatabase.OpenReadOnly(result.OutputPath);
        database.Query("SELECT `Target`, `WkDir`, `Icon_` FROM `Shortcut` WHERE `Shortcut` = ?", "Menu_Shortcut")
            .ShouldHaveSingleItem().ShouldBe(["[INSTALLFOLDER]Sample.exe", "INSTALLFOLDER", "ProductIcon.exe"], ignoreOrder: false);
        database.Query("SELECT `Condition` FROM `Component` WHERE `Component` = ?", "Desktop").ShouldHaveSingleItem()[0].ShouldBe("WANT_DESKTOP");
        database.Query("SELECT `Value` FROM `Registry` WHERE `Registry` = ?", "Settings_0").ShouldHaveSingleItem()[0].ShouldBe("[INSTALLFOLDER]");
        database.Query("SELECT `Value` FROM `Registry` WHERE `Registry` = ?", "Settings_1").ShouldHaveSingleItem()[0].ShouldBe("#7");
        database.Query("SELECT `Root` FROM `Registry` WHERE `Registry` = ?", "Menu_KeyPath").ShouldHaveSingleItem()[0].ShouldBe("-1");
        database.Query("SELECT `Root` FROM `Registry` WHERE `Registry` = ?", "Settings_0").ShouldHaveSingleItem()[0].ShouldBe("-1");
        database.Query("SELECT `Root`, `Value` FROM `Registry` WHERE `Registry` = ?", "Machine_0").ShouldHaveSingleItem().ShouldBe(["2", "##literal"], ignoreOrder: false);
        database.Query("SELECT `Root` FROM `Registry` WHERE `Registry` = ?", "User_0").ShouldHaveSingleItem()[0].ShouldBe("1");
        database.Query("SELECT `FileKey` FROM `RemoveFile`").ShouldHaveSingleItem()[0].ShouldBe("Menu_Folder");
        database.GetSummaryProperty(7).ShouldBe("x64;1033");
        database.GetSummaryProperty(15).ShouldBe(2);
        database.GetSummaryProperty(1).ShouldBe(1252);
    }

    [Fact]
    public void An_administrative_install_extracts_every_file_unchanged()
    {
        MsiBuildResult result = Build("1.2.3");
        string target = _temp.Combine("admin");
        string log = _temp.Combine("admin.log");

        (int exitCode, _) = ProcessRunner.Run("msiexec.exe", "/a", result.OutputPath, "/qn", $"TARGETDIR={target}", "/l*v", log);

        exitCode.ShouldBe(0, File.Exists(log) ? File.ReadAllText(log) : "no msiexec log");
        string installRoot = Path.GetDirectoryName(Directory.GetFiles(target, "Sample.exe", SearchOption.AllDirectories).ShouldHaveSingleItem())!;
        installRoot.ShouldEndWith(@"OmniEurope\App");
        foreach (string relative in new[] { "Sample.exe", "readme.txt", @"wwwroot\css\app.css", @"fr\Sample.resources.dll" })
        {
            File.ReadAllBytes(Path.Combine(installRoot, relative)).ShouldBe(File.ReadAllBytes(Path.Combine(_source, relative)), relative);
        }

        File.Exists(Path.Combine(installRoot, "old.msi")).ShouldBeFalse();
    }

    [Fact]
    public void Component_guids_do_not_change_between_versions()
    {
        MsiBuildResult first = Build("1.0.0", "first.msi");
        MsiBuildResult second = Build("1.0.1", "second.msi");

        ComponentGuids(first.OutputPath).ShouldBe(ComponentGuids(second.OutputPath));
        first.ProductCode.ShouldNotBe(second.ProductCode);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("256.0.0")]
    [InlineData("1.0.65536")]
    [InlineData("1.0.0-beta")]
    [InlineData("١.٢.٣")]
    public void An_invalid_product_version_is_refused(string version)
    {
        Should.Throw<ArgumentException>(() => Build(version));
    }

    [Fact]
    public void Shortcuts_in_the_same_start_menu_folder_share_it_and_a_folderless_one_goes_to_the_menu_root()
    {
        ProductDefinition definition = Definition() with
        {
            Shortcuts =
            [
                new ShortcutDefinition { Id = "Menu", Location = ShortcutLocation.ProgramMenu, Folder = "App", Name = "App", RegistryKey = @"Software\OmniEurope\App", RegistryName = "Menu" },
                new ShortcutDefinition { Id = "Help", Location = ShortcutLocation.ProgramMenu, Folder = "App", Name = "App Help", RegistryKey = @"Software\OmniEurope\App", RegistryName = "Help" },
                new ShortcutDefinition { Id = "Top", Location = ShortcutLocation.ProgramMenu, Name = "App Top", RegistryKey = @"Software\OmniEurope\App", RegistryName = "Top" },
            ],
        };

        MsiBuildResult result = MsiPackageBuilder.Build(definition, new MsiBuildRequest(_source, "1.0.0", _temp.Combine("out", "menu.msi")));

        using MsiDatabase database = MsiDatabase.OpenReadOnly(result.OutputPath);
        var directories = database.Query("SELECT `Shortcut`, `Directory_` FROM `Shortcut`").ToDictionary(row => row[0]!, row => row[1]);
        directories["Menu_Shortcut"].ShouldBe(directories["Help_Shortcut"]);
        directories["Top_Shortcut"].ShouldBe("ProgramMenuFolder");
        database.Query("SELECT `Directory` FROM `Directory` WHERE `Directory_Parent` = ?", "ProgramMenuFolder").ShouldHaveSingleItem();
    }

    [Fact]
    public void An_unknown_registry_root_is_refused_rather_than_mapped_to_a_hive()
    {
        ProductDefinition definition = Definition() with
        {
            RegistryComponents = [Definition().RegistryComponents[0] with { Root = (RegistryRoot)99 }],
        };

        Should.Throw<InvalidDataException>(() => MsiPackageBuilder.Build(definition, new MsiBuildRequest(_source, "1.0.0", _temp.Combine("out", "x.msi"))))
            .Message.ShouldContain("unknown root");
    }

    [Fact]
    public void A_missing_or_empty_published_folder_is_refused()
    {
        string empty = _temp.Combine("empty");
        Directory.CreateDirectory(empty);

        Should.Throw<DirectoryNotFoundException>(() => MsiPackageBuilder.Build(Definition(), new MsiBuildRequest(_temp.Combine("missing"), "1.0.0", _temp.Combine("out", "x.msi"))));
        Should.Throw<InvalidOperationException>(() => MsiPackageBuilder.Build(Definition(), new MsiBuildRequest(empty, "1.0.0", _temp.Combine("out", "y.msi"))))
            .Message.ShouldContain("No file to install");
    }

    [Fact]
    public void A_missing_main_executable_is_refused()
    {
        File.Delete(Path.Combine(_source, "Sample.exe"));

        Should.Throw<FileNotFoundException>(() => Build("1.0.0")).Message.ShouldContain("Sample.exe");
    }

    private MsiBuildResult Build(string version, string fileName = "app.msi") =>
        MsiPackageBuilder.Build(Definition(), new MsiBuildRequest(_source, version, _temp.Combine("out", fileName)));

    private static Dictionary<string, string?> ComponentGuids(string msi)
    {
        using MsiDatabase database = MsiDatabase.OpenReadOnly(msi);
        return database.Query("SELECT `Component`, `ComponentId` FROM `Component`").ToDictionary(row => row[0]!, row => row[1]);
    }

    private static string? Property(MsiDatabase database, string name) =>
        database.Query("SELECT `Value` FROM `Property` WHERE `Property` = ?", name).ShouldHaveSingleItem()[0];

    private static ProductDefinition Definition() => new()
    {
        Name = "App",
        Manufacturer = "OmniEurope",
        UpgradeCode = UpgradeCode,
        MainExecutable = "Sample.exe",
        InstallDirectory = ["OmniEurope", "App"],
        DowngradeErrorMessage = "A newer version of App is already installed.",
        Exclude = ["*.msi"],
        Properties = new Dictionary<string, string> { ["INSTALL_STARTUP"] = "1", ["WANT_DESKTOP"] = "1" },
        Shortcuts =
        [
            new ShortcutDefinition { Id = "Desktop", Location = ShortcutLocation.Desktop, Name = "App", Condition = "WANT_DESKTOP", RegistryKey = @"Software\OmniEurope\App", RegistryName = "Desktop" },
            new ShortcutDefinition { Id = "Menu", Location = ShortcutLocation.ProgramMenu, Folder = "App", Name = "App", RegistryKey = @"Software\OmniEurope\App", RegistryName = "Menu" },
        ],
        RegistryComponents =
        [
            new RegistryComponentDefinition
            {
                Id = "Settings",
                Root = RegistryRoot.Installation,
                Key = @"Software\OmniEurope\App\Installer",
                Values =
                [
                    new RegistryValueDefinition { Name = "InstallFolder", Type = RegistryValueType.String, Value = "[INSTALLFOLDER]" },
                    new RegistryValueDefinition { Name = "Count", Type = RegistryValueType.Integer, Value = "7" },
                ],
            },
            new RegistryComponentDefinition
            {
                Id = "Machine",
                Root = RegistryRoot.LocalMachine,
                Key = @"Software\OmniEurope\App\Machine",
                Values = [new RegistryValueDefinition { Name = "Tag", Type = RegistryValueType.String, Value = "#literal" }],
            },
            new RegistryComponentDefinition
            {
                Id = "User",
                Root = RegistryRoot.CurrentUser,
                Key = @"Software\OmniEurope\App\User",
                Values = [new RegistryValueDefinition { Type = RegistryValueType.String, Value = "on" }],
            },
        ],
    };
}
