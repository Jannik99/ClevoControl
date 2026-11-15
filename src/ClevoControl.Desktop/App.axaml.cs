using System.IO.Pipes;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ClevoControl.Core;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Settings;

namespace ClevoControl.Desktop;

public partial class App : Application
{
    private const string PipeName = "ClevoControl.Desktop.Show";

    public static string[] Args { get; set; } = [];
    public static bool IsExiting { get; private set; }
    public static new App Current => (App)Application.Current!;

    private Mutex? _mutex;
    private ControlCenter? _cc;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private readonly List<PosixSignalRegistration> _signals = [];
    private CancellationTokenSource? _pipeCts;

    public ControlCenter? Cc => _cc;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var screenshotFolder = ArgValue("--screenshots");
        _mutex = new Mutex(true, screenshotFolder is null ? "ClevoControl.Desktop" : "ClevoControl.Desktop.Shots." + (uint)Path.GetFullPath(screenshotFolder).GetHashCode(), out var first);
        if (!first)
        {
            // Another instance runs: ask it to show its window, then quit.
            try
            {
                using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                c.Connect(1000);
            }
            catch { }
            desktop.Shutdown();
            return;
        }
        if (screenshotFolder is null) ListenForShowRequests();

        var store = new SettingsStore();
        Loc.Init(store.Current.General.Language);
        var simulate = Args.Contains("--simulate") || screenshotFolder is not null;
        StopBackgroundService();
        var hw = HardwareFactory.Create(simulate);
        Log.Info($"Start ClevoControl (desktop) {typeof(App).Assembly.GetName().Version} – {hw.Description}, keyboard {hw.KeyboardType}");
        _cc = new ControlCenter(hw, store);
        _cc.AutoProfiles.Invoke = a => Dispatcher.UIThread.Post(a);
        _cc.HotkeyHandled += msg => Notify("ClevoControl", msg);
        _cc.Overheat += msg => Notify(Loc.T("ClevoControl: zu heiß", "ClevoControl: too hot"),
            msg + Loc.T(". Tipp: Lüfter auf Maximum oder „Prozessor-Drosselung“ unter „Leistung“.", ". Tip: fans to maximum or “CPU throttling” under “Performance”."));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception (app terminates)", e.ExceptionObject as Exception);
            _cc?.Fans.SafeRestoreAuto();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error("UI error", e.Exception);
            e.Handled = true;
            _ = Ui.MessageAsync(_window, Loc.T("Es ist ein Fehler aufgetreten:\n\n", "An error occurred:\n\n") + e.Exception.Message +
                Loc.T("\n\nDie App läuft weiter. Details stehen in der Logdatei.", "\n\nThe app keeps running. Details are in the log file."));
        };
        RegisterSignals();

        _window = new MainWindow(_cc);
        desktop.MainWindow = null; // the window is shown/hidden by us (tray app)
        desktop.Exit += (_, _) => Cleanup();
        CreateTray();

        if (screenshotFolder is not null)
        {
            _window.Show();
            _ = TakeScreenshots(screenshotFolder);
        }
        else
        {
            var minimized = Args.Contains("--minimized") && store.Current.General.StartMinimized;
            if (!minimized) _window.Show();
            if (!hw.IsReal && !simulate)
            {
                _window.Show();
                _ = Ui.MessageAsync(_window, Loc.T(
                    "ClevoControl hat keinen Clevo-Treiber gefunden und läuft im Demo-Modus: Du kannst alles ausprobieren, an der Hardware ändert sich aber nichts.\n\nGrund: ",
                    "ClevoControl found no Clevo driver and runs in demo mode: you can try everything, but nothing changes on the hardware.\n\nReason: ")
                    + hw.Description + Loc.T("\n\nAnleitung: docs/LINUX.md", "\n\nSetup guide: docs/LINUX.md"));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static string? ArgValue(string name)
    {
        var i = Array.IndexOf(Args, name);
        return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
    }

    /// <summary>The systemd user service (clevoctl daemon) would fight with the app over the hardware.</summary>
    private static void StopBackgroundService()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Core.Platform.Shell.Run("systemctl", "--user", "is-active", "clevocontrol.service")?.Trim() == "active")
        {
            Log.Info("Stopping the clevocontrol background service while the app runs");
            Core.Platform.Shell.Run("systemctl", "--user", "stop", "clevocontrol.service");
        }
    }

    private void ListenForShowRequests()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    await server.WaitForConnectionAsync(token);
                    Dispatcher.UIThread.Post(ShowWindow);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(1000, token).ContinueWith(_ => { }); }
            }
        });
    }

    /// <summary>Linux: contrib/linux/clevocontrol-sleep sends SIGUSR1 before suspend and SIGUSR2 after resume.</summary>
    private void RegisterSignals()
    {
        if (!OperatingSystem.IsLinux()) return;
        const int sigUsr1 = 10, sigUsr2 = 12;
        _signals.Add(PosixSignalRegistration.Create((PosixSignal)sigUsr1, c => { c.Cancel = true; _cc?.OnSuspend(); }));
        _signals.Add(PosixSignalRegistration.Create((PosixSignal)sigUsr2, c => { c.Cancel = true; _cc?.OnResume(); }));
        _signals.Add(PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; Dispatcher.UIThread.Post(ExitApp); }));
    }

    /// <summary>Desktop notification (Linux: notify-send). Quietly does nothing where unavailable.</summary>
    public static void Notify(string title, string message)
    {
        if (OperatingSystem.IsLinux())
            _ = Task.Run(() => Core.Platform.Shell.Run("notify-send", "-a", "ClevoControl", "-i", "clevocontrol", title, message));
    }

    public void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void CreateTray()
    {
        using var icon = AssetLoader.Open(new Uri("avares://clevocontrol/Assets/icon-256.png"));
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(icon),
            ToolTipText = "ClevoControl",
            Menu = new NativeMenu(),
        };
        _tray.Clicked += (_, _) => ShowWindow();
        _tray.Menu.NeedsUpdate += (_, _) => BuildTrayMenu();
        BuildTrayMenu();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void BuildTrayMenu()
    {
        if (_tray?.Menu is not { } menu || _cc is null) return;
        var cc = _cc;
        menu.Items.Clear();
        menu.Items.Add(Item(Loc.T("Öffnen", "Open"), ShowWindow));
        menu.Items.Add(new NativeMenuItemSeparator());

        var light = Item(Loc.T("Tastaturlicht", "Keyboard light"), ToggleKeyboard);
        light.ToggleType = MenuItemToggleType.CheckBox;
        light.IsChecked = cc.Settings.Keyboard.Enabled;
        menu.Items.Add(light);

        menu.Items.Add(Sub(Loc.T("Szenen", "Scenes"), cc.Settings.Scenes.Select(s => Item(s.Name, () => cc.ApplyScene(s)))));
        menu.Items.Add(Sub(Loc.T("Leistungsprofil", "Performance profile"), Pages.PowerPage.ProfileOrder.Select(p =>
            Radio(Pages.PowerPage.ProfileName(p), cc.Settings.Power.Profile == p, () => { cc.Settings.Power.Profile = p; cc.ApplyPower(); }))));
        menu.Items.Add(Sub(Loc.T("Lüfter", "Fans"), Enum.GetValues<FanMode>().Select(m =>
            Radio(Pages.FansPage.ModeName(m), cc.Settings.Fan.Mode == m, () => { cc.Settings.Fan.Mode = m; cc.ApplyFans(); }))));

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(Item(Loc.T("Beenden", "Exit"), ExitApp));

        static NativeMenuItem Item(string header, Action click)
        {
            var i = new NativeMenuItem(header);
            i.Click += (_, _) => Dispatcher.UIThread.Post(click);
            return i;
        }
        static NativeMenuItem Radio(string header, bool on, Action click)
        {
            var i = Item(header, click);
            i.ToggleType = MenuItemToggleType.Radio;
            i.IsChecked = on;
            return i;
        }
        static NativeMenuItem Sub(string header, IEnumerable<NativeMenuItem> items)
        {
            var m = new NativeMenu();
            foreach (var i in items) m.Items.Add(i);
            return new NativeMenuItem(header) { Menu = m };
        }
    }

    public void ToggleKeyboard()
    {
        if (_cc is null) return;
        _cc.Settings.Keyboard.Enabled = !_cc.Settings.Keyboard.Enabled;
        _cc.ApplyKeyboard();
    }

    /// <summary>Developer helper: renders every page to PNG (README images, UI checks on Linux and Windows).</summary>
    private async Task TakeScreenshots(string folder)
    {
        Directory.CreateDirectory(folder);
        _window!.Width = 1120;
        _window.Height = 900;
        var keys = (Environment.GetEnvironmentVariable("CC_SHOTS") ?? string.Join(',', MainWindow.PageKeys)).Split(',');
        foreach (var key in keys)
        {
            _window.Navigate(key);
            await Task.Delay(2200);
            _window.SaveScreenshot(Path.Combine(folder, key + ".png"));
        }
        ExitApp();
    }

    public void ExitApp()
    {
        IsExiting = true;
        _window?.Close();
        Cleanup();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private bool _cleaned;

    private void Cleanup()
    {
        if (_cleaned) return;
        _cleaned = true;
        _pipeCts?.Cancel();
        foreach (var s in _signals) s.Dispose();
        if (_tray is not null) { _tray.IsVisible = false; _tray.Dispose(); }
        _cc?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
    }
}
