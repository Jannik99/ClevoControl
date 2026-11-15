using Avalonia;

namespace ClevoControl.Desktop;

internal static class Program
{
    /// <summary>
    /// Options: --simulate (no hardware), --minimized (start in the tray, used by autostart),
    /// --screenshots &lt;folder&gt; (render every page to PNG and exit; developer/CI tool).
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        App.Args = args;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the Avalonia designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
