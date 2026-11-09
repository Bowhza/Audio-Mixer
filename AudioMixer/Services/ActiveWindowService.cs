using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioMixer.Services;

/// <summary>
/// Resolves the foreground ("active") window and whether it is running
/// fullscreen. Used by the <c>!</c> wildcard so a fader can follow whatever the
/// user is currently focused on, including fullscreen games.
/// </summary>
public static class ActiveWindowService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>Process id that owns the current foreground window, or 0 if none.</summary>
    public static int GetForegroundProcessId()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return 0;
        GetWindowThreadProcessId(hWnd, out uint pid);
        return (int)pid;
    }

    /// <summary>The <see cref="Process"/> that owns the foreground window, or null.</summary>
    public static Process? GetForegroundProcess()
    {
        int pid = GetForegroundProcessId();
        if (pid == 0)
            return null;
        try
        {
            return Process.GetProcessById(pid);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Title text of the foreground window.</summary>
    public static string GetForegroundWindowTitle()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return string.Empty;
        int length = GetWindowTextLength(hWnd);
        var title = new StringBuilder(length + 1);
        GetWindowText(hWnd, title, title.Capacity);
        return title.ToString();
    }

    /// <summary>
    /// True when the foreground window exactly covers the monitor it sits on
    /// (a borderless/exclusive fullscreen app, e.g. a game or video player).
    /// </summary>
    public static bool IsForegroundFullscreen()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return false;

        if (!GetWindowRect(hWnd, out RECT windowRect))
            return false;

        IntPtr monitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        RECT m = info.rcMonitor;
        // Allow a 1px tolerance for off-by-one window metrics.
        return windowRect.Left <= m.Left + 1 &&
               windowRect.Top <= m.Top + 1 &&
               windowRect.Right >= m.Right - 1 &&
               windowRect.Bottom >= m.Bottom - 1;
    }
}
