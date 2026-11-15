using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ClevoControl.Core;
using ClevoControl.Desktop.Pages;

namespace ClevoControl.Desktop;

/// <summary>Main window: navigation on the left, the current page on the right (like the WPF app).</summary>
public sealed class MainWindow : Window
{
    public static readonly string[] PageKeys =
        ["dashboard", "lighting", "scenes", "automation", "power", "fans", "battery", "games", "keys", "monitor", "system", "settings"];

    private readonly ControlCenter _cc;
    private readonly Dictionary<string, Func<AppPage>> _pages;
    private readonly StackPanel _nav = new();
    private readonly ContentControl _host = new();
    private readonly ScrollViewer _scroll;
    private readonly Border _keyboardDot;
    private readonly Material.Icons.Avalonia.MaterialIcon _keyboardIcon = new() { Kind = Icons.Kind(Icons.Keyboard)!.Value, Width = 22, Height = 22 };
    private AppPage? _current;

    public MainWindow(ControlCenter cc)
    {
        _cc = cc;
        Title = "ClevoControl";
        Width = 1120;
        Height = 780;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        using (var icon = AssetLoader.Open(new Uri("avares://clevocontrol/Assets/icon-256.png"))) Icon = new WindowIcon(icon);
        Ui.SetRes(this, BackgroundProperty, "SolidBackgroundFillColorBaseBrush");

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

        // ---- navigation pane --------------------------------------------------------------
        _keyboardDot = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1),
            Child = new Avalonia.Controls.Panel { Children = { Centered(_keyboardIcon) } },
        };
        SetKeyboardDot(cc.Settings.Keyboard.Color);
        Ui.SetRes(_keyboardDot, Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        var hw = cc.Hardware;
        var header = new DockPanel { Margin = new Thickness(8, 12, 0, 22) };
        DockPanel.SetDock(_keyboardDot, Dock.Left);
        header.Children.Add(_keyboardDot);
        var model = Ui.Text(DeviceModel() ?? "Clevo", 15, true);
        model.TextWrapping = TextWrapping.NoWrap;
        model.TextTrimming = TextTrimming.CharacterEllipsis;
        var status = Ui.Text(hw.IsReal
            ? Loc.T("Tastatur: ", "Keyboard: ") + KeyboardTypeText(hw.KeyboardType)
            : Loc.T("Demo-Modus – kein Clevo-Treiber", "Demo mode – no Clevo driver"), 12, sub: true);
        header.Children.Add(new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { model, status } });

        var version = Ui.Text("Version " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3) + " · " + Loc.T("KI-generiert, inoffiziell", "AI-generated, unofficial"), 11);
        Ui.SetRes(version, TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        version.Margin = new Thickness(12, 8, 0, 0);

        var navPane = new DockPanel { Margin = new Thickness(12, 8, 8, 12) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(version, Dock.Bottom);
        navPane.Children.Add(header);
        navPane.Children.Add(version);
        navPane.Children.Add(new ScrollViewer { Content = _nav, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

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

        // ---- content ------------------------------------------------------------------------
        var column = new Grid { Margin = new Thickness(36, 28, 36, 36), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        column.ColumnDefinitions[0].MaxWidth = 1040;
        column.Children.Add(_host);
        _scroll = new ScrollViewer { Content = column, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var content = new Border { CornerRadius = new CornerRadius(8, 0, 0, 0), BorderThickness = new Thickness(1, 1, 0, 0), Child = _scroll };
        Ui.SetRes(content, Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        Ui.SetRes(content, Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*") };
        root.Children.Add(navPane);
        Grid.SetColumn(content, 1);
        root.Children.Add(content);
        Content = root;

        cc.Lighting.ColorChanged += c => Dispatcher.UIThread.Post(() =>
            SetKeyboardDot(cc.Lighting.KeyboardIsOn ? c : new RgbColor(40, 40, 40)));
        Activated += (_, _) => _current?.OnShow();
        Navigate("dashboard");
    }

    /// <summary>Keyboard color in the header; the icon is black or white, whichever is readable on it.</summary>
    private void SetKeyboardDot(RgbColor c)
    {
        _keyboardDot.Background = c.ToBrush();
        _keyboardIcon.Foreground = Ui.ContrastText(c);
    }

    private static Control Centered(Control c)
    {
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        c.Opacity = 0.9;
        return c;
    }

    private static string? DeviceModel()
    {
        if (OperatingSystem.IsLinux()) return Core.Platform.LinuxSys.Dmi("product_name");
        if (!OperatingSystem.IsWindows()) return null;
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
        Ui.SetRes(line, Border.BackgroundProperty, "DividerStrokeColorDefaultBrush");
        _nav.Children.Add(line);
    }

    private void AddNav(string key, string icon, string title, bool isChecked = false)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var ic = Ui.Icon(icon, 16);
        ic.Margin = new Thickness(0, 0, 14, 0);
        sp.Children.Add(ic);
        sp.Children.Add(Ui.Text(title, 14));
        var rb = new RadioButton { Content = sp, GroupName = "nav", IsChecked = isChecked, Tag = key };
        rb.Classes.Add("nav");
        rb.IsCheckedChanged += (_, _) => { if (rb.IsChecked == true && _current?.Key != key) Navigate(key); };
        _nav.Children.Add(rb);
    }

    /// <summary>
    /// Shows a page. Navigating to the page that is already shown rebuilds it (pages use this to refresh)
    /// and keeps the scroll position; any other page starts at the top.
    /// </summary>
    public void Navigate(string key)
    {
        var same = _current?.Key == key;
        var offset = _scroll.Offset;
        _current?.OnLeave();
        _current = _pages[key]();
        _current.Key = key;
        _host.Content = _current.Build();
        _scroll.Offset = new Vector(0, 0);
        if (same && offset.Y > 0)
        {
            // The new content has no size yet: restore once it has been laid out.
            Dispatcher.UIThread.Post(() => _scroll.Offset = new Vector(0, offset.Y), DispatcherPriority.Loaded);
        }
        foreach (var rb in _nav.Children.OfType<RadioButton>())
            if ((string?)rb.Tag == key && rb.IsChecked != true) rb.IsChecked = true;
    }

    /// <summary>Renders the window content into a PNG (screenshot mode).</summary>
    public void SaveScreenshot(string path)
    {
        if (Content is not Control content) return;
        var size = new PixelSize((int)Math.Max(1, content.Bounds.Width), (int)Math.Max(1, content.Bounds.Height));
        using var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
        bmp.Render(this);
        using var fs = File.Create(path);
        bmp.Save(fs, new PngBitmapEncoderOptions());
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsExiting && _cc.Settings.General.CloseToTray)
        {
            e.Cancel = true;
            if (!App.Current.TrayAvailable)
            {
                // No tray to come back from: hiding would leave the app unreachable. Minimise instead.
                WindowState = WindowState.Minimized;
                return;
            }
            _current?.OnLeave();
            Hide();
            App.Current.ShowTrayHint();
            return;
        }
        _current?.OnLeave();
        base.OnClosing(e);
        if (!App.IsExiting && !_cc.Settings.General.CloseToTray) Dispatcher.UIThread.Post(App.Current.ExitApp);
    }

}

/// <summary>Base class for code-built pages (same as in the WPF app).</summary>
public abstract class AppPage
{
    protected AppPage(ControlCenter cc) => Cc = cc;

    protected ControlCenter Cc { get; }

    internal string? Key { get; set; }

    /// <summary>The window, e.g. as owner for dialogs.</summary>
    protected static Window? Owner => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows.OfType<MainWindow>().FirstOrDefault();

    public abstract Control Build();

    /// <summary>Called when the page is left (stop timers).</summary>
    public virtual void OnLeave() { }

    public virtual void OnShow() { }
}
