using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioMixer.Services;

/// <summary>Win32 helpers for resolving a process's executable path across bitness.</summary>
public static class ProcessHelper
{
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr handle, int flags, StringBuilder exeName, ref int size);

    /// <summary>
    /// Full path to the executable backing <paramref name="processId"/>, or null.
    /// Uses PROCESS_QUERY_LIMITED_INFORMATION so it works without elevation for
    /// most user processes (unlike Process.MainModule, which breaks across bitness).
    /// </summary>
    public static string? GetExecutablePath(int processId)
    {
        if (processId <= 0)
            return null;

        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            int capacity = 1024;
            var buffer = new StringBuilder(capacity);
            return QueryFullProcessImageName(handle, 0, buffer, ref capacity)
                ? buffer.ToString()
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
