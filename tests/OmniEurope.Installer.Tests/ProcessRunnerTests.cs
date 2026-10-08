using System.Diagnostics;
using Shouldly;

namespace OmniEurope.Installer.Tests;

/// <summary>The runner every tool-driven test relies on must give up at its timeout, whatever the child does with its output.</summary>
public sealed class ProcessRunnerTests
{
    [Fact]
    public void A_child_that_keeps_running_is_stopped_at_the_timeout()
    {
        // cmd keeps its standard output open while ping runs: reading the output to its end would wait for ping.
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var watch = Stopwatch.StartNew();

        Should.Throw<TimeoutException>(() => ProcessRunner.Run(TimeSpan.FromSeconds(1), cmd, "/c", "ping -n 30 127.0.0.1 >nul"));

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void A_finished_child_returns_its_exit_code_and_output()
    {
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        (int exitCode, string output) = ProcessRunner.Run(cmd, "/c", "echo ready& exit 3");

        exitCode.ShouldBe(3);
        output.ShouldContain("ready");
    }
}
