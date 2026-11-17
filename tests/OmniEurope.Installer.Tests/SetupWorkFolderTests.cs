using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using OmniEurope.Installer.Msi;
using OmniEurope.Installer.Setup;
using Shouldly;

namespace OmniEurope.Installer.Tests;

/// <summary>The setup host's private work folder and locked extracted files (PrivateFolder, LockedFile).</summary>
public sealed class SetupWorkFolderTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void The_work_folder_grants_only_the_user_system_and_administrators_and_inherits_nothing()
    {
        string folder = PrivateFolder.Create(_temp.Path);

        Path.GetDirectoryName(folder).ShouldBe(_temp.Combine(PrivateFolder.GroupName));
        foreach (string created in new[] { folder, Path.GetDirectoryName(folder)! })
        {
            DirectorySecurity security = new DirectoryInfo(created).GetAccessControl();
            security.AreAccessRulesProtected.ShouldBeTrue();
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().ToList();
            rules.ShouldAllBe(rule => !rule.IsInherited && rule.AccessControlType == AccessControlType.Allow);
            rules.Select(rule => rule.IdentityReference).Distinct().OrderBy(sid => sid.Value)
                .ShouldBe(PrivateFolder.AllowedAccounts().OrderBy(sid => sid.Value));
            PrivateFolder.IsTrusted(created).ShouldBeTrue();
        }

        Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty();
        PrivateFolder.Create(_temp.Path).ShouldNotBe(folder);
    }

    [Fact]
    public void A_group_folder_planted_as_a_link_is_bypassed_for_a_folder_directly_under_the_root()
    {
        string elsewhere = _temp.Combine("elsewhere");
        Directory.CreateDirectory(elsewhere);
        string group = _temp.Combine(PrivateFolder.GroupName);
        // A junction needs no privilege: any account able to write the root can plant one.
        ProcessRunner.Run("cmd.exe", "/c", "mklink", "/J", group, elsewhere).ExitCode.ShouldBe(0);

        string folder = PrivateFolder.Create(_temp.Path);

        PrivateFolder.IsTrusted(group).ShouldBeFalse();
        Path.GetDirectoryName(folder).ShouldBe(_temp.Path);
        Path.GetFileName(folder).ShouldStartWith(PrivateFolder.GroupName + "-");
        Directory.EnumerateFileSystemEntries(elsewhere).ShouldBeEmpty();
        Directory.Delete(group);
    }

    [Fact]
    public void Only_the_user_system_and_administrators_are_trusted_owners()
    {
        using WindowsIdentity current = WindowsIdentity.GetCurrent();

        PrivateFolder.IsAllowedOwner(current.User!).ShouldBeTrue();
        PrivateFolder.IsAllowedOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)).ShouldBeTrue();
        PrivateFolder.IsAllowedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)).ShouldBeTrue();
        PrivateFolder.IsAllowedOwner(new SecurityIdentifier(WellKnownSidType.WorldSid, null)).ShouldBeFalse();
        PrivateFolder.IsAllowedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null)).ShouldBeFalse();
        PrivateFolder.IsAllowedOwner(new SecurityIdentifier("S-1-5-21-1-2-3-1001")).ShouldBeFalse();
    }

    [Fact]
    public void A_locked_package_stays_readable_by_windows_installer_but_cannot_be_written_replaced_or_deleted()
    {
        string folder = PrivateFolder.Create(_temp.Path);
        string msi = Path.Combine(folder, "App-1.0.0.msi");
        using (MsiDatabase database = MsiDatabase.Create(msi))
        {
            database.Execute("CREATE TABLE `Sample` (`Name` CHAR(72) NOT NULL PRIMARY KEY `Name`)");
            database.Commit();
        }

        byte[] content = File.ReadAllBytes(msi);
        using (LockedFile locked = LockedFile.Lock(msi, SHA256.HashData(content)))
        {
            locked.Path.ShouldBe(msi);
            Should.Throw<IOException>(() => new FileStream(msi, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
            Should.Throw<IOException>(() => File.Delete(msi));
            Should.Throw<IOException>(() => File.Move(msi, msi + ".old"));
            Should.Throw<IOException>(() => File.Copy(_temp.WriteFile("evil.msi", [1, 2, 3]), msi, overwrite: true));
            File.ReadAllBytes(msi).ShouldBe(content);
            using (MsiDatabase.OpenReadOnly(msi))
            {
                // msi.dll opens the package while the setup holds it.
            }

            locked.Verify();
        }

        File.Delete(msi);
        File.Exists(msi).ShouldBeFalse();
    }

    [Fact]
    public void A_file_that_differs_from_the_carried_hash_is_refused_and_left_unlocked()
    {
        byte[] original = [1, 2, 3, 4, 5];
        string copy = _temp.WriteFile("App.msi", original);
        File.WriteAllBytes(copy, [1, 2, 3, 4, 6]);

        Should.Throw<InvalidDataException>(() => LockedFile.Lock(copy, SHA256.HashData(original))).Message.ShouldContain("does not match");

        File.Delete(copy);
    }

    [Fact]
    public void A_file_locked_without_an_expected_hash_records_its_own_and_verifies_against_it()
    {
        string license = _temp.WriteFile("license.rtf", @"{\rtf1 License}"u8.ToArray());

        using LockedFile locked = LockedFile.Lock(license, null);

        locked.Verify();
        Should.Throw<IOException>(() => File.WriteAllText(license, "changed"));
    }

    [Fact]
    public void Hashes_are_equal_only_when_every_byte_and_the_length_match()
    {
        LockedFile.SameHash([1, 2, 3], [1, 2, 3]).ShouldBeTrue();
        LockedFile.SameHash([1, 2, 3], [1, 2, 4]).ShouldBeFalse();
        LockedFile.SameHash([1, 2, 3], [1, 2]).ShouldBeFalse();
        using var stream = new MemoryStream([9, 8, 7]);
        stream.Position = 2;
        LockedFile.Sha256(stream).ShouldBe(SHA256.HashData(new byte[] { 9, 8, 7 }));
    }
}
