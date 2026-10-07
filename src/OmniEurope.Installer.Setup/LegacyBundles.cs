using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.Win32;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Removes the "Installed apps" entry left by a former setup bundle once the MSI has replaced its
/// package. Runs the bundle's own quiet uninstall: the bundle then finds its package already gone,
/// removes only its registration and cache, and leaves the new MSI alone.
/// </summary>
internal static class LegacyBundles
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static void Remove(IReadOnlyCollection<string> bundleUpgradeCodes, Action<string> log)
    {
        if (bundleUpgradeCodes.Count == 0)
        {
            return;
        }

        foreach (string command in FindQuietUninstallCommands(bundleUpgradeCodes))
        {
            log($"Removing the former setup entry: {command}");
            (string fileName, string arguments) = SplitCommand(command);
            Process? process;
            try
            {
                process = Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false });
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // A cleaned Package Cache leaves the entry pointing at a missing program: the product is
                // installed all the same, so this must not turn a successful setup into a failure.
                log($"The former setup could not be started ({exception.Message}); its entry may remain.");
                continue;
            }

            using var started = process;
            if (process is null || !process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                log("The former setup did not finish in time; its entry may remain.");
                continue;
            }

            log($"Former setup removal exit code {process.ExitCode}");
        }
    }

    /// <summary>Splits "\"C:\path\setup.exe\" /uninstall /quiet" into the program and its arguments.</summary>
    internal static (string FileName, string Arguments) SplitCommand(string command)
    {
        string trimmed = command.Trim();
        if (trimmed.StartsWith("\"", StringComparison.Ordinal))
        {
            int closing = trimmed.IndexOf('"', 1);
            if (closing > 0)
            {
                return (trimmed.Substring(1, closing - 1), trimmed.Substring(closing + 1).Trim());
            }
        }

        int space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, string.Empty) : (trimmed.Substring(0, space), trimmed.Substring(space + 1).Trim());
    }

    private static IEnumerable<string> FindQuietUninstallCommands(IReadOnlyCollection<string> bundleUpgradeCodes)
    {
        foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                continue;
            }

            foreach (string name in uninstall.GetSubKeyNames())
            {
                using RegistryKey? entry = uninstall.OpenSubKey(name);
                if (entry is not null && Matches(entry.GetValue("BundleUpgradeCode"), bundleUpgradeCodes)
                    && entry.GetValue("QuietUninstallString") is string command && command.Length > 0)
                {
                    yield return command;
                }
            }
        }
    }

    private static bool Matches(object? value, IReadOnlyCollection<string> codes)
    {
        IEnumerable<string> present = value switch
        {
            string[] many => many,
            string one => new[] { one },
            _ => Array.Empty<string>(),
        };
        return present.Any(code => codes.Contains(code, StringComparer.OrdinalIgnoreCase));
    }
}
