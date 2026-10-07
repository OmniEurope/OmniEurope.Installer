using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Copies the first icon group of one executable into another (kernel32 resource API), so the setup
/// program shows the product's icon in Explorer, the taskbar and its own window.
/// </summary>
internal static unsafe partial class IconResources
{
    private const uint LoadAsDataFile = 0x2;
    private const uint LoadAsImageResource = 0x20;
    private static readonly nint IconType = 3;
    private static readonly nint GroupIconType = 14;
    private const int GroupHeaderLength = 6;
    private const int GroupEntryLength = 14;

    /// <summary>Writes the icon group of <paramref name="sourceExecutable"/> into <paramref name="targetExecutable"/>.</summary>
    /// <exception cref="InvalidOperationException">The source has no icon, or Windows refused the update.</exception>
    public static void Copy(string sourceExecutable, string targetExecutable)
    {
        (byte[] group, List<(ushort Id, byte[] Data)> icons) = ReadFirstGroup(sourceExecutable);
        nint update = BeginUpdateResource(targetExecutable, false);
        if (update == 0)
        {
            throw new InvalidOperationException($"Cannot open {targetExecutable} for resource update (error {Marshal.GetLastPInvokeError()}).");
        }

        bool written = true;
        foreach ((ushort id, byte[] data) in icons)
        {
            written &= UpdateIcon(update, IconType, id, data);
        }

        written &= UpdateIcon(update, GroupIconType, 1, group);
        if (!EndUpdateResource(update, !written) || !written)
        {
            throw new InvalidOperationException($"Windows refused the icon update of {targetExecutable} (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    internal static (byte[] Group, List<(ushort Id, byte[] Data)> Icons) ReadFirstGroup(string executable)
    {
        nint module = LoadLibraryEx(executable, 0, LoadAsDataFile | LoadAsImageResource);
        if (module == 0)
        {
            throw new InvalidOperationException($"Cannot read the resources of {executable} (error {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            nint groupName = FirstGroupName(module, executable);
            byte[]? groupData = ReadResource(module, groupName, GroupIconType);
            if (groupName > 0xFFFF)
            {
                Marshal.FreeHGlobal(groupName);
            }

            byte[] group = groupData ?? throw new InvalidOperationException($"{executable} has an unreadable icon group.");
            int count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
            var icons = new List<(ushort, byte[])>(count);
            for (int index = 0; index < count; index++)
            {
                // Renumber the images 1..n: the group entry's last two bytes hold the image id.
                int entry = GroupHeaderLength + index * GroupEntryLength;
                ushort sourceId = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(entry + 12));
                byte[] image = ReadResource(module, sourceId, IconType)
                    ?? throw new InvalidOperationException($"{executable} lacks icon image {sourceId}.");
                ushort id = (ushort)(index + 1);
                BinaryPrimitives.WriteUInt16LittleEndian(group.AsSpan(entry + 12), id);
                icons.Add((id, image));
            }

            return (group, icons);
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static nint FirstGroupName(nint module, string executable)
    {
        nint found = 0;
        EnumResourceNames(module, GroupIconType, &OnGroupName, (nint)Unsafe.AsPointer(ref found));
        if (found == 0)
        {
            throw new InvalidOperationException($"{executable} has no icon.");
        }

        return found;
    }

    // Keeps the first name. Integer ids are kept as they are; a string name is only valid during the
    // enumeration, so it is copied to unmanaged memory freed by the caller.
    [UnmanagedCallersOnly]
    private static int OnGroupName(nint module, nint type, nint name, nint state)
    {
        *(nint*)state = name <= 0xFFFF ? name : Marshal.StringToHGlobalUni(Marshal.PtrToStringUni(name));
        return 0;
    }

    private static byte[]? ReadResource(nint module, nint name, nint type)
    {
        nint info = FindResource(module, name, type);
        if (info == 0)
        {
            return null;
        }

        uint size = SizeofResource(module, info);
        nint data = LockResource(LoadResource(module, info));
        if (data == 0 || size == 0)
        {
            return null;
        }

        byte[] bytes = new byte[size];
        Marshal.Copy(data, bytes, 0, (int)size);
        return bytes;
    }

    private static bool UpdateIcon(nint update, nint type, ushort id, byte[] data)
    {
        fixed (byte* pointer = data)
        {
            return UpdateResource(update, type, id, 0, pointer, (uint)data.Length);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryEx(string fileName, nint file, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);

    [LibraryImport("kernel32.dll", EntryPoint = "EnumResourceNamesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumResourceNames(nint module, nint type, delegate* unmanaged<nint, nint, nint, nint, int> callback, nint state);

    [LibraryImport("kernel32.dll", EntryPoint = "FindResourceW")]
    private static partial nint FindResource(nint module, nint name, nint type);

    [LibraryImport("kernel32.dll")]
    private static partial nint LoadResource(nint module, nint info);

    [LibraryImport("kernel32.dll")]
    private static partial nint LockResource(nint data);

    [LibraryImport("kernel32.dll")]
    private static partial uint SizeofResource(nint module, nint info);

    [LibraryImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint BeginUpdateResource(string fileName, [MarshalAs(UnmanagedType.Bool)] bool deleteExisting);

    [LibraryImport("kernel32.dll", EntryPoint = "UpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateResource(nint update, nint type, nint name, ushort language, byte* data, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndUpdateResource(nint update, [MarshalAs(UnmanagedType.Bool)] bool discard);
}
