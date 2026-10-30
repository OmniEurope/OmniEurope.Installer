using OmniEurope.Installer.Tests.Guards;
using Shouldly;

namespace OmniEurope.Installer.Tests;

public sealed class PipelineGuardTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData(0, 0, 0, "Completed", 1)]
    [InlineData(1, 0, 1, "Failed", 1)]
    [InlineData(1, 0, 0, "Completed", 1)]
    [InlineData(1, 1, 0, "Failed", 1)]
    [InlineData(1, 1, 0, "Completed", 0)]
    public void The_real_trx_guard_checks_execution_and_success(int executed, int passed, int failed, string outcome, int expectedExit)
    {
        File.WriteAllText(_temp.Combine("tests.trx"), $"""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="{outcome}">
                <Counters total="{executed}" executed="{executed}" passed="{passed}" failed="{failed}" />
              </ResultSummary>
            </TestRun>
            """);

        (int code, string output) = RunGuard();
        code.ShouldBe(expectedExit, output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<TestRun />")]
    [InlineData("not XML")]
    public void The_real_trx_guard_refuses_empty_or_invalid_reports(string text)
    {
        File.WriteAllText(_temp.Combine("tests.trx"), text);

        RunGuard().ExitCode.ShouldBe(1);
    }

    [Fact]
    public void The_real_trx_guard_refuses_a_missing_report() => RunGuard().ExitCode.ShouldBe(1);

    private (int ExitCode, string Output) RunGuard() => ProcessRunner.Run("pwsh", "-NoProfile", "-File",
        Path.Combine(RepositoryScan.Root, ".github", "scripts", "verify-test-results.ps1"),
        "-ResultsDirectory", _temp.Path, "-ReportName", "tests.trx");
}
