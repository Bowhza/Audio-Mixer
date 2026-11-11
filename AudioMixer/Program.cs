using Avalonia;
using System;

namespace AudioMixer;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Only one copy may own the mixer's COM port. A second launch just brings
        // the running window to the front and exits.
        if (!Services.SingleInstance.TryAcquire())
            return;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Services.SingleInstance.Release();
        }
    }

    // Avalonia configuration, don't remove. Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
