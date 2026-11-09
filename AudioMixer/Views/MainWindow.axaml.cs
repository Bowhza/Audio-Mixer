using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AudioMixer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TryEnableDarkTitleBar();
        // The native handle may not exist yet at OnOpened on some versions, and
        // Avalonia sets its own caption theme during first render, so re-apply after.
        DispatcherTimer.RunOnce(TryEnableDarkTitleBar, TimeSpan.FromMilliseconds(300));
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Hide to the system tray when minimized. The app keeps running.
        if (change.Property == WindowStateProperty && WindowState == WindowState.Minimized)
            Hide();
    }

    // Force the native Windows title bar / caption into dark mode so it matches
    // the app, regardless of the user's system light/dark setting.
    private void TryEnableDarkTitleBar()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = TryGetPlatformHandle()?.Handle;
        if (handle is null || handle == IntPtr.Zero)
            return;

        IntPtr hwnd = handle.Value;
        int useDark = 1;
        // Attribute 20 on Windows 10 2004+, 19 on older builds. Set both to be safe.
        DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 19, ref useDark, sizeof(int));

        // The caption only repaints on a frame change, so nudge it.
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED;
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, flags);
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
