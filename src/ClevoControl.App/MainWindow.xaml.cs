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
            KeyboardDot.Background = (cc.Lighting.KeyboardIsOn ? c : new RgbColor(40, 40, 40)).ToBrush());
        KeyboardDot.Background = cc.Settings.Keyboard.Color.ToBrush();
        Navigate("dashboard");
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
        KeyboardType.PerKeyRgb => Loc.T("RGB pro Taste (nur als 1 Farbe)", "Per-key RGB (single color only)"),
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
        rb.Checked += (_, _) => Navigate(key);
        NavPanel.Children.Add(rb);
    }

    public void Navigate(string key)
    {
        _current?.OnLeave();
        _current = _pages[key]();
        PageHost.Content = _current.Build();
        ContentScroll.ScrollToTop();
        foreach (var rb in NavPanel.Children.OfType<RadioButton>())
            if ((string)rb.Tag == key && rb.IsChecked != true) rb.IsChecked = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting && _cc.Settings.General.CloseToTray)
        {
            e.Cancel = true;
            _current?.OnLeave();
            Hide();
            return;
        }
        _current?.OnLeave();
        base.OnClosing(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _current?.OnShow();
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
