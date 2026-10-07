using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniEurope.Installer.Setup;

/// <summary>
/// Closes the product before its files are replaced: ask its windows to close, wait, then end the
/// process. Only instances started from the install folder are touched (a development build running
/// elsewhere is left alone).
/// </summary>
internal static class RunningApplication
{
    private const uint QueryLimitedInformation = 0x1000;
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);

    public static void Close(string installFolder, string mainExecutable, Action<string> log)
    {
        string name = Path.GetFileNameWithoutExtension(mainExecutable);
        string folder = Path.GetFullPath(installFolder).TrimEnd('\\') + "\\";
        foreach (Process process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                string? path = ImagePath(process.Id);
                if (path is null)
                {
                    // Left running: its files may then be replaced only after a restart (3010).
                    log($"{process.ProcessName} (PID {process.Id}): image path unreadable ({Marshal.GetLastWin32Error()}), left running");
                }
                else if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    Stop(process, log);
                }
                else
                {
                    log($"{process.ProcessName} (PID {process.Id}) runs from {path}, outside {folder}: left running");
                }
            }
        }
    }

    private static void Stop(Process process, Action<string> log)
    {
        log($"Closing {process.ProcessName} (PID {process.Id})");
        process.CloseMainWindow();
        if (process.WaitForExit((int)GracePeriod.TotalMilliseconds))
        {
            return;
        }

        log($"{process.ProcessName} (PID {process.Id}) did not close within {GracePeriod.TotalSeconds:0} s: ending it");
        try
        {
            process.Kill();
            process.WaitForExit((int)GracePeriod.TotalMilliseconds);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            log($"Could not end PID {process.Id}: {exception.Message}");
        }
    }

    // QueryFullProcessImageName needs only the limited query right, granted on processes of other
    // sessions and integrity levels where Process.MainModule (which reads the process memory) fails.
    private static string? ImagePath(int processId)
    {
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(32768);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
