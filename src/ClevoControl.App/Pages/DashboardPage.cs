using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClevoControl.App.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.App.Pages;

public sealed class DashboardPage(ControlCenter cc) : AppPage(cc)
{
    private DispatcherTimer? _timer;
    private readonly List<Action<SensorSnapshot, IReadOnlyList<SensorSnapshot>>> _updaters = [];

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Übersicht", "Dashboard")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Live-Werte deines Laptops und die wichtigsten Schalter.", "Live values of your laptop and the most important switches.")));

        // Sensor tiles
        var tiles = new UniformGrid4();
        tiles.Add(SensorTile("CPU", Color.FromRgb(76, 141, 255), s => s.Cpu.TemperatureC, s => $"{s.Cpu.Rpm} RPM · {s.Cpu.DutyPercent}%", "°C"));
        tiles.Add(SensorTile("GPU", Color.FromRgb(61, 220, 132), s => s.Gpu.TemperatureC, s => $"{s.Gpu.Rpm} RPM · {s.Gpu.DutyPercent}%", "°C"));
        tiles.Add(SensorTile(Loc.T("CPU-Last", "CPU load"), Color.FromRgb(255, 181, 71), s => s.CpuLoadPercent, _ => Loc.T("alle Kerne", "all cores"), "%"));
        tiles.Add(SensorTile(Loc.T("Arbeitsspeicher", "Memory"), Color.FromRgb(200, 120, 255), s => s.MemoryUsedPercent, s => $"{s.MemoryTotalGb:0} GB", "%"));
        root.Children.Add(tiles);

        // Quick controls
        var quick = new Grid();
        quick.ColumnDefinitions.Add(new ColumnDefinition());
        quick.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        quick.ColumnDefinitions.Add(new ColumnDefinition());
        var left = KeyboardCard();
        var right = SystemCard();
        Grid.SetColumn(right, 2);
        quick.Children.Add(left);
        quick.Children.Add(right);
        root.Children.Add(quick);

        // Scenes
        var scenes = new WrapPanel();
        var i = 1;
        foreach (var s in Cc.Settings.Scenes)
        {
            var scene = s;
            var b = Ui.Button($"{scene.Icon}  {scene.Name}", () => { Cc.ApplyScene(scene); ((MainWindow)Application.Current.MainWindow!).Navigate("dashboard"); });
            if (i <= 9 && Cc.Settings.General.SceneHotkeys) b.ToolTip = $"Ctrl+Alt+Shift+{i}";
            i++;
            scenes.Children.Add(b);
        }
        root.Children.Add(Ui.Card(scenes, Loc.T("Szenen", "Scenes"), Loc.T("Ein Klick stellt Licht, Lüfter und Leistung auf einmal um.", "One click switches light, fans and performance at once.")));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
        return root;
    }

    private void Refresh()
    {
        var latest = Cc.Sensors.Latest;
        var hist = Cc.Sensors.History;
        foreach (var u in _updaters) u(latest, hist);
    }

    public override void OnLeave() => _timer?.Stop();

    private Border SensorTile(string title, Color color, Func<SensorSnapshot, double> value, Func<SensorSnapshot, string> detail, string unit)
    {
        var big = Ui.Text("–", 30, true);
        var unitText = Ui.Text(unit, 14, sub: true);
        unitText.Margin = new Thickness(4, 0, 0, 5);
        unitText.VerticalAlignment = VerticalAlignment.Bottom;
        var valueRow = new StackPanel { Orientation = Orientation.Horizontal };
        valueRow.Children.Add(big);
        valueRow.Children.Add(unitText);
        var sub = Ui.Text("", 12, sub: true);
        var spark = new Sparkline { Height = 46, Margin = new Thickness(0, 10, 0, 0), LineColor = color, Max = unit == "°C" ? 100 : 100, Min = unit == "°C" ? 30 : 0 };
        var head = Ui.Text(title, 12, sub: true);
        var stack = Ui.Stack(head, valueRow, sub, spark);
        _updaters.Add((s, h) =>
        {
            var v = value(s);
            big.Text = v.ToString("0");
            sub.Text = detail(s);
            spark.SetValues(h.Select(value).ToList());
        });
        return Ui.CardBorder(stack, new Thickness(0, 0, 0, 8));
    }

    private Border KeyboardCard()
    {
        var kb = Cc.Settings.Keyboard;
        var preview = new Border { Height = 54, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 14) };
        var glow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.9 };
        preview.Effect = glow;
        var label = Ui.Text("", 12.5, true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Foreground = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        preview.Child = label;
        _updaters.Add((_, _) =>
        {
            var c = Cc.Lighting.KeyboardIsOn ? Cc.Lighting.CurrentColor : RgbColor.Black;
            preview.Background = c.ToBrush();
            glow.Color = c.ToWpf();
            label.Text = Cc.Lighting.KeyboardIsOn ? $"{LightingPage.EffectName(Cc.Settings.Keyboard.Effect)} · {c.ToHex()}" : Loc.T("aus", "off");
            label.Foreground = c.ToHsv().V > 0.5 ? new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)) : Brushes.White;
        });

        var swatches = new WrapPanel();
        foreach (var f in kb.Favorites)
        {
            var col = f;
            swatches.Children.Add(Ui.Swatch(col, 26, () =>
            {
                Cc.Settings.Keyboard.Color = col;
                if (Cc.Settings.Keyboard.Effect is Core.Settings.LightingEffectKind.Off) Cc.Settings.Keyboard.Effect = Core.Settings.LightingEffectKind.Static;
                Cc.Settings.Keyboard.Enabled = true;
                Cc.ApplyKeyboard();
            }));
        }

        var brightness = Ui.SliderRow(Loc.T("Helligkeit", "Brightness"), 0, 100, kb.Brightness, v =>
        {
            Cc.Settings.Keyboard.Brightness = (int)v;
            Cc.ApplyKeyboard();
        }, v => $"{v:0} %");

        var power = Ui.Switch(Loc.T("Beleuchtung an", "Light on"), kb.Enabled, on => { Cc.Settings.Keyboard.Enabled = on; Cc.ApplyKeyboard(); });
        return Ui.Card(Ui.Stack(preview, swatches, brightness, power), Loc.T("Tastatur", "Keyboard"));
    }

    private Border SystemCard()
    {
        var profile = Ui.Combo(Enum.GetValues<PerformanceProfile>().Select(p => (p, PowerPage.ProfileName(p))), Cc.Settings.Power.Profile,
            p => { Cc.Settings.Power.Profile = p; Cc.ApplyPower(); }, 180);
        var fan = Ui.Combo(Enum.GetValues<FanMode>().Select(m => (m, FansPage.ModeName(m))), Cc.Settings.Fan.Mode,
            m => { Cc.Settings.Fan.Mode = m; Cc.ApplyFans(); }, 180);

        var vol = Cc.Sound.Volume ?? 50;
        var volume = Ui.SliderRow(Loc.T("Lautstärke", "Volume"), 0, 100, vol, v => Cc.Sound.Volume = (int)v, v => $"{v:0} %");
        var stack = Ui.Stack(Ui.Row(Loc.T("Leistungsprofil", "Performance profile"), profile), Ui.Row(Loc.T("Lüfter", "Fans"), fan), volume);

        if (DisplayService.GetBrightness() is { } br)
            stack.Children.Add(Ui.SliderRow(Loc.T("Bildschirmhelligkeit", "Screen brightness"), 0, 100, br, v => DisplayService.SetBrightness((int)v), v => $"{v:0} %"));

        var battery = Ui.Text("", 12.5, sub: true);
        _updaters.Add((_, _) =>
        {
            Core.Native.Win32Public.PowerStatus(out var ac, out var pct);
            battery.Text = Loc.T("Akku: ", "Battery: ") + $"{pct} %" + (ac ? Loc.T(" · Netzteil", " · AC power") : Loc.T(" · Akkubetrieb", " · on battery"));
        });
        stack.Children.Add(Ui.Switch(Loc.T("Windows-Taste sperren", "Lock Windows key"), Cc.Settings.General.WinKeyLock,
            on => { Cc.Settings.General.WinKeyLock = on; Cc.ApplyGeneral(); }, Loc.T("Praktisch beim Zocken", "Handy while gaming")));
        stack.Children.Add(battery);
        return Ui.Card(stack, Loc.T("System", "System"));
    }

    /// <summary>Four equal columns that wrap to two on narrow windows.</summary>
    private sealed class UniformGrid4 : Grid
    {
        private readonly List<UIElement> _items = [];

        public UniformGrid4() => SizeChanged += (_, _) => Layout();

        public void Add(UIElement e)
        {
            _items.Add(e);
            Children.Add(e);
            Layout();
        }

        private void Layout()
        {
            var cols = ActualWidth is > 0 and < 760 ? 2 : 4;
            ColumnDefinitions.Clear();
            RowDefinitions.Clear();
            for (var c = 0; c < cols * 2 - 1; c++)
                ColumnDefinitions.Add(c % 2 == 0 ? new ColumnDefinition() : new ColumnDefinition { Width = new GridLength(8) });
            for (var r = 0; r < (_items.Count + cols - 1) / cols; r++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < _items.Count; i++)
            {
                SetColumn(_items[i], i % cols * 2);
                SetRow(_items[i], i / cols);
            }
        }
    }
}
