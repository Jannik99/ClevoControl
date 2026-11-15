using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClevoControl.Core;
using ClevoControl.Core.Platform;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;
using ClevoControl.Desktop.Controls;

namespace ClevoControl.Desktop.Pages;

public sealed class DashboardPage(ControlCenter cc) : AppPage(cc)
{
    private DispatcherTimer? _timer;
    private readonly List<Action<SensorSnapshot, IReadOnlyList<SensorSnapshot>>> _updaters = [];
    private WrapPanel? _tilePanel;
    private readonly StackPanel _whyPanel = new() { Margin = new Thickness(0, 0, 0, 8) };
    private bool _editing;
    private Button? _editButton;

    private static MainWindow? Main => Owner as MainWindow;

    private static Cursor HandCursor => new(StandardCursorType.Hand);

    public override Control Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Startseite", "Home")));

        if (!Cc.Hardware.IsReal)
            root.Children.Add(Ui.InfoBar(OperatingSystem.IsWindows()
                ? Loc.T(
                    "Demo-Modus: Es wurde kein Clevo-Treiber gefunden. Du kannst alles ausprobieren, an der Hardware ändert sich aber nichts. Installiere das Clevo Control Center, damit die App funktioniert.",
                    "Demo mode: no Clevo driver was found. You can try everything, but nothing changes on the hardware. Install Clevo Control Center for the app to work.")
                : Loc.T(
                    "Demo-Modus: Es wurde kein Clevo-Treiber gefunden. Du kannst alles ausprobieren, an der Hardware ändert sich aber nichts. Installiere tuxedo-drivers und die udev-Regel (contrib/linux/install.sh), damit die App funktioniert.",
                    "Demo mode: no Clevo driver was found. You can try everything, but nothing changes on the hardware. Install tuxedo-drivers and the udev rule (contrib/linux/install.sh) for the app to work."), true));

        root.Children.Add(QuickSettings());

        // Sensor tiles – click opens the Sensors page.
        var tiles = new UniformGrid4();
        tiles.Add(SensorTile(Loc.T("CPU-Temperatur", "CPU temperature"), Color.FromRgb(76, 141, 255), s => s.Cpu.TemperatureC, s => FanText(s.Cpu), "°C"));
        tiles.Add(SensorTile(Loc.T("GPU-Temperatur", "GPU temperature"), Color.FromRgb(61, 220, 132), s => s.Gpu.TemperatureC, s => FanText(s.Gpu), "°C"));
        tiles.Add(SensorTile(Loc.T("CPU-Auslastung", "CPU load"), Color.FromRgb(255, 181, 71), s => s.CpuLoadPercent, _ => Loc.T("alle Kerne", "all cores"), "%"));
        tiles.Add(SensorTile(Loc.T("Arbeitsspeicher", "Memory"), Color.FromRgb(200, 120, 255), s => s.MemoryUsedPercent, s => $"{s.MemoryTotalGb:0} GB", "%"));
        root.Children.Add(tiles);

        root.Children.Add(KeyboardCard());
        root.Children.Add(ScenesCard());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
        return root;
    }

    /// <summary>"Fan 2400 RPM · 45 %". The Linux driver reports no RPM, then only the duty is shown.</summary>
    private static string FanText(FanReading f) =>
        Loc.T("Lüfter ", "Fan ") + (f.Rpm > 0 ? $"{f.Rpm} RPM · {f.DutyPercent} %" : $"{f.DutyPercent} %");

    private void Refresh()
    {
        var latest = Cc.Sensors.Latest;
        var hist = Cc.Sensors.History;
        foreach (var u in _updaters) u(latest, hist);
    }

    public override void OnLeave() => _timer?.Stop();

    // ------------------------------------------------------------------ quick settings

    /// <summary>
    /// A quick settings tile. <paramref name="Available"/> hides tiles that cannot work here; if
    /// <paramref name="Why"/> returns text, the tile editor lists it with that explanation.
    /// </summary>
    private sealed record QuickTile(string Id, string Icon, Func<string> Label, Func<bool> IsOn, Action Toggle,
        Func<bool>? Available = null, Func<string?>? Why = null, Func<string?>? Hint = null);

    private static string OnLinuxViaKeyd => Loc.T(
        "Unter Linux über den keyd-Export auf der Seite „Tastenbelegung“.",
        "On Linux through the keyd export on the “Key mapping” page.");

    private List<QuickTile> AllTiles()
    {
        var s = Cc.Settings;
        var windows = OperatingSystem.IsWindows();
        return
        [
            new("kblight", Icons.Keyboard, () => Loc.T("Tastaturlicht", "Keyboard light"), () => s.Keyboard.Enabled,
                () => { s.Keyboard.Enabled = !s.Keyboard.Enabled; Cc.ApplyKeyboard(); }),
            new("profile", Icons.Lightning, () => PowerPage.ProfileName(s.Power.Profile), () => s.Power.Profile == PerformanceProfile.Performance,
                () =>
                {
                    var order = PowerPage.ProfileOrder;
                    s.Power.Profile = order[(Array.IndexOf(order, s.Power.Profile) + 1) % order.Length];
                    Cc.ApplyPower();
                }),
            new("fanmax", Icons.Max, () => Loc.T("Lüfter voll", "Fans full"), () => s.Fan.Mode == FanMode.Max, () => Cc.ToggleFanBoost()),
            new("throttle", Icons.Thermometer, () => Loc.T("CPU drosseln", "Throttle CPU"), () => s.Cooling.Mode != CoolingMode.Normal,
                () => { s.Cooling.Mode = s.Cooling.Mode == CoolingMode.Normal ? CoolingMode.Cool : CoolingMode.Normal; Cc.ApplyCooling(); },
                () => CoolingService.IsSupported,
                () => Loc.T("Der Prozessor bietet keine Turbo-Steuerung (intel_pstate / cpufreq boost).", "The processor offers no turbo control (intel_pstate / cpufreq boost).")),
            new("ambient", Icons.Monitor, () => Loc.T("Bildschirmfarbe", "Screen color"), () => s.Keyboard.Effect == LightingEffectKind.Ambient,
                () =>
                {
                    s.Keyboard.Effect = s.Keyboard.Effect == LightingEffectKind.Ambient ? LightingEffectKind.Static : LightingEffectKind.Ambient;
                    s.Keyboard.Enabled = true;
                    Cc.ApplyKeyboard();
                },
                () => ScreenGrabber.UnsupportedReason is null,
                () => Loc.T("Bildschirmaufnahme nicht möglich: ", "Screen capture not possible: ") + ScreenGrabber.UnsupportedReason),
            new("winkey", Icons.Shield, () => Loc.T("Win-Taste sperren", "Lock Win key"), () => s.General.WinKeyLock,
                () => { s.General.WinKeyLock = !s.General.WinKeyLock; Cc.ApplyGeneral(); },
                () => windows, () => OnLinuxViaKeyd),
            new("mute", Icons.Mute, () => Loc.T("Ton aus", "Mute"), () => Cc.Sound.Muted, () => Cc.Sound.Muted = !Cc.Sound.Muted,
                () => windows || LinuxAudio.Available,
                () => Loc.T("Braucht „pactl“ (PipeWire / PulseAudio).", "Needs “pactl” (PipeWire / PulseAudio).")),
            new("micmute", Icons.Microphone, () => Loc.T("Mikrofon aus", "Mic off"), () => Cc.Sound.MicrophoneMuted,
                () => Cc.Sound.MicrophoneMuted = !Cc.Sound.MicrophoneMuted, () => Cc.Sound.HasMicrophone),
            new("keymap", Icons.Keyboard, () => Loc.T("Tastenbelegung", "Key mapping"), () => s.KeyRemap.Enabled,
                () => { s.KeyRemap.Enabled = !s.KeyRemap.Enabled; Cc.ApplyKeyRemap(); },
                () => windows, () => OnLinuxViaKeyd),
            new("lowhz", Icons.Monitor, () => Loc.T($"{s.Power.BatteryRefreshRate} Hz", $"{s.Power.BatteryRefreshRate} Hz"),
                () => DisplayService.GetRefreshRate() < DisplayService.GetMaxRefreshRate(),
                () =>
                {
                    var max = DisplayService.GetMaxRefreshRate();
                    DisplayService.SetRefreshRate(DisplayService.GetRefreshRate() < max ? max : s.Power.BatteryRefreshRate);
                }, () => DisplayService.GetRefreshRates().Count > 1),
            new("openrgb", Icons.Plug, () => "OpenRGB", () => s.Games.OpenRgbServerEnabled,
                () => { s.Games.OpenRgbServerEnabled = !s.Games.OpenRgbServerEnabled; Cc.ApplyKeyboard(); }),
            new("auto", Icons.Sparkle, () => Loc.T("Automatik", "Automation"), () => s.AutoProfiles.Enabled,
                () => { s.AutoProfiles.Enabled = !s.AutoProfiles.Enabled; Cc.Store.SaveSoon(); }),
            new("hotkeys", Icons.Keyboard, () => Loc.T("Tastenkürzel", "Hotkeys"), () => s.General.SceneHotkeys,
                () => { s.General.SceneHotkeys = !s.General.SceneHotkeys; Cc.Store.SaveSoon(); },
                Hint: () => Cc.KeyboardInputError),
        ];
    }

    private Border QuickSettings()
    {
        _tilePanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        FillTiles();

        var s = new StackPanel();
        s.Children.Add(_tilePanel);
        s.Children.Add(_whyPanel);

        // Sliders like the Windows 11 quick settings flyout.
        if (OperatingSystem.IsWindows() || LinuxAudio.Available)
            s.Children.Add(IconSlider(Icons.Volume, Loc.T("Lautstärke", "Volume"), Cc.Sound.Volume ?? 50, v => Cc.Sound.Volume = (int)v));
        if (DisplayService.GetBrightness() is { } br)
            s.Children.Add(IconSlider(Icons.Brightness, Loc.T("Bildschirmhelligkeit", "Screen brightness"), br, v => DisplayService.SetBrightness((int)v)));
        s.Children.Add(IconSlider(Icons.Keyboard, Loc.T("Tastaturhelligkeit", "Keyboard brightness"), Cc.Settings.Keyboard.Brightness, v =>
        {
            Cc.Settings.Keyboard.Brightness = (int)v;
            Cc.ApplyKeyboard();
        }));

        var devices = Cc.Sound.GetOutputDevices();
        if (devices.Count > 1)
        {
            var current = devices.FirstOrDefault(d => d.IsDefault)?.Id ?? devices[0].Id;
            var combo = Ui.Combo(devices.Select(d => (d.Id, d.Name)), current, id =>
            {
                if (!SoundService.SetDefaultOutput(id))
                    _ = Ui.MessageAsync(Owner, Loc.T("Ausgabegerät konnte nicht gewechselt werden.", "Could not switch the output device."));
            }, 300);
            s.Children.Add(Ui.Row(Loc.T("Ausgabegerät", "Output device"), combo, bottom: 0));
        }

        _editButton = Ui.Button(Loc.T("Bearbeiten", "Edit"), () =>
        {
            _editing = !_editing;
            _editButton!.Content = _editing ? Loc.T("Fertig", "Done") : Loc.T("Bearbeiten", "Edit");
            FillTiles();
        }, icon: Icons.Edit);
        _editButton.Margin = new Thickness(0);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(_editButton, Dock.Right);
        head.Children.Add(_editButton);
        var title = Ui.Text(Loc.T("Schnelleinstellungen", "Quick settings"), 14, true);
        title.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(title);

        return Ui.CardBorder(Ui.Stack(head, s), new Thickness(0, 0, 0, 8));
    }

    private void FillTiles()
    {
        _tilePanel!.Children.Clear();
        _whyPanel.Children.Clear();
        var selected = Cc.Settings.General.QuickTiles;
        var everything = AllTiles();
        var all = everything.Where(t => t.Available?.Invoke() ?? true).ToList();
        IEnumerable<QuickTile> shown = _editing
            ? all.OrderBy(t => selected.IndexOf(t.Id) is var i and >= 0 ? i : 100 + all.IndexOf(t))
            : selected.Select(id => all.FirstOrDefault(t => t.Id == id)).OfType<QuickTile>();
        foreach (var t in shown) _tilePanel.Children.Add(Tile(t, selected.Contains(t.Id)));
        if (_editing)
        {
            _tilePanel.Children.Add(Ui.Text(Loc.T("Tippe auf Kacheln, um sie ein- oder auszublenden.", "Tap tiles to show or hide them."), 12, sub: true));
            // Tiles that cannot work on this system: say why instead of silently leaving them out.
            foreach (var t in everything.Except(all))
                if (t.Why?.Invoke() is { Length: > 0 } why)
                {
                    _whyPanel.Children.Add(Ui.Text($"{t.Label()} – {Loc.T("hier nicht verfügbar", "not available here")}: {why}", 12, sub: true, bottom: 4));
                }
        }
    }

    private Border Tile(QuickTile t, bool included)
    {
        var on = !_editing && t.IsOn();
        var icon = Ui.Icon(t.Icon, 16, on ? "TextOnAccentFillColorPrimaryBrush" : Ui.TextPrimary);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        var label = new TextBlock { Text = t.Label(), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        Ui.SetRes(label, TextBlock.ForegroundProperty, on ? "TextOnAccentFillColorPrimaryBrush" : Ui.TextPrimary);
        var content = new Grid();
        content.Children.Add(Ui.Stack(icon, label));
        if (_editing)
        {
            var mark = Ui.Icon(included ? "" : Icons.Add, 12, included ? Ui.Accent : Ui.TextSecondary);
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.VerticalAlignment = VerticalAlignment.Top;
            content.Children.Add(mark);
        }
        var b = new Border
        {
            Width = 132, Height = 72, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 10, 8), Margin = new Thickness(0, 0, 8, 8), Child = content, Cursor = HandCursor,
            Opacity = _editing && !included ? 0.45 : 1,
        };
        var hint = t.Hint?.Invoke();
        ToolTip.SetTip(b, hint is { Length: > 0 } ? $"{t.Label()}\n{hint}" : t.Label());
        Ui.SetRes(b, Border.BackgroundProperty, on ? Ui.Accent : "ControlFillColorDefaultBrush");
        Ui.SetRes(b, Border.BorderBrushProperty, on ? Ui.Accent : "ControlStrokeColorDefaultBrush");
        b.PointerEntered += (_, _) => { if (!on) Ui.SetRes(b, Border.BackgroundProperty, "ControlFillColorSecondaryBrush"); };
        b.PointerExited += (_, _) => { if (!on) Ui.SetRes(b, Border.BackgroundProperty, "ControlFillColorDefaultBrush"); };
        b.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if (_editing)
            {
                var list = Cc.Settings.General.QuickTiles;
                if (!list.Remove(t.Id)) list.Add(t.Id);
                Cc.Store.SaveSoon();
            }
            else
            {
                try { t.Toggle(); }
                catch (Exception ex) { Log.Error($"Quick tile {t.Id}", ex); }
            }
            FillTiles();
        };
        return b;
    }

    private static Control IconSlider(string icon, string tooltip, double value, Action<double> changed)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8), ColumnDefinitions = new ColumnDefinitions("36,*,48") };
        ToolTip.SetTip(g, tooltip);
        var ic = Ui.Icon(icon, 16);
        ic.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(ic);
        // Negative margin: the Fluent slider template is taller than WPF's, keep the rows as compact as there.
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -8, 0, -8) };
        Grid.SetColumn(slider, 1);
        var text = Ui.Text($"{value:0} %", 12.5, sub: true);
        text.HorizontalAlignment = HorizontalAlignment.Right;
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 2);
        slider.ValueChanged += (_, e) => { text.Text = $"{e.NewValue:0} %"; changed(e.NewValue); };
        g.Children.Add(slider);
        g.Children.Add(text);
        return g;
    }

    // ------------------------------------------------------------------ sensors, keyboard, scenes

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
        var spark = new Sparkline { Height = 40, Margin = new Thickness(0, 8, 0, 0), LineColor = color, Max = 100, Min = unit == "°C" ? 30 : 0 };
        var head = Ui.Text(title, 12, sub: true);
        var stack = Ui.Stack(head, valueRow, sub, spark);
        _updaters.Add((s, h) =>
        {
            big.Text = value(s).ToString("0");
            sub.Text = detail(s);
            spark.SetValues(h.Select(value).ToList());
        });
        var card = Ui.CardBorder(stack, new Thickness(0, 0, 0, 8));
        card.Cursor = HandCursor;
        ToolTip.SetTip(card, Loc.T("Details unter „Sensoren“", "Details under “Sensors”"));
        card.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Main?.Navigate("monitor"); };
        card.PointerEntered += (_, _) => Ui.SetRes(card, Border.BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
        card.PointerExited += (_, _) => Ui.SetRes(card, Border.BackgroundProperty, Ui.CardBg);
        return card;
    }

    private Border KeyboardCard()
    {
        var kb = Cc.Settings.Keyboard;
        var preview = new Border { Height = 46, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 12) };
        var label = Ui.Text("", 12.5, true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        preview.Child = label;
        var darkText = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
        _updaters.Add((_, _) =>
        {
            var on = Cc.Lighting.KeyboardIsOn;
            var c = on ? Cc.Lighting.CurrentColor : RgbColor.Black;
            preview.Background = c.ToBrush();
            // Glow around the preview (WPF: DropShadowEffect with blur 30, opacity 0.9).
            preview.BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = Color.FromArgb(230, c.R, c.G, c.B) });
            var effect = Cc.Settings.Keyboard.Effect;
            // The hex code only means something for a static color.
            label.Text = !on ? Loc.T("aus", "off") : effect == LightingEffectKind.Static ? $"{LightingPage.EffectName(effect)} · {c.ToHex()}" : LightingPage.EffectName(effect);
            label.Foreground = c.ToHsv().V > 0.5 ? darkText : Brushes.White;
        });

        var swatches = new WrapPanel();
        foreach (var f in kb.Favorites)
        {
            var col = f;
            swatches.Children.Add(Ui.Swatch(col, 26, () =>
            {
                Cc.Settings.Keyboard.Color = col;
                if (Cc.Settings.Keyboard.Effect is LightingEffectKind.Off) Cc.Settings.Keyboard.Effect = LightingEffectKind.Static;
                Cc.Settings.Keyboard.Enabled = true;
                Cc.ApplyKeyboard();
            }));
        }
        var more = Ui.Button(Loc.T("Alle Effekte", "All effects"), () => Main?.Navigate("lighting"), icon: Icons.Color);
        return Ui.Card(Ui.Stack(preview, swatches, more), Loc.T("Tastaturbeleuchtung", "Keyboard lighting"), icon: Icons.Color);
    }

    private Border ScenesCard()
    {
        var scenes = new WrapPanel();
        var i = 1;
        foreach (var s in Cc.Settings.Scenes)
        {
            var scene = s;
            var b = Ui.Button(scene.Name, () => { Cc.ApplyScene(scene); Main?.Navigate("dashboard"); }, icon: ScenesPage.IconGlyph(scene.Icon));
            if (i <= 9 && Cc.Settings.General.SceneHotkeys) ToolTip.SetTip(b, ScenesPage.HotkeyText(i));
            i++;
            scenes.Children.Add(b);
        }
        return Ui.Card(scenes, Loc.T("Szenen", "Scenes"), Loc.T("Ein Klick stellt Licht, Lüfter und Leistung auf einmal um.", "One click switches light, fans and performance at once."), Icons.Star);
    }

    /// <summary>Four equal columns that wrap to two on narrow windows.</summary>
    private sealed class UniformGrid4 : Grid
    {
        private readonly List<Control> _items = [];
        private int _cols;

        public UniformGrid4() => SizeChanged += (_, e) => Layout(e.NewSize.Width);

        public void Add(Control e)
        {
            _items.Add(e);
            Children.Add(e);
            _cols = 0;
            Layout(Bounds.Width);
        }

        private void Layout(double width)
        {
            var cols = width is > 0 and < 760 ? 2 : 4;
            if (cols == _cols) return;
            _cols = cols;
            ColumnDefinitions.Clear();
            RowDefinitions.Clear();
            for (var c = 0; c < cols * 2 - 1; c++)
                ColumnDefinitions.Add(c % 2 == 0 ? new ColumnDefinition(1, GridUnitType.Star) : new ColumnDefinition(8, GridUnitType.Pixel));
            for (var r = 0; r < (_items.Count + cols - 1) / cols; r++) RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var i = 0; i < _items.Count; i++)
            {
                SetColumn(_items[i], i % cols * 2);
                SetRow(_items[i], i / cols);
            }
        }
    }
}
