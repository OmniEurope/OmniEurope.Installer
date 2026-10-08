using System.Diagnostics;

namespace OmniEurope.Installer.Tests;

internal static class ProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Runs a Windows tool to completion and returns its exit code and output.</summary>
    public static (int ExitCode, string Output) Run(string fileName, params string[] arguments) =>
        Run(DefaultTimeout, fileName, arguments);

    /// <summary>Runs a Windows tool to completion within <paramref name="timeout"/> and returns its exit code and output.</summary>
    public static (int ExitCode, string Output) Run(TimeSpan timeout, string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {fileName}.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        // The exit and both streams under one deadline: a child that keeps a stream open cannot outlast it.
        if (!Task.WhenAll(output, error, process.WaitForExitAsync()).Wait(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not finish within {timeout}.");
        }

        return (process.ExitCode, output.Result + error.Result);
    }
}
