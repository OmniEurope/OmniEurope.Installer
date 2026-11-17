using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// The setup's work folder: readable and writable only by the current user, SYSTEM (the Windows Installer
/// service) and Administrators, with no rule inherited from the folder above. Nobody else can put a file in it,
/// replace the extracted MSI or plant a link in its place.
/// </summary>
internal static class PrivateFolder
{
    /// <summary>The fixed folder, under the temporary root, that groups the setup's work folders.</summary>
    public const string GroupName = "OmniEurope.Installer";

    /// <summary>
    /// Creates a new private folder under <paramref name="tempRoot"/>\<see cref="GroupName"/>. When that group folder
    /// is not trusted (owned by another account, or a link), the new folder goes directly under
    /// <paramref name="tempRoot"/> instead.
    /// </summary>
    /// <exception cref="IOException">The new folder already existed or does not belong to the current user.</exception>
    public static string Create(string tempRoot)
    {
        string group = Path.Combine(tempRoot, GroupName);
        if (!Directory.Exists(group))
        {
            // A concurrent creation returns the existing folder: it is checked below like any other.
            new DirectoryInfo(group).Create(Security());
        }

        string folder = IsTrusted(group)
            ? Path.Combine(group, Guid.NewGuid().ToString("N"))
            : Path.Combine(tempRoot, $"{GroupName}-{Guid.NewGuid():N}");
        new DirectoryInfo(folder).Create(Security());
        if (!IsTrusted(folder) || Directory.EnumerateFileSystemEntries(folder).Any())
        {
            throw new IOException($"The setup work folder {folder} was prepared by someone else.");
        }

        return folder;
    }

    /// <summary>Full control for the current user, SYSTEM and Administrators only; nothing inherited.</summary>
    public static DirectorySecurity Security()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (SecurityIdentifier account in AllowedAccounts())
        {
            security.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }

    /// <summary>The accounts a work folder grants access to, and may be owned by.</summary>
    public static SecurityIdentifier[] AllowedAccounts()
    {
        using WindowsIdentity current = WindowsIdentity.GetCurrent();
        return
        [
            current.User!,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
        ];
    }

    /// <summary>
    /// Whether <paramref name="folder"/> is a real folder owned by an allowed account. An elevated process owns what it
    /// creates through the Administrators group, hence that group counts as an owner.
    /// </summary>
    public static bool IsTrusted(string folder)
    {
        var info = new DirectoryInfo(folder);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        IdentityReference? owner = info.GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        return owner is SecurityIdentifier sid && IsAllowedOwner(sid);
    }

    /// <summary>Whether <paramref name="owner"/> is one of the <see cref="AllowedAccounts"/>.</summary>
    public static bool IsAllowedOwner(SecurityIdentifier owner) => AllowedAccounts().Contains(owner);
}
