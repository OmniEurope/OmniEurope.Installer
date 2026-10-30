using System.Diagnostics;
using OmniEurope.Installer.Tests;
using OmniEurope.Installer.Tests.Guards;
using Shouldly;

namespace OmniEurope.Installer.IntegrationTests;

/// <summary>
/// The setup executable run for real, quietly, on the fixture product: each scenario starts from a
/// machine without the fixture and leaves it that way. Tests of one class run one after the other.
/// </summary>
public sealed class SetupScenarioTests : IClassFixture<FixtureProduct>, IDisposable
{
    private const int Success = 0;
    private const int SuccessRestartRequired = 3010;
    private const int AnotherVersionInstalled = 1638;

    private readonly FixtureProduct _fixture;

    public SetupScenarioTests(FixtureProduct fixture)
    {
        _fixture = fixture;
        FixtureProduct.Entries().ShouldBeEmpty("a previous scenario left the fixture installed");
    }

    public void Dispose()
    {
        if (FixtureProduct.Entries().Count > 0)
        {
            _fixture.Run(_fixture.SetupV2, "/uninstall");
        }
    }

    [Fact]
    public void A_quiet_install_then_uninstall_leaves_nothing_behind()
    {
        _fixture.Run(_fixture.SetupV1).ExitCode.ShouldBe(Success);

        var entry = FixtureProduct.Entries().ShouldHaveSingleItem();
        entry.Version.ShouldBe("1.0.0");
        File.ReadAllText(Path.Combine(FixtureProduct.InstallFolder, FixtureProduct.DataFile)).ShouldBe("version 1.0.0");
        File.Exists(Path.Combine(FixtureProduct.InstallFolder, FixtureProduct.MainExecutable)).ShouldBeTrue();
        File.Exists(FixtureProduct.StartMenuShortcut).ShouldBeTrue();
        FixtureProduct.RecordedInstallFolder().ShouldNotBeNullOrEmpty();

        _fixture.Run(_fixture.SetupV1, "/uninstall").ExitCode.ShouldBe(Success);

        FixtureProduct.Entries().ShouldBeEmpty();
        Directory.Exists(FixtureProduct.InstallFolder).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(FixtureProduct.InstallFolder)).ShouldBeFalse("the manufacturer folder created by the install");
        File.Exists(FixtureProduct.StartMenuShortcut).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(FixtureProduct.StartMenuShortcut)).ShouldBeFalse("the Start menu folder created by the install");
        FixtureProduct.RecordedInstallFolder().ShouldBeNull();
    }

    [Fact]
    public void An_update_keeps_a_single_entry_and_an_older_version_is_refused()
    {
        _fixture.Run(_fixture.SetupV1).ExitCode.ShouldBe(Success);
        string firstProductCode = FixtureProduct.Entries().ShouldHaveSingleItem().ProductCode;

        _fixture.Run(_fixture.SetupV2).ExitCode.ShouldBe(Success);

        var updated = FixtureProduct.Entries().ShouldHaveSingleItem();
        updated.Version.ShouldBe("2.0.0");
        updated.ProductCode.ShouldNotBe(firstProductCode);
        File.ReadAllText(Path.Combine(FixtureProduct.InstallFolder, FixtureProduct.DataFile)).ShouldBe("version 2.0.0");

        _fixture.Run(_fixture.SetupV1).ExitCode.ShouldBe(AnotherVersionInstalled);
        FixtureProduct.Entries().ShouldHaveSingleItem().Version.ShouldBe("2.0.0");
    }

    [Fact]
    public void The_running_application_is_closed_before_its_files_are_replaced()
    {
        _fixture.Run(_fixture.SetupV1).ExitCode.ShouldBe(Success);
        string executable = Path.Combine(FixtureProduct.InstallFolder, FixtureProduct.MainExecutable);
        using Process running = Process.Start(new ProcessStartInfo(executable, "/d /c ping -n 300 127.0.0.1 >nul") { CreateNoWindow = true })!;
        try
        {
            (int exitCode, string log) = _fixture.Run(_fixture.SetupV2);

            exitCode.ShouldBe(Success);
            log.ShouldContain($"Closing FixtureApp (PID {running.Id})");
            log.ShouldNotContain("left running");
            running.HasExited.ShouldBeTrue();
            File.ReadAllText(Path.Combine(FixtureProduct.InstallFolder, FixtureProduct.DataFile)).ShouldBe("version 2.0.0");
        }
        finally
        {
            Stop(running);
        }
    }

    [Fact]
    public void The_entry_left_by_a_former_setup_is_removed_after_installing()
    {
        FixtureProduct.CreateLegacyEntry();
        FixtureProduct.LegacyEntryExists().ShouldBeTrue();

        (int exitCode, string log) = _fixture.Run(_fixture.SetupV1);

        exitCode.ShouldBe(Success);
        log.ShouldContain("Removing the former setup entry");
        log.ShouldContain("Former setup removal exit code 0");
        FixtureProduct.LegacyEntryExists().ShouldBeFalse();
        FixtureProduct.Entries().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_file_held_by_another_program_gives_3010_and_never_restarts_the_computer()
    {
        _fixture.Run(_fixture.SetupV1).ExitCode.ShouldBe(Success);
        string data = Path.Combine(FixtureProduct.InstallFolder, FixtureProduct.DataFile);

        // Another program, outside the install folder, keeps the file open without delete sharing:
        // Windows Installer can replace it only at the next restart.
        string held = Path.Combine(Path.GetTempPath(), $"oe-fixture-held-{Guid.NewGuid():N}");
        string holder = $"$s = [IO.File]::Open({Quoted(data)}, 'Open', 'Read', 'Read'); New-Item -ItemType File {Quoted(held)} | Out-Null; Start-Sleep -Seconds 300";
        using Process holding = Process.Start(new ProcessStartInfo("powershell.exe", ["-NoProfile", "-Command", holder]) { CreateNoWindow = true })!;
        try
        {
            WaitForFile(held, holding);
            (int exitCode, string log) = _fixture.Run(_fixture.SetupV2);

            exitCode.ShouldBe(SuccessRestartRequired, log.Length > 4000 ? log[^4000..] : log);
            FixtureProduct.Entries().ShouldHaveSingleItem().Version.ShouldBe("2.0.0");
        }
        finally
        {
            Stop(holding);
            File.Delete(held);
        }
    }

    [Fact]
    public void A_per_user_install_lands_in_the_user_profile_and_its_uninstall_leaves_nothing()
    {
        _fixture.Run(_fixture.SetupV1, "/peruser").ExitCode.ShouldBe(Success);

        var entry = FixtureProduct.Entries().ShouldHaveSingleItem();
        entry.PerUser.ShouldBeTrue();
        File.ReadAllText(Path.Combine(FixtureProduct.UserInstallFolder, FixtureProduct.DataFile)).ShouldBe("version 1.0.0");
        Directory.Exists(FixtureProduct.InstallFolder).ShouldBeFalse("a per-user install writes nothing under Program Files");
        File.Exists(FixtureProduct.UserStartMenuShortcut).ShouldBeTrue();
        File.Exists(FixtureProduct.StartMenuShortcut).ShouldBeFalse();
        FixtureProduct.RecordedInstallFolder(perUser: true).ShouldNotBeNullOrEmpty();
        FixtureProduct.RecordedInstallFolder().ShouldBeNull();

        _fixture.Run(_fixture.SetupV1, "/uninstall").ExitCode.ShouldBe(Success);

        FixtureProduct.Entries().ShouldBeEmpty();
        Directory.Exists(FixtureProduct.UserInstallFolder).ShouldBeFalse();
        File.Exists(FixtureProduct.UserStartMenuShortcut).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(FixtureProduct.UserStartMenuShortcut)).ShouldBeFalse("the Start menu folder created by the install");
        FixtureProduct.RecordedInstallFolder(perUser: true).ShouldBeNull();
    }

    [Fact]
    public void An_update_keeps_the_context_of_the_installed_product()
    {
        _fixture.Run(_fixture.SetupV1, "/peruser").ExitCode.ShouldBe(Success);

        // No /peruser: a quiet first install would be per machine, but the installed product's context wins.
        _fixture.Run(_fixture.SetupV2).ExitCode.ShouldBe(Success);

        var updated = FixtureProduct.Entries().ShouldHaveSingleItem();
        updated.Version.ShouldBe("2.0.0");
        updated.PerUser.ShouldBeTrue();
        File.ReadAllText(Path.Combine(FixtureProduct.UserInstallFolder, FixtureProduct.DataFile)).ShouldBe("version 2.0.0");
        Directory.Exists(FixtureProduct.InstallFolder).ShouldBeFalse();
    }

    [Fact]
    public void A_real_standard_user_installs_updates_and_uninstalls_without_administrator_membership()
    {
        (int code, string output) = ProcessRunner.Run("pwsh", "-NoProfile", "-File",
            Path.Combine(RepositoryScan.Root, ".github", "scripts", "test-standard-user.ps1"),
            "-SetupV1", _fixture.SetupV1, "-SetupV2", _fixture.SetupV2);

        code.ShouldBe(0, output);
        output.ShouldContain("Standard-user install, update and uninstall verified.");
        FixtureProduct.Entries().ShouldBeEmpty("the standard user's installation must not affect the controller's context");
        Directory.Exists(FixtureProduct.InstallFolder).ShouldBeFalse();
        FixtureProduct.RecordedInstallFolder().ShouldBeNull();
    }

    [Fact]
    public void The_msi_alone_installs_per_machine_as_a_group_policy_assignment_does()
    {
        string log = Path.Combine(Path.GetTempPath(), $"oe-fixture-msi-{Guid.NewGuid():N}.log");
        try
        {
            (int exitCode, _) = ProcessRunner.Run("msiexec.exe", "/i", _fixture.MsiV1, "/qn", "REBOOT=ReallySuppress", "/l*v", log);

            exitCode.ShouldBe(Success);
            FixtureProduct.Entries().ShouldHaveSingleItem().PerUser.ShouldBeFalse();
            File.Exists(Path.Combine(FixtureProduct.InstallFolder, FixtureProduct.MainExecutable)).ShouldBeTrue();
            File.Exists(FixtureProduct.StartMenuShortcut).ShouldBeTrue();
            FixtureProduct.RecordedInstallFolder().ShouldNotBeNullOrEmpty();

            // The setup then finds that per-machine installation and updates it in place.
            _fixture.Run(_fixture.SetupV2).ExitCode.ShouldBe(Success);
            var updated = FixtureProduct.Entries().ShouldHaveSingleItem();
            updated.Version.ShouldBe("2.0.0");
            updated.PerUser.ShouldBeFalse();
        }
        finally
        {
            File.Delete(log);
        }
    }

    // A PowerShell single-quoted literal: a quote inside the path is doubled.
    private static string Quoted(string path) => $"'{path.Replace("'", "''")}'";

    private static void Stop(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill();
            process.WaitForExit();
        }
    }

    // The holder creates the marker only once the file is open, so the setup never starts too early.
    private static void WaitForFile(string path, Process holder)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(path))
        {
            if (holder.HasExited || DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"The file holder never started (exited: {holder.HasExited}).");
            }

            Thread.Sleep(200);
        }
    }
}
