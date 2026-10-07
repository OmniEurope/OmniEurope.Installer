using System.Diagnostics;

namespace OmniEurope.Installer.Tests;

internal static class ProcessRunner
{
    /// <summary>Runs a Windows tool to completion and returns its exit code and output.</summary>
    public static (int ExitCode, string Output) Run(string fileName, params string[] arguments)
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
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not finish within 5 minutes.");
        }

        return (process.ExitCode, output + error.Result);
    }
}
