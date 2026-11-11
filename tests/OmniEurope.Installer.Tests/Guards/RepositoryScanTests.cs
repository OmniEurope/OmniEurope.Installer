using System.Text;
using Shouldly;

namespace OmniEurope.Installer.Tests.Guards;

public sealed class RepositoryScanTests
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("// file");

    [Fact]
    public void Scan_skips_build_output_and_nested_checkouts_wherever_they_live()
    {
        using var temp = new TempFolder();
        temp.WriteFile(Path.Combine("src", "Kept.cs"), Content);
        temp.WriteFile(Path.Combine("src", "Deep", "AlsoKept.cs"), Content);
        temp.WriteFile(Path.Combine("src", "bin", "Debug", "Built.cs"), Content);
        temp.WriteFile(Path.Combine("src", "obj", "Generated.cs"), Content);
        temp.WriteFile(Path.Combine(".worktrees", "a1b2", "src", "Other.cs"), Content);
        // A worktree kept by any tool under any folder name: its root holds a .git file.
        temp.WriteFile(Path.Combine("tool-state", "worktrees", "x9y8", ".git"), Encoding.UTF8.GetBytes("gitdir: elsewhere"));
        temp.WriteFile(Path.Combine("tool-state", "worktrees", "x9y8", "src", "Branch.cs"), Content);
        // A nested clone: its root holds a .git folder.
        temp.WriteFile(Path.Combine("vendor", "clone", ".git", "HEAD"), Encoding.UTF8.GetBytes("ref: refs/heads/main"));
        temp.WriteFile(Path.Combine("vendor", "clone", "Cloned.cs"), Content);

        var files = RepositoryScan.Enumerate(temp.Path, "*.cs")
            .Select(file => Path.GetRelativePath(temp.Path, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        files.ShouldBe(["src/Deep/AlsoKept.cs", "src/Kept.cs"]);
    }

    [Fact]
    public void Scan_of_a_checkout_root_keeps_its_own_files()
    {
        using var temp = new TempFolder();
        temp.WriteFile(Path.Combine(".git", "HEAD"), Encoding.UTF8.GetBytes("ref: refs/heads/main"));
        temp.WriteFile(Path.Combine("src", "Kept.cs"), Content);

        RepositoryScan.Enumerate(temp.Path, "*.cs").Count.ShouldBe(1);
    }

    [Fact]
    public void Empty_scan_fails_instead_of_passing_vacuously()
    {
        using var temp = new TempFolder();
        temp.WriteFile(Path.Combine("bin", "Only.cs"), Content);

        Should.Throw<InvalidOperationException>(() => RepositoryScan.Enumerate(temp.Path, "*.cs"));
        RepositoryScan.EnumerateOptional(temp.Combine("missing"), "*.cs").ShouldBeEmpty();
    }
}
