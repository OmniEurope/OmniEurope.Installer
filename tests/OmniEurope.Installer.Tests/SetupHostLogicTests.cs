using System.Runtime.InteropServices;
using OmniEurope.Installer.Setup;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class SetupHostLogicTests
{
    private static SetupConfig Config() => new()
    {
        ProductName = "App",
        InstallDirectory = ["OmniEurope", "App"],
        LanguageProperty = "INSTALL_LANGUAGE",
        Options =
        [
            new SetupOption { Property = "INSTALL_DESKTOP_SHORTCUT", SettingName = "DesktopShortcut", DefaultChecked = true },
            new SetupOption { Property = "INSTALL_STARTUP", SettingName = "Startup", DefaultChecked = true },
        ],
    };

    [Theory]
    [InlineData(null, null, "{A}", "1.0.0", "Install")]
    [InlineData("{A}", "1.0.0", "{A}", "1.0.0", "Repair")]
    [InlineData("{B}", "1.9.0", "{A}", "1.10.0", "Upgrade")]
    [InlineData("{B}", "1.10.0", "{A}", "1.10.0", "SameVersion")]
    [InlineData("{B}", "2.0.0", "{A}", "1.10.0", "Downgrade")]
    [InlineData("{B}", "garbage", "{A}", "1.0.0", "Upgrade")]
    public void The_mode_follows_the_installed_product(string? installedCode, string? installedVersion, string code, string version, string expected)
    {
        SetupDecision.Decide(installedCode, installedVersion, code, version).ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("Downgrade", false, false)]
    [InlineData("Downgrade", true, true)]
    [InlineData("Repair", false, true)]
    [InlineData("Upgrade", true, true)]
    public void A_newer_installed_version_can_only_be_uninstalled(string mode, bool uninstall, bool expected)
    {
        SetupDecision.CanProceed(Enum.Parse<SetupMode>(mode), uninstall).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Acme/App", "Acme_App")]
    [InlineData(@"Acme\App: ""Pro""", "Acme_App_ _Pro_")]
    [InlineData("Sample App", "Sample App")]
    public void A_display_name_becomes_a_file_name_without_separators(string name, string expected)
    {
        SetupFileNames.Safe(name).ShouldBe(expected);
    }

    [Fact]
    public void Choices_become_quoted_properties_with_empty_values_for_unchecked_boxes()
    {
        SetupConfig config = Config();
        InstallChoices choices = InstallChoices.Defaults(config, @"C:\Program Files");
        choices.Options["INSTALL_STARTUP"] = false;
        choices.InstallFolder = @"D:\Apps\Say ""hi""\";

        string commandLine = choices.ToCommandLine(config, new Dictionary<string, string> { ["EXTRA"] = "x" });

        commandLine.ShouldBe(@"INSTALLFOLDER=""D:\Apps\Say """"hi"""""" INSTALL_LANGUAGE=""fr"" INSTALL_DESKTOP_SHORTCUT=""1"" INSTALL_STARTUP="""" EXTRA=""x""");
    }

    [Fact]
    public void Recorded_settings_override_the_defaults_and_missing_ones_are_kept()
    {
        SetupConfig config = Config();
        InstallChoices choices = InstallChoices.Defaults(config, @"C:\Program Files");
        var settings = new Dictionary<string, string> { ["Language"] = "en", ["InstallFolder"] = @"E:\App\", ["Startup"] = "" };

        choices.ApplySettings(name => settings.GetValueOrDefault(name), config);

        choices.Language.ShouldBe("en");
        choices.InstallFolder.ShouldBe(@"E:\App");
        choices.Options["INSTALL_STARTUP"].ShouldBeFalse();
        choices.Options["INSTALL_DESKTOP_SHORTCUT"].ShouldBeTrue();
        InstallChoices.Defaults(config, @"C:\Program Files").InstallFolder.ShouldBe(@"C:\Program Files\OmniEurope\App");
    }

    [Fact]
    public void Progress_follows_the_script_then_the_execution_pass()
    {
        var progress = new MsiProgress();
        progress.Percent.ShouldBeNull();

        progress.OnProgress("1: 0 2: 1000 3: 0 4: 1");
        progress.OnProgress("1: 2 2: 500 3: 0 4: 0");
        progress.Percent.ShouldBe(5);

        progress.OnProgress("1: 0 2: 2000 3: 0 4: 0");
        progress.OnProgress("1: 1 2: 100 3: 1 4: 0");
        for (int step = 0; step < 10; step++)
        {
            progress.OnActionData();
        }

        progress.Percent.ShouldBe(55);
        progress.OnProgress("1: 3 2: 2000 3: 0 4: 0");
        progress.Percent.ShouldBe(32);
    }

    [Fact]
    public void Backward_progress_counts_down_from_the_total()
    {
        var progress = new MsiProgress();
        progress.OnProgress("1: 0 2: 100 3: 1 4: 0");
        progress.OnProgress("1: 2 2: 50 3: 0 4: 0");

        progress.Percent.ShouldBe(55);
    }

    [Fact]
    public void The_command_line_reads_switches_and_properties()
    {
        SetupCommandLine line = SetupCommandLine.Parse(["/quiet", "/log", @"C:\t\s.log", "INSTALL_STARTUP=", "INSTALLFOLDER=D:\\A B"]);

        line.Quiet.ShouldBeTrue();
        line.Uninstall.ShouldBeFalse();
        line.LogPath.ShouldBe(@"C:\t\s.log");
        line.Properties["INSTALL_STARTUP"].ShouldBe(string.Empty);
        line.Properties["INSTALLFOLDER"].ShouldBe(@"D:\A B");
    }

    [Fact]
    public void The_command_line_reads_the_context_and_the_language()
    {
        SetupCommandLine line = SetupCommandLine.Parse(["/PerUser", "/lang", "EN"]);

        line.Scope.ShouldBe(InstallScope.PerUser);
        line.Language.ShouldBe("en");
        SetupCommandLine.Parse(["/permachine"]).Scope.ShouldBe(InstallScope.PerMachine);
        SetupCommandLine.Parse([]).Scope.ShouldBeNull();
    }

    [Theory]
    [InlineData("PerMachine", "PerUser", true, "PerMachine")]
    [InlineData("PerUser", null, true, "PerUser")]
    [InlineData(null, "PerUser", true, "PerUser")]
    [InlineData(null, null, true, "PerMachine")]
    [InlineData(null, null, false, null)]
    public void An_installed_product_keeps_its_context_and_a_quiet_first_install_is_per_machine(string? installed, string? requested, bool quiet, string? expected)
    {
        SetupDecision.Scope(Parse(installed), Parse(requested), quiet).ShouldBe(Parse(expected));

        static InstallScope? Parse(string? scope) => scope is null ? null : Enum.Parse<InstallScope>(scope);
    }

    [Theory]
    [InlineData("PerMachine", false, true)]
    [InlineData("PerMachine", true, false)]
    [InlineData("PerUser", false, false)]
    public void Only_a_per_machine_operation_needs_administrator_rights(string scope, bool elevated, bool expected)
    {
        SetupDecision.NeedsElevation(Enum.Parse<InstallScope>(scope), elevated).ShouldBe(expected);
    }

    [Fact]
    public void The_elevated_run_is_per_machine_and_keeps_every_other_argument()
    {
        List<string> arguments = SetupCommandLine.ForElevation(["/peruser", "/lang", "fr", "/log", @"C:\a b\s.log", "INSTALL_STARTUP="], "en");

        arguments.ShouldBe(["/log", @"C:\a b\s.log", "INSTALL_STARTUP=", "/permachine", "/lang", "en"]);
        SetupCommandLine elevated = SetupCommandLine.Parse(arguments);
        elevated.Scope.ShouldBe(InstallScope.PerMachine);
        elevated.Language.ShouldBe("en");
    }

    [Fact]
    public void Formatted_arguments_split_back_into_the_same_arguments()
    {
        string[] arguments = ["/quiet", @"C:\a b\s.log", "INSTALLFOLDER=C:\\Program Files\\App\\", "SAY=\"hi\"", string.Empty, @"\\server\share"];

        CommandLineToArguments(SetupCommandLine.Format(arguments)).ShouldBe(arguments);
    }

    [Fact]
    public void Only_French_and_English_are_languages()
    {
        Should.Throw<ArgumentException>(() => SetupCommandLine.Parse(["/lang", "de"]));
    }

    [Theory]
    [InlineData("/unknown")]
    [InlineData("lowercase=1")]
    [InlineData("/log")]
    [InlineData("/lang")]
    public void An_unknown_or_incomplete_argument_is_refused(string argument)
    {
        Should.Throw<ArgumentException>(() => SetupCommandLine.Parse([argument]));
    }

    [Theory]
    [InlineData(@"""C:\ProgramData\Package Cache\{X}\App-Setup.exe""  /uninstall /quiet", @"C:\ProgramData\Package Cache\{X}\App-Setup.exe", "/uninstall /quiet")]
    [InlineData(@"C:\Setup.exe /x", @"C:\Setup.exe", "/x")]
    [InlineData(@"C:\Setup.exe", @"C:\Setup.exe", "")]
    public void A_quiet_uninstall_command_is_split_into_program_and_arguments(string command, string fileName, string arguments)
    {
        LegacyBundles.SplitCommand(command).ShouldBe((fileName, arguments));
    }

    [Fact]
    public void The_bar_chases_the_reported_progress_without_passing_it()
    {
        ProgressAnimation.Step(null, null).ShouldBeNull();
        ProgressAnimation.Step(null, 40).ShouldBe(10);
        ProgressAnimation.Step(38, 40).ShouldBe(39);
        ProgressAnimation.Step(40, 40).ShouldBe(40);
        ProgressAnimation.Step(50, 40).ShouldBe(50);
    }

    // Windows' own parser: what a started program receives from a command line.
    private static string[] CommandLineToArguments(string commandLine)
    {
        // The program name comes first, as in a full command line.
        IntPtr argv = CommandLineToArgvW("setup.exe " + commandLine, out int count);
        try
        {
            var arguments = new string[count - 1];
            for (int index = 1; index < count; index++)
            {
                arguments[index - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * IntPtr.Size))!;
            }

            return arguments;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
