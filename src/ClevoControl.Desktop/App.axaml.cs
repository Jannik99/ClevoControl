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
    // Per user: on Linux the pipe is a socket in /tmp that all users share.
    private static readonly string PipeName = "ClevoControl.Desktop.Show." + Environment.UserName;

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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Inter (last entry of the font stack in App.axaml) would render "N9x0" as "N9×0" with contextual alternates.
        Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.Is<TopLevel>(x))
        {
            Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.FontFeaturesProperty, new Avalonia.Media.FontFeatureCollection { Avalonia.Media.FontFeature.Parse("-calt") }) },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var screenshotFolder = ArgValue("--screenshots");
        // On Unix a mutex without "Global\" only covers one login session (setsid): XDG autostart, the app
        // launcher and a terminal can each be another one, so two instances would drive the hardware.
        var mutexName = screenshotFolder is null ? "ClevoControl.Desktop" : "ClevoControl.Desktop.Shots." + (uint)Path.GetFullPath(screenshotFolder).GetHashCode();
        if (!OperatingSystem.IsWindows()) mutexName = @"Global\" + mutexName + "." + Environment.UserName;
        _mutex = new Mutex(true, mutexName, out var first);
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
        if (!simulate) StopBackgroundService();
        var hw = HardwareFactory.Create(simulate);
        Log.Info($"Start ClevoControl (desktop) {typeof(App).Assembly.GetName().Version} – {hw.Description}, keyboard {hw.KeyboardType}");
        _cc = new ControlCenter(hw, store);
        // Keep the autostart on this exe (a new version elsewhere must not keep starting an old one).
        if (!simulate) Task.Run(() => ClevoControl.Core.Services.Autostart.EnsureCurrent(Environment.ProcessPath));
        // Linux layout detection runs gsettings/setxkbmap/localectl (up to seconds): not on the UI thread.
        if (!OperatingSystem.IsWindows()) _ = Task.Run(() => Core.Platform.KeyboardLayout.Current);
        _cc.AutoProfiles.Invoke = a => Dispatcher.UIThread.Post(a);
        _cc.HotkeyHandled += msg => Notify("ClevoControl", msg);
        // Hotkeys switch on/off live (Linux: evdev reader in ControlCenter). Say so when key presses cannot be read.
        _cc.HotkeysChanged += () =>
        {
            if (_cc.Settings.General.SceneHotkeys && _cc.KeyboardInputError is { Length: > 0 } err)
                Notify("ClevoControl", Loc.T("Tastenkürzel funktionieren nicht: ", "Hotkeys do not work: ") + err);
        };
        _cc.StatusChanged += () => Dispatcher.UIThread.Post(BuildTrayMenu);
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
                Loc.T("\n\nDie App läuft weiter. Details stehen in der Logdatei (Info & Support → Logdatei öffnen).", "\n\nThe app keeps running. Details are in the log file (About & support → Open log file)."));
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
            _serviceStopped = true;
        }
    }

    private static bool _serviceStopped;

    /// <summary>Starts the background service again if the app stopped it (after the app has let go of the hardware).</summary>
    private static void RestartBackgroundService()
    {
        if (!_serviceStopped) return;
        _serviceStopped = false;
        Log.Info("Starting the clevocontrol background service again");
        Core.Platform.Shell.Run("systemctl", "--user", "start", "--no-block", "clevocontrol.service");
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
        // Ctrl+C, a closed terminal and SIGQUIT must also hand the fans back (ControlCenter.Dispose).
        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP, PosixSignal.SIGQUIT })
            _signals.Add(PosixSignalRegistration.Create(signal, c => { c.Cancel = true; Dispatcher.UIThread.Post(ExitApp); }));
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

    /// <summary>
    /// False when there is (probably) no system tray to come back from: the tray icon could not be created, or on
    /// Linux no StatusNotifier host runs (e.g. GNOME without the AppIndicator extension). Close-to-tray then
    /// minimises the window instead of hiding it.
    /// </summary>
    public bool TrayAvailable { get; private set; } = true;

    private void CreateTray()
    {
        try
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
        catch (Exception ex)
        {
            Log.Warn("No tray icon: " + ex.Message);
            TrayAvailable = false;
            return;
        }
        if (OperatingSystem.IsLinux())
            _ = Task.Run(() =>
            {
                if (HasStatusNotifierHost() is false)
                {
                    Log.Info("No StatusNotifier host (system tray) found: closing the window minimises it instead");
                    TrayAvailable = false;
                }
            });
    }

    /// <summary>Linux: is a StatusNotifierWatcher on the session bus? null = cannot tell (no dbus-send/busctl).</summary>
    private static bool? HasStatusNotifierHost()
    {
        var reply = Core.Platform.Shell.Run("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus",
            "org.freedesktop.DBus.NameHasOwner", "string:org.kde.StatusNotifierWatcher");
        if (reply is not null) return reply.Contains("boolean true", StringComparison.Ordinal);
        var names = Core.Platform.Shell.Run("busctl", "--user", "--no-pager", "list");
        if (names is not null) return names.Contains("org.kde.StatusNotifierWatcher", StringComparison.Ordinal);
        return null;
    }

    /// <summary>Once: tells the user that closing the window keeps the app running in the tray.</summary>
    public void ShowTrayHint()
    {
        if (_cc is null || _cc.Settings.General.TrayHintShown) return;
        _cc.Settings.General.TrayHintShown = true;
        _cc.Store.SaveSoon();
        Notify("ClevoControl", OperatingSystem.IsWindows()
            ? Loc.T("ClevoControl läuft im Infobereich weiter. Beenden über Rechtsklick → Beenden.", "ClevoControl keeps running in the notification area. To quit, right-click → Exit.")
            : Loc.T("ClevoControl läuft in der Systemleiste weiter. Beenden über Rechtsklick → Beenden.", "ClevoControl keeps running in the system tray. To quit, right-click → Exit."));
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

        menu.Items.Add(Sub(Loc.T("Szenen", "Scenes"), cc.Settings.Scenes.Select(s =>
            Radio(s.Name, string.Equals(cc.ActiveScene, s.Name, StringComparison.OrdinalIgnoreCase), () => cc.ApplyScene(s)))));
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
        // CC_THEME=Light/Dark renders the other theme without changing the system setting.
        RequestedThemeVariant = Environment.GetEnvironmentVariable("CC_THEME") switch
        {
            "Light" => Avalonia.Styling.ThemeVariant.Light,
            "Dark" => Avalonia.Styling.ThemeVariant.Dark,
            _ => RequestedThemeVariant,
        };
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
        RestartBackgroundService();
        try { _mutex?.ReleaseMutex(); } catch { }
    }
}
