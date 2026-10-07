using OmniEurope.Installer.Setup;
using Shouldly;

namespace OmniEurope.Installer.Tests;

/// <summary>
/// A quiet install with a file in use once restarted the computer on its own (1641): every operation
/// must suppress the automatic restart, and 3010 must read as a success that asks for a restart.
/// </summary>
public sealed class MsiCommandLinesTests
{
    [Fact]
    public void Every_operation_suppresses_the_automatic_restart()
    {
        MsiCommandLines.Install(@"INSTALLFOLDER=""C:\App""").ShouldBe(@"INSTALLFOLDER=""C:\App"" REBOOT=ReallySuppress");
        MsiCommandLines.Install(string.Empty).ShouldBe("REBOOT=ReallySuppress");
        MsiCommandLines.Repair().ShouldBe("REINSTALL=ALL REINSTALLMODE=amus REBOOT=ReallySuppress");
        MsiCommandLines.Uninstall().ShouldBe("REBOOT=ReallySuppress");
    }

    [Fact]
    public void A_REBOOT_override_comes_before_the_suppression_which_therefore_wins()
    {
        string commandLine = MsiCommandLines.Install(@"REBOOT=""Force""");

        commandLine.ShouldEndWith(" REBOOT=ReallySuppress");
    }

    [Theory]
    [InlineData(0u, true, false)]
    [InlineData(3010u, true, true)]
    [InlineData(1641u, false, false)]
    [InlineData(1603u, false, false)]
    [InlineData(1602u, false, false)]
    public void Results_read_as_success_and_restart_required(uint result, bool success, bool restartRequired)
    {
        MsiCommandLines.IsSuccess(result).ShouldBe(success);
        MsiCommandLines.RestartRequired(result).ShouldBe(restartRequired);
    }
}
