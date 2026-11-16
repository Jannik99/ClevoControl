using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ClevoControl.App.Pages;
using ClevoControl.Core;

namespace ClevoControl.App;

public partial class MainWindow : Window
{
    private readonly ControlCenter _cc;
    private readonly Dictionary<string, Func<AppPage>> _pages;
    private AppPage? _current;
    private string? _currentKey;
    /// <summary>The page was left when hiding to the tray (timers and events stopped): rebuild it on the next activation.</summary>
    private bool _pageLeft;

    public MainWindow(ControlCenter cc)
    {
        _cc = cc;
        InitializeComponent();
        VersionText.Text = "Version " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3) + " · " + Loc.T("KI-generiert, inoffiziell", "AI-generated, unofficial");

        _pages = new()
        {
            ["dashboard"] = () => new DashboardPage(cc),
            ["monitor"] = () => new MonitorPage(cc),
            ["keys"] = () => new KeysPage(cc),
            ["automation"] = () => new AutomationPage(cc),
            ["system"] = () => new SystemPage(cc),
            ["lighting"] = () => new LightingPage(cc),
            ["games"] = () => new GamesPage(cc),
            ["fans"] = () => new FansPage(cc),
            ["power"] = () => new PowerPage(cc),
            ["battery"] = () => new BatteryPage(cc),
            ["scenes"] = () => new ScenesPage(cc),
            ["settings"] = () => new SettingsPage(cc),
        };

        AddNav("dashboard", Icons.Home, Loc.T("Startseite", "Home"), true);
        AddNav("lighting", Icons.Color, Loc.T("Beleuchtung", "Lighting"));
        AddNav("scenes", Icons.Star, Loc.T("Szenen", "Scenes"));
        AddNav("automation", Icons.Sparkle, Loc.T("Automatik", "Automation"));
        AddNav("power", Icons.Lightning, Loc.T("Leistung", "Performance"));
        AddNav("fans", Icons.Gauge, Loc.T("Lüfter", "Fans"));
        AddNav("battery", Icons.Battery, Loc.T("Akku", "Battery"));
        AddNav("games", Icons.Game, Loc.T("Spiele & Integrationen", "Games & integrations"));
        AddNav("keys", Icons.Keyboard, Loc.T("Tastenbelegung", "Key mapping"));
        AddNav("monitor", Icons.Diagnostic, Loc.T("Sensoren", "Sensors"));
        AddSeparator();
        AddNav("system", Icons.Info, Loc.T("Info & Support", "About & support"));
        AddNav("settings", Icons.Settings, Loc.T("Einstellungen", "Settings"));

        var hw = cc.Hardware;
        ModelText.Text = DeviceModel() ?? "Clevo";
        StatusText.Text = hw.IsReal
            ? Loc.T("Tastatur: ", "Keyboard: ") + KeyboardTypeText(hw.KeyboardType)
            : Loc.T("Demo-Modus – kein Clevo-Treiber", "Demo mode – no Clevo driver");

        cc.Lighting.ColorChanged += c => Dispatcher.BeginInvoke(() =>
            SetKeyboardDot(cc.Lighting.KeyboardIsOn ? c : new RgbColor(40, 40, 40)));
        SetKeyboardDot(cc.Settings.Keyboard.Color);
        Navigate("dashboard");
    }

    /// <summary>Keyboard color in the header; the icon is black or white, whichever is readable on it.</summary>
    private void SetKeyboardDot(RgbColor c)
    {
        KeyboardDot.Background = c.ToBrush();
        KeyboardIcon.Foreground = Ui.ContrastText(c);
    }

    private static string? DeviceModel()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (var o in s.Get()) return (o["Model"] as string)?.Trim();
        }
        catch { }
        return null;
    }

    public static string KeyboardTypeText(KeyboardType t) => t switch
    {
        KeyboardType.SingleZoneRgb => Loc.T("RGB, 1 Zone", "RGB, 1 zone"),
        KeyboardType.ThreeZoneRgb => Loc.T("RGB, 3 Zonen", "RGB, 3 zones"),
        KeyboardType.PerKeyRgb => Loc.T("RGB pro Taste", "per-key RGB"),
        KeyboardType.WhiteOnly => Loc.T("nur weiß", "white only"),
        KeyboardType.None => Loc.T("keine Beleuchtung", "no backlight"),
        _ => Loc.T("unbekannt", "unknown"),
    };

    private void AddSeparator()
    {
        var line = new Border { Height = 1, Margin = new Thickness(16, 8, 16, 8) };
        line.SetResourceReference(Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        NavPanel.Children.Add(line);
    }

    private void AddNav(string key, string icon, string title, bool isChecked = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = Ui.Icon(icon, 16);
        ic.Margin = new Thickness(0, 0, 14, 0);
        sp.Children.Add(ic);
        sp.Children.Add(Ui.Text(title, 14));
        var rb = new RadioButton { Style = (Style)FindResource("NavRadio"), Content = sp, GroupName = "nav", IsChecked = isChecked, Tag = key };
        // Navigate() checks the radio itself; only navigate when the user clicked a different page.
        rb.Checked += (_, _) => { if (key != _currentKey) Navigate(key); };
        NavPanel.Children.Add(rb);
    }

    /// <summary>
    /// Shows a page. Navigating to the page that is already shown rebuilds it (pages use this to refresh)
    /// and keeps the scroll position; any other page starts at the top.
    /// </summary>
    public void Navigate(string key)
    {
        var same = key == _currentKey;
        var offset = ContentScroll.VerticalOffset;
        _current?.OnLeave();
        _current = _pages[key]();
        _currentKey = key;
        PageHost.Content = _current.Build();
        ContentScroll.ScrollToTop();
        if (same && offset > 0)
            // The new content has no size yet: restore once it has been laid out.
            Dispatcher.BeginInvoke(() => ContentScroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        foreach (var rb in NavPanel.Children.OfType<RadioButton>())
            if ((string)rb.Tag == key && rb.IsChecked != true) rb.IsChecked = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting && _cc.Settings.General.CloseToTray)
        {
            e.Cancel = true;
            _current?.OnLeave();
            _pageLeft = true;
            Hide();
            (Application.Current as App)?.ShowTrayHint();
            return;
        }
        _current?.OnLeave();
        base.OnClosing(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        // The user may have switched the keyboard layout meanwhile (cheap to detect on Windows).
        Core.Platform.KeyboardLayout.Refresh();
        if (_pageLeft && _currentKey is not null) { _pageLeft = false; Navigate(_currentKey); }
        else _current?.OnShow();
    }
}

/// <summary>Base class for code-built pages.</summary>
public abstract class AppPage
{
    protected AppPage(ControlCenter cc) => Cc = cc;

    protected ControlCenter Cc { get; }

    public abstract UIElement Build();

    /// <summary>Called when the page is left (stop timers).</summary>
    public virtual void OnLeave() { }

    public virtual void OnShow() { }
}
