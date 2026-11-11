// kit-model RepositoryScan 2
//
// File enumeration for repository-wide guard tests, with one property the raw
// Directory.EnumerateFiles does not have: it refuses to return nothing.
//
// Why: a guard that scans the repository and finds no file passes. Every assertion it makes is
// vacuously true over an empty set, so it reports green while protecting nothing (seen in a nested checkout:
// a guard excluded the whole repository when the suite ran from a worktree).
//
// Version 2: build output AND nested checkouts are skipped (.git, .worktrees). A
// nested checkout is another branch's code and changes under the scan: a parallel agent deleting a file
// made two architecture guards throw FileNotFoundException (observed in a nested checkout).
//
// Public-repository adaptation of version 2: a nested checkout is recognised by the .git entry (folder
// or file) at its root instead of by the name of the folder a tool keeps its worktrees in, so every
// worktree location is skipped without naming any tool. This covers every folder version 2 skips.
//
// Copy as is next to the guards that use it (FileSizeAuditTests) and adjust only the namespace. The
// repository root is the folder holding global.json, found by walking up from the test binary, so no
// path constant is needed.
// Keep the first line of this file in the copy: it tells the kit's verify-rules.ps1 which version of
// the model the copy implements.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmniEurope.Installer.Tests.Guards;

internal static class RepositoryScan
{
    /// <summary>The repository root: the nearest ancestor of the test binary holding global.json.</summary>
    internal static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root (global.json).");
        }
    }

    /// <summary>Enumerates and fails loudly when the scan is empty.</summary>
    internal static IReadOnlyList<string> Enumerate(string directory, string pattern)
    {
        var files = EnumerateOptional(directory, pattern);
        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"Scanning '{directory}' for '{pattern}' returned no file. A guard that scans nothing "
                + "passes vacuously, so this is reported as a failure rather than a green run.");
        }

        return files;
    }

    /// <summary>
    /// Enumerates without the floor, skipping build output and nested checkouts. Only for one leg of a sweep whose total
    /// the caller still proves non-empty.
    /// </summary>
    internal static IReadOnlyList<string> EnumerateOptional(string directory, string pattern)
    {
        var files = new List<string>();
        if (Directory.Exists(directory))
        {
            Collect(directory, pattern, files);
        }

        return files;
    }

    private static readonly HashSet<string> SkippedFolders =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", ".git", ".worktrees" };

    private static void Collect(string directory, string pattern, List<string> files)
    {
        files.AddRange(Directory.EnumerateFiles(directory, pattern));
        foreach (var child in Directory.EnumerateDirectories(directory).Where(child => !IsSkipped(child)))
        {
            Collect(child, pattern, files);
        }
    }

    /// <summary>Build output and nested checkouts (other branches' code), never part of a scan. Only folders
    /// below the scanned directory are judged: the checkout running the scan is itself a checkout.</summary>
    private static bool IsSkipped(string folder)
        => SkippedFolders.Contains(Path.GetFileName(folder)) || Path.Exists(Path.Combine(folder, ".git"));
}
