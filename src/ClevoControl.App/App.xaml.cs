using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ClevoControl.Core;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Settings;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace ClevoControl.App;

public partial class App : Application
{
    private const string MutexName = "ClevoControl.SingleInstance";
    private const string ShowEventName = "ClevoControl.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private ControlCenter? _cc;
    private MainWindow? _window;
    private Forms.NotifyIcon? _tray;
    private HwndSource? _hotkeySource;

    public static bool IsExiting { get; private set; }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Screenshot runs (developer tool) may run next to an installed instance.
        var isScreenshotRun = e.Args.Contains("--screenshots");
        _mutex = new Mutex(true, isScreenshotRun ? MutexName + ".Screenshots" : MutexName, out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, isScreenshotRun ? ShowEventName + ".Screenshots" : ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowWindow);
        }) { IsBackground = true }.Start();

        var store = new SettingsStore();
        Loc.Init(store.Current.General.Language);
        var simulate = e.Args.Contains("--simulate");
        var hw = HardwareFactory.Create(simulate);
        Log.Info($"Start ClevoControl {typeof(App).Assembly.GetName().Version} – {hw.Description}, keyboard {hw.KeyboardType}");
        _cc = new ControlCenter(hw, store);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception (app terminates)", args.ExceptionObject as Exception);
            _cc?.Fans.SafeRestoreAuto();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("UI error", args.Exception);
            MessageBox.Show(Loc.T("Es ist ein Fehler aufgetreten:\n\n", "An error occurred:\n\n") + args.Exception.Message +
                Loc.T("\n\nDie App läuft weiter. Details stehen in der Logdatei (System → Logdatei öffnen).",
                      "\n\nThe app keeps running. Details are in the log file (System → Open log file)."),
                "ClevoControl", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _window = new MainWindow(_cc);
        _cc.Overheat += msg => Dispatcher.BeginInvoke(() => _tray?.ShowBalloonTip(5000, Loc.T("ClevoControl: zu heiß", "ClevoControl: too hot"),
            msg + Loc.T(". Tipp: Lüfter auf Maximum oder „Prozessor-Drosselung“ unter „Leistung“.", ". Tip: fans to maximum or “CPU throttling” under “Performance”."), Forms.ToolTipIcon.Warning));
        CreateTray();
        RegisterHotkeys();

        var shotIndex = Array.IndexOf(e.Args, "--screenshots");
        if (shotIndex >= 0 && shotIndex + 1 < e.Args.Length)
        {
            _window.Show();
            _ = TakeScreenshots(e.Args[shotIndex + 1]);
            return;
        }

        var minimized = e.Args.Contains("--minimized") && store.Current.General.StartMinimized;
        if (!minimized) _window.Show();

        if (!hw.IsReal && !simulate)
        {
            _window.Show();
            MessageBox.Show(_window,
                Loc.T("ClevoControl hat keinen Clevo-Treiber gefunden und läuft im Demo-Modus: Du kannst alles ausprobieren, an der Hardware ändert sich aber nichts.\n\nInstalliere das Clevo Control Center (oder den Treiber deines Laptop-Herstellers), damit die App funktioniert. Technische Details stehen unter „Info & Support“.",
                      "ClevoControl found no Clevo driver and runs in demo mode: you can try everything, but nothing changes on the hardware.\n\nInstall Clevo Control Center (or your laptop vendor's driver) for the app to work. Technical details are under “About & support”."),
                "ClevoControl", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Developer helper: renders every page to PNG (used for README images and UI checks).</summary>
    private async Task TakeScreenshots(string folder)
    {
        // Renders the app window page by page into PNG files (app content only).
        System.IO.Directory.CreateDirectory(folder);
        _window!.ShowActivated = false;
        _window.Width = 1120;
        _window.Height = Math.Min(SystemParameters.WorkArea.Height - 20, 1000);
        _window.Left = 10;
        _window.Top = 10;
        var scroll = (System.Windows.Controls.ScrollViewer)_window.FindName("ContentScroll");
        foreach (var key in (Environment.GetEnvironmentVariable("CC_SHOTS") ?? "dashboard,lighting,scenes,power,fans,battery,games,keys,monitor,system,settings").Split(','))
        {
            _window.Navigate(key);
            await Task.Delay(2200);
            Capture(System.IO.Path.Combine(folder, key + ".png"));
            if (scroll.ScrollableHeight > 50)
            {
                scroll.ScrollToVerticalOffset(scroll.ViewportHeight - 60);
                await Task.Delay(500);
                Capture(System.IO.Path.Combine(folder, key + "-2.png"));
            }
        }
        ExitApp();

        void Capture(string path)
        {
            // Renders only the app's own visual tree (never the screen), on an opaque Windows 11 base
            // background because the Mica backdrop is composed by DWM and not part of the visual tree.
            var content = (FrameworkElement)_window.Content;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(content);
            var w = content.ActualWidth;
            var h = content.ActualHeight;
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle((System.Windows.Media.Brush)FindResource("SolidBackgroundFillColorBaseBrush"), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(content), null, new Rect(0, 0, w, h));
            }
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w * dpi.DpiScaleX), (int)(h * dpi.DpiScaleY),
                96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
            bmp.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(path);
            enc.Save(fs);
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend: _cc?.OnSuspend(); break;
            case PowerModes.Resume: _cc?.OnResume(); break;
            case PowerModes.StatusChange:
                try { _cc?.Power.CheckPowerSource(_cc.Settings.Power); } catch { }
                break;
        }
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void CreateTray()
    {
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = iconStream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(iconStream),
            Text = "ClevoControl",
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowWindow(); };
        _tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildTrayMenu();
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        if (_tray?.ContextMenuStrip is not { } menu || _cc is null) return;
        menu.Items.Clear();
        menu.Items.Add(Loc.T("Öffnen", "Open"), null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var kb = _cc.Settings.Keyboard;
        var light = new Forms.ToolStripMenuItem(Loc.T("Tastaturlicht", "Keyboard light")) { Checked = kb.Enabled };
        light.Click += (_, _) => Dispatcher.Invoke(ToggleKeyboard);
        menu.Items.Add(light);

        var scenes = new Forms.ToolStripMenuItem(Loc.T("Szenen", "Scenes"));
        foreach (var s in _cc.Settings.Scenes)
        {
            var scene = s;
            scenes.DropDownItems.Add(scene.Name, null, (_, _) => Dispatcher.Invoke(() => _cc.ApplyScene(scene)));
        }
        menu.Items.Add(scenes);

        var profiles = new Forms.ToolStripMenuItem(Loc.T("Leistungsprofil", "Performance profile"));
        foreach (var p in Pages.PowerPage.ProfileOrder)
        {
            var prof = p;
            var item = new Forms.ToolStripMenuItem(Pages.PowerPage.ProfileName(prof)) { Checked = _cc.Settings.Power.Profile == prof };
            item.Click += (_, _) => Dispatcher.Invoke(() => { _cc.Settings.Power.Profile = prof; _cc.ApplyPower(); });
            profiles.DropDownItems.Add(item);
        }
        menu.Items.Add(profiles);

        var fans = new Forms.ToolStripMenuItem(Loc.T("Lüfter", "Fans"));
        foreach (var m in Enum.GetValues<FanMode>())
        {
            var mode = m;
            var item = new Forms.ToolStripMenuItem(Pages.FansPage.ModeName(mode)) { Checked = _cc.Settings.Fan.Mode == mode };
            item.Click += (_, _) => Dispatcher.Invoke(() => { _cc.Settings.Fan.Mode = mode; _cc.ApplyFans(); });
            fans.DropDownItems.Add(item);
        }
        menu.Items.Add(fans);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Beenden", "Exit"), null, (_, _) => Dispatcher.Invoke(ExitApp));
    }

    public void ToggleKeyboard()
    {
        if (_cc is null) return;
        _cc.Settings.Keyboard.Enabled = !_cc.Settings.Keyboard.Enabled;
        _cc.ApplyKeyboard();
    }

    // Ctrl+Alt+Shift+1..9 = scenes, Ctrl+Alt+Shift+0 = keyboard light on/off.
    // Plain Ctrl+Alt is NOT used: on German and many other layouts AltGr == Ctrl+Alt (², ³, {, [, ], }).
    private void RegisterHotkeys()
    {
        if (_cc is null || !_cc.Settings.General.SceneHotkeys) return;
        var p = new HwndSourceParameters("ClevoControlHotkeys") { Width = 0, Height = 0, ParentWindow = new IntPtr(-3) };
        _hotkeySource = new HwndSource(p);
        _hotkeySource.AddHook(HotkeyHook);
        const uint ctrlAlt = 0x0002 | 0x0001 | 0x0004 | 0x4000; // MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT
        for (var i = 0; i <= 9; i++) RegisterHotKey(_hotkeySource.Handle, 100 + i, ctrlAlt, (uint)(0x30 + i));
        RegisterHotKey(_hotkeySource.Handle, 120, ctrlAlt, 0x46); // Ctrl+Alt+Shift+F = fans full on/off
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg != WM_HOTKEY || _cc is null) return IntPtr.Zero;
        var idx = wParam.ToInt32() - 100;
        if (idx == 20)
        {
            _cc.ToggleFanBoost();
            _tray?.ShowBalloonTip(1500, "ClevoControl", Loc.T("Lüfter: ", "Fans: ") + Pages.FansPage.ModeName(_cc.Settings.Fan.Mode), Forms.ToolTipIcon.None);
        }
        else if (idx == 0) ToggleKeyboard();
        else if (idx >= 1 && idx <= _cc.Settings.Scenes.Count)
        {
            var scene = _cc.Settings.Scenes[idx - 1];
            _cc.ApplyScene(scene);
            _tray?.ShowBalloonTip(1500, "ClevoControl", $"{scene.Icon} {scene.Name}", Forms.ToolTipIcon.None);
        }
        handled = true;
        return IntPtr.Zero;
    }

    public void ExitApp()
    {
        IsExiting = true;
        if (_hotkeySource is not null)
        {
            for (var i = 0; i <= 9; i++) UnregisterHotKey(_hotkeySource.Handle, 100 + i);
            UnregisterHotKey(_hotkeySource.Handle, 120);
            _hotkeySource.Dispose();
        }
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _window?.Close();
        _cc?.Dispose();
        _mutex?.ReleaseMutex();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _cc?.Dispose();
        base.OnSessionEnding(e);
    }
}
