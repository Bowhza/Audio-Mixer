using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using AudioMixer.ViewModels;
using AudioMixer.Views;

namespace AudioMixer;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validation messages from Avalonia + CommunityToolkit.
            BindingPlugins.DataValidators.RemoveAt(0);

            _viewModel = new MainWindowViewModel();
            _window = new MainWindow { DataContext = _viewModel };

            // Behave like a tray utility: the window hides instead of quitting, so
            // volume control keeps running in the background.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = _window;

            _window.Closing += (_, e) =>
            {
                e.Cancel = true;
                _window.Hide();
            };

            desktop.Exit += (_, _) => _viewModel?.Dispose();

            SetupTrayIcon(desktop);

            // A second launch of the app asks this one to show its window.
            Services.SingleInstance.ListenForShowRequests(
                () => Avalonia.Threading.Dispatcher.UIThread.Post(ShowWindow));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var menu = new NativeMenu();

        var openItem = new NativeMenuItem("Open");
        openItem.Click += (_, _) => ShowWindow();
        menu.Add(openItem);

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _trayIcon!.IsVisible = false;
            desktop.Shutdown();
        };
        menu.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "AudioMixer",
            IsVisible = true,
            Menu = menu,
        };

        try
        {
            _trayIcon.Icon = new WindowIcon(
                AssetLoader.Open(new Uri("avares://AudioMixer/Assets/Mixer-Icon.ico")));
        }
        catch
        {
            // Icon is optional, and the tray entry still works without it.
        }

        _trayIcon.Clicked += (_, _) => ShowWindow();

        var icons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, icons);
    }

    private void ShowWindow()
    {
        if (_window == null)
            return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
}
