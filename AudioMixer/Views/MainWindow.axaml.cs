using System;
using System.Runtime.InteropServices;
using AudioMixer.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace AudioMixer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The native window (HWND) already exists here but hasn't been shown yet.
        // Setting the dark caption now means the very first frame is dark, instead
        // of flashing the default white title bar until OnOpened runs.
        TryEnableDarkTitleBar(repaint: false);

        DataContextChanged += (_, _) => AttachToFaders();
    }

    // ---- Auto-size to the fader count -----------------------------------------
    //
    // Width needed for n faders, matching MainWindow.axaml:
    //   page margins 24+24 + sidebar 248 + gap 24      = 320
    //   each strip 160 + 16 gap                       = 176 per fader
    //   Add/Remove column                             = 96
    // So 5 faders = 1296 and 8 faders = 1824 (the window's MaxWidth).
    private const double FixedWidth = 24 + 24 + 248 + 24;
    private const double StripWidth = 160 + 16;
    private const double AddColumnWidth = 96;

    // Below 4 faders the top bar (name, page switch, status chips) runs out of
    // room, so the window never gets narrower than the 4-fader width.
    private const int MinLayoutFaders = 4;

    private System.Collections.Specialized.INotifyCollectionChanged? _faders;

    private static double WidthFor(int faders) =>
        FixedWidth + StripWidth * Math.Max(faders, MinLayoutFaders) + AddColumnWidth;

    private void AttachToFaders()
    {
        if (_faders != null)
            _faders.CollectionChanged -= OnFadersChanged;

        _faders = (DataContext as MainWindowViewModel)?.Faders;
        if (_faders != null)
            _faders.CollectionChanged += OnFadersChanged;

        FitToFaders();
    }

    private void OnFadersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => FitToFaders();

    /// <summary>
    /// Sizes the window to exactly fit the faders plus the Add/Remove column, and
    /// makes that the minimum so the row can't be squeezed into a scroll.
    /// </summary>
    private void FitToFaders()
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        double target = Math.Min(WidthFor(vm.Faders.Count), MaxWidth);

        // Lower the minimum first when shrinking, raise it after when growing,
        // so Width never fights the old minimum.
        if (target < MinWidth)
            MinWidth = target;

        // Leave a maximized/fullscreen window alone; it re-fits when restored.
        if (WindowState == WindowState.Normal)
        {
            Width = target;
            KeepOnScreen();
        }

        MinWidth = target;
    }

    /// <summary>When the window grows past the screen's right edge, slide it left.</summary>
    private void KeepOnScreen()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen == null)
            return;

        var area = screen.WorkingArea; // physical pixels
        double scale = screen.Scaling;
        int windowRight = Position.X + (int)Math.Ceiling(Bounds.Width * scale);
        // Bounds may still hold the old width; use the target width instead.
        windowRight = Math.Max(windowRight, Position.X + (int)Math.Ceiling(Width * scale));

        int overflow = windowRight - area.Right;
        if (overflow > 0)
            Position = new Avalonia.PixelPoint(Math.Max(area.X, Position.X - overflow), Position.Y);
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Hide to the system tray when minimized. The app keeps running.
        if (change.Property == WindowStateProperty && WindowState == WindowState.Minimized)
            Hide();

        // Coming back from maximized: re-fit to the current fader count.
        if (change.Property == WindowStateProperty && WindowState == WindowState.Normal)
            FitToFaders();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Safety net: Avalonia may reapply its own caption theme during the first
        // render, so enforce dark again once the window is up.
        TryEnableDarkTitleBar(repaint: true);
        DispatcherTimer.RunOnce(() => TryEnableDarkTitleBar(repaint: true), TimeSpan.FromMilliseconds(300));
    }

    // Esc on the Settings page returns to the mixer.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel { IsSettingsOpen: true } vm)
        {
            vm.IsSettingsOpen = false;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    // Force the native Windows title bar / caption into dark mode so it matches
    // the app, regardless of the user's system light/dark setting.
    private void TryEnableDarkTitleBar(bool repaint)
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

        // A visible caption only repaints on a frame change, so nudge it. Not needed
        // before the window is first shown, since it paints with the new setting.
        if (!repaint)
            return;
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
