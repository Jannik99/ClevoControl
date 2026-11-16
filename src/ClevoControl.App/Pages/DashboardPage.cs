using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClevoControl.App.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;
using Keyboard = System.Windows.Input.Keyboard;

namespace ClevoControl.App.Pages;

public sealed class DashboardPage(ControlCenter cc) : AppPage(cc)
{
    private DispatcherTimer? _timer;
    private readonly List<Action<SensorSnapshot, IReadOnlyList<SensorSnapshot>>> _updaters = [];
    private TileGrid? _tilePanel;
    private readonly TextBlock _editHint = Ui.Text(Loc.T("Tippe auf Kacheln, um sie ein- oder auszublenden.", "Tap tiles to show or hide them."), 12, sub: true, bottom: 12);
    private bool _editing;
    private Button? _editButton;

    private MainWindow Main => (MainWindow)Application.Current.MainWindow!;

    public override UIElement Build()
    {
        var root = new StackPanel();
        var title = Ui.PageTitle(Loc.T("Startseite", "Home"));
        root.Children.Add(title);
        // Who set the current state (scene, automation, AC auto-switch, hotkey …), one line under the title.
        _statusLine.Margin = new Thickness(0, -14, 0, 16);
        root.Children.Add(_statusLine);
        UpdateStatusLine();
        Cc.StatusChanged += OnStatusChanged;

        if (!Cc.Settings.General.FirstRunDone) root.Children.Add(FirstRunCard(root));
        CheckConflictingApps(root, root.Children.IndexOf(_statusLine) + 1);

        if (!Cc.Hardware.IsReal)
            root.Children.Add(Ui.InfoBar(Loc.T(
                "Demo-Modus: Es wurde kein Clevo-Treiber gefunden. Du kannst alles ausprobieren, an der Hardware ändert sich aber nichts. Installiere das Clevo Control Center, damit die App funktioniert.",
                "Demo mode: no Clevo driver was found. You can try everything, but nothing changes on the hardware. Install Clevo Control Center for the app to work."), true));

        root.Children.Add(QuickSettings());

        // Sensor tiles – click opens the Sensors page.
        var tiles = Ui.TileGrid(170, 8,
            SensorTile(Loc.T("CPU-Temperatur", "CPU temperature"), Color.FromRgb(76, 141, 255), s => s.Cpu.TemperatureC, s => Loc.T("Lüfter ", "Fan ") + $"{s.Cpu.Rpm} RPM · {s.Cpu.DutyPercent} %", "°C"),
            SensorTile(Loc.T("GPU-Temperatur", "GPU temperature"), Color.FromRgb(61, 220, 132), s => s.Gpu.TemperatureC, s => Loc.T("Lüfter ", "Fan ") + $"{s.Gpu.Rpm} RPM · {s.Gpu.DutyPercent} %", "°C"),
            SensorTile(Loc.T("CPU-Auslastung", "CPU load"), Color.FromRgb(255, 181, 71), s => s.CpuLoadPercent, _ => Loc.T("alle Kerne", "all cores"), "%"),
            SensorTile(Loc.T("Arbeitsspeicher", "Memory"), Color.FromRgb(200, 120, 255), s => s.MemoryUsedPercent, s => $"{s.MemoryTotalGb:0} GB", "%"));
        root.Children.Add(tiles);

        root.Children.Add(KeyboardCard());
        _scenesHost.Child = ScenesCard();
        root.Children.Add(_scenesHost);

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

    public override void OnLeave()
    {
        _timer?.Stop();
        Cc.StatusChanged -= OnStatusChanged;
    }

    // ------------------------------------------------------------------ status line, first run, conflicts

    private readonly TextBlock _statusLine = Ui.Text("", 13, sub: true);
    private readonly Border _scenesHost = new();

    private void OnStatusChanged() => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        UpdateStatusLine();
        _scenesHost.Child = ScenesCard();
        if (_tilePanel is not null) FillTiles();
    });

    private void UpdateStatusLine()
    {
        var change = Cc.LastChange;
        string? text = change is { Source: not (ChangeSource.User or ChangeSource.None) } ? change.Description
            : Cc.ActiveScene is { } s ? Loc.T($"Szene „{s}“ aktiv", $"Scene “{s}” active")
            : null;
        _statusLine.Text = text is null ? "" : (change is { Source: not (ChangeSource.User or ChangeSource.None) } ? $"{text} · {change.Time:HH:mm}" : text);
        _statusLine.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>First start: autostart, close-to-tray and hotkeys in one place, dismissable.</summary>
    private Border FirstRunCard(StackPanel root)
    {
        var g = Cc.Settings.General;
        var s = new StackPanel();
        s.Children.Add(SettingsPage.AutostartSwitch(Cc, Loc.T("Mit Windows starten", "Start with Windows"), Loc.T("Empfohlen: eigene Lüfterkurven, Effekte und die Automatik laufen nur, solange die App läuft.",
            "Recommended: custom fan curves, effects and automation only run while the app is running.")));
        s.Children.Add(Ui.Switch(Loc.T("Beim Schließen im Infobereich weiterlaufen", "Keep running in the tray when closed"), g.CloseToTray,
            v => { g.CloseToTray = v; Cc.Store.SaveSoon(); },
            Loc.T("Beenden dann über Rechtsklick auf das Symbol → Beenden.", "Then quit with a right-click on the icon → Exit.")));
        s.Children.Add(Ui.Switch(Loc.T("Globale Tastenkürzel", "Global hotkeys"), g.SceneHotkeys,
            v => { g.SceneHotkeys = v; Cc.ApplyGeneral(); },
            Loc.T("Strg+Alt+Umschalt+1…9: Szenen · +0: Tastaturlicht · +F: Lüfter voll", "Ctrl+Alt+Shift+1…9: scenes · +0: keyboard light · +F: fans full")));
        Border? card = null;
        var done = Ui.Button(Loc.T("Fertig", "Done"), () =>
        {
            g.FirstRunDone = true;
            Cc.Store.SaveSoon();
            root.Children.Remove(card);
        }, accent: true);
        s.Children.Add(done);
        card = Ui.Card(s, Loc.T("Willkommen bei ClevoControl", "Welcome to ClevoControl"),
            Loc.T("Drei Einstellungen für den Start. Du findest sie später unter „Einstellungen“.", "Three settings to start with. You find them later under “Settings”."), Icons.Sparkle);
        return card;
    }

    private static (DateTime Time, List<string> Names)? _conflictCache;

    /// <summary>Warns when Clevo's own apps run too (they overwrite fans and light). Checked off the UI thread, cached for a minute.</summary>
    private void CheckConflictingApps(StackPanel root, int index)
    {
        void Show(List<string> names)
        {
            if (names.Count == 0) return;
            var bar = Ui.InfoBar(Loc.T($"Clevo Control Center läuft und kann Lüfter und Licht überschreiben ({string.Join(", ", names)}).",
                $"Clevo Control Center is running and may override fans and light ({string.Join(", ", names)})."), true);
            var details = Ui.Button(Loc.T("Details", "Details"), () => Main.Navigate("system"));
            details.Margin = new Thickness(12, -4, 0, -4);
            details.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(details, Dock.Right);
            ((DockPanel)bar.Child).Children.Insert(1, details);
            root.Children.Insert(Math.Min(index, root.Children.Count), bar);
        }
        if (_conflictCache is { } c && DateTime.UtcNow - c.Time < TimeSpan.FromMinutes(1)) { Show(c.Names); return; }
        Task.Run(() =>
        {
            List<string> names;
            try
            {
                names = SystemInfoService.ClevoComponents()
                    .Where(x => x.Running && x.ProcessName is "ControlCenter30" or "LedKeyboardSetting")
                    .Select(x => x.Name).ToList();
            }
            catch { names = []; }
            _conflictCache = (DateTime.UtcNow, names);
            Application.Current.Dispatcher.BeginInvoke(() => Show(names));
        });
    }

    // ------------------------------------------------------------------ quick settings

    /// <summary>
    /// A quick settings tile. <paramref name="Sub"/> adds a second line (e.g. the current profile); with
    /// <paramref name="Open"/> a click opens a menu at the tile instead of toggling.
    /// </summary>
    private sealed record QuickTile(string Id, string Icon, Func<string> Label, Func<bool> IsOn, Action Toggle, Func<bool>? Available = null,
        Func<string>? Sub = null, Action<FrameworkElement>? Open = null);

    /// <summary>Effect that was active before the "screen color" tile switched to Ambient (restored when it is switched off).</summary>
    private static LightingEffectKind? _beforeAmbient;

    private void ProfileMenu(FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var p in PowerPage.ProfileOrder)
        {
            var prof = p;
            var item = new MenuItem { Header = PowerPage.ProfileName(p), IsCheckable = true, IsChecked = Cc.Settings.Power.Profile == p };
            item.Click += (_, _) =>
            {
                Cc.Settings.Power.Profile = prof;
                Cc.ApplyPower();
                FillTiles(focusId: "profile");
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => { if (target.IsVisible) target.Focus(); };
        menu.IsOpen = true;
    }

    private List<QuickTile> AllTiles()
    {
        var s = Cc.Settings;
        return
        [
            new("kblight", Icons.Keyboard, () => Loc.T("Tastaturlicht", "Keyboard light"), () => s.Keyboard.Enabled,
                () => { s.Keyboard.Enabled = !s.Keyboard.Enabled; Cc.ApplyKeyboard(); }),
            new("profile", Icons.Lightning, () => Loc.T("Leistungsprofil", "Performance profile"), () => false, () => { },
                Sub: () => PowerPage.ProfileName(s.Power.Profile), Open: ProfileMenu),
            new("fanmax", Icons.Max, () => Loc.T("Lüfter voll", "Fans full"), () => s.Fan.Mode == FanMode.Max, () => Cc.ToggleFanBoost()),
            new("throttle", Icons.Thermometer, () => Loc.T("CPU drosseln", "Throttle CPU"), () => s.Cooling.Mode != CoolingMode.Normal,
                () => { s.Cooling.Mode = s.Cooling.Mode == CoolingMode.Normal ? CoolingMode.Cool : CoolingMode.Normal; Cc.ApplyCooling(); }),
            new("ambient", Icons.Monitor, () => Loc.T("Bildschirmfarbe", "Screen color"), () => s.Keyboard.Effect == LightingEffectKind.Ambient,
                () =>
                {
                    if (s.Keyboard.Effect == LightingEffectKind.Ambient)
                    {
                        // Back to the effect that was on before (not always "static").
                        s.Keyboard.Effect = _beforeAmbient is { } prev && prev != LightingEffectKind.Ambient ? prev : LightingEffectKind.Static;
                        _beforeAmbient = null;
                    }
                    else
                    {
                        _beforeAmbient = s.Keyboard.Effect;
                        s.Keyboard.Effect = LightingEffectKind.Ambient;
                    }
                    s.Keyboard.Enabled = true;
                    Cc.ApplyKeyboard();
                }),
            new("winkey", Icons.Shield, () => Loc.T("Win-Taste sperren", "Lock Win key"), () => s.General.WinKeyLock,
                () => { s.General.WinKeyLock = !s.General.WinKeyLock; Cc.ApplyGeneral(); }),
            new("mute", Icons.Mute, () => Loc.T("Ton aus", "Mute"), () => Cc.Sound.Muted, () => Cc.Sound.Muted = !Cc.Sound.Muted),
            new("micmute", Icons.Microphone, () => Loc.T("Mikrofon aus", "Mic off"), () => Cc.Sound.MicrophoneMuted,
                () => Cc.Sound.MicrophoneMuted = !Cc.Sound.MicrophoneMuted, () => Cc.Sound.HasMicrophone),
            new("keymap", Icons.Keyboard, () => Loc.T("Tastenbelegung", "Key mapping"), () => s.KeyRemap.Enabled,
                () => { s.KeyRemap.Enabled = !s.KeyRemap.Enabled; Cc.ApplyKeyRemap(); }),
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
                () => { s.General.SceneHotkeys = !s.General.SceneHotkeys; Cc.ApplyGeneral(); }),
        ];
    }

    private Border QuickSettings()
    {
        _tilePanel = Ui.TileGrid(120, 8);
        _tilePanel.Margin = new Thickness(0, 0, 0, 14);
        FillTiles();

        var s = new StackPanel();
        s.Children.Add(_tilePanel);
        s.Children.Add(_editHint);

        // Sliders like the Windows 11 quick settings flyout.
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
                    MessageBox.Show(Loc.T("Ausgabegerät konnte nicht gewechselt werden.", "Could not switch the output device."), "ClevoControl");
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

    /// <summary>Rebuilds the tiles; <paramref name="focusId"/> gets the keyboard focus back after the rebuild.</summary>
    private void FillTiles(string? focusId = null)
    {
        focusId ??= _tilePanel!.Children.OfType<FrameworkElement>().FirstOrDefault(x => x.IsKeyboardFocusWithin)?.Tag as string;
        _tilePanel!.Children.Clear();
        var selected = Cc.Settings.General.QuickTiles;
        var all = AllTiles().Where(t => t.Available?.Invoke() ?? true).ToList();
        IEnumerable<QuickTile> shown = _editing
            ? all.OrderBy(t => selected.IndexOf(t.Id) is var i and >= 0 ? i : 100 + all.IndexOf(t))
            : selected.Select(id => all.FirstOrDefault(t => t.Id == id)).OfType<QuickTile>();
        foreach (var t in shown) _tilePanel.Children.Add(Tile(t, selected.Contains(t.Id)));
        _editHint.Visibility = _editing ? Visibility.Visible : Visibility.Collapsed;
        if (focusId is not null && _tilePanel.Children.OfType<FrameworkElement>().FirstOrDefault(x => x.Tag as string == focusId) is { } again)
            again.Dispatcher.BeginInvoke(() => Keyboard.Focus(again), DispatcherPriority.Loaded);
    }

    private Border Tile(QuickTile t, bool included)
    {
        var on = !_editing && t.IsOn();
        var icon = Ui.Icon(t.Icon, 16, on ? "TextOnAccentFillColorPrimaryBrush" : Ui.TextPrimary);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        var sub = t.Sub?.Invoke();
        var label = new TextBlock { Text = t.Label(), FontSize = 12.5, TextWrapping = sub is null ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, sub is null ? 8 : 5, 0, 0) };
        label.SetResourceReference(TextBlock.ForegroundProperty, on ? "TextOnAccentFillColorPrimaryBrush" : Ui.TextPrimary);
        var content = new Grid();
        var stack = Ui.Stack(icon, label);
        if (sub is not null)
        {
            var subText = new TextBlock { Text = sub, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
            subText.SetResourceReference(TextBlock.ForegroundProperty, on ? "TextOnAccentFillColorPrimaryBrush" : Ui.TextSecondary);
            stack.Children.Add(subText);
        }
        content.Children.Add(stack);
        if (_editing)
        {
            var mark = Ui.Icon(included ? "" : Icons.Add, 12, included ? Ui.Accent : Ui.TextSecondary);
            mark.HorizontalAlignment = HorizontalAlignment.Right;
            mark.VerticalAlignment = VerticalAlignment.Top;
            content.Children.Add(mark);
        }
        var b = new Border
        {
            Height = 72, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 10, 8), Child = content, Cursor = Cursors.Hand,
            Opacity = _editing && !included ? 0.45 : 1, ToolTip = sub is null ? t.Label() : $"{t.Label()}: {sub}", Tag = t.Id,
        };
        b.SetResourceReference(Border.BackgroundProperty, on ? Ui.Accent : "ControlFillColorDefaultBrush");
        b.SetResourceReference(Border.BorderBrushProperty, on ? Ui.Accent : "ControlStrokeColorDefaultBrush");
        b.MouseEnter += (_, _) => { if (!on) b.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush"); };
        b.MouseLeave += (_, _) => { if (!on) b.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush"); };
        b.MouseLeftButtonUp += (_, _) => Click();
        Ui.Clickable(b, sub is null ? t.Label() : $"{t.Label()}: {sub}", Click);
        return b;

        void Click()
        {
            if (_editing)
            {
                var list = Cc.Settings.General.QuickTiles;
                if (!list.Remove(t.Id)) list.Add(t.Id);
                Cc.Store.SaveSoon();
            }
            else if (t.Open is not null)
            {
                t.Open(b);
                return;
            }
            else
            {
                try { t.Toggle(); }
                catch (Exception ex) { Log.Error($"Quick tile {t.Id}", ex); }
            }
            FillTiles(focusId: b.IsKeyboardFocusWithin ? t.Id : null);
        }
    }

    private static FrameworkElement IconSlider(string icon, string tooltip, double value, Action<double> changed)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8), ToolTip = tooltip };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        g.Children.Add(Ui.Icon(icon, 16));
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = value, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
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
        card.Cursor = Cursors.Hand;
        card.ToolTip = Loc.T("Details unter „Sensoren“", "Details under “Sensors”");
        card.MouseLeftButtonUp += (_, _) => Main.Navigate("monitor");
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorSecondaryBrush");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BackgroundProperty, Ui.CardBg);
        return card;
    }

    private Border KeyboardCard()
    {
        var kb = Cc.Settings.Keyboard;
        var preview = new Border { Height = 46, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 12) };
        var glow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.9 };
        preview.Effect = glow;
        var label = Ui.Text("", 12.5, true);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        preview.Child = label;
        _updaters.Add((_, _) =>
        {
            var on = Cc.Lighting.KeyboardIsOn;
            var c = on ? Cc.Lighting.CurrentColor : RgbColor.Black;
            preview.Background = c.ToBrush();
            glow.Color = c.ToWpf();
            var effect = Cc.Settings.Keyboard.Effect;
            // The hex code only means something for a static color.
            label.Text = !on ? Loc.T("aus", "off") : effect == LightingEffectKind.Static ? $"{LightingPage.EffectName(effect)} · {c.ToHex()}" : LightingPage.EffectName(effect);
            label.Foreground = Ui.ContrastText(c);
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
        var more = Ui.Button(Loc.T("Alle Effekte", "All effects"), () => Main.Navigate("lighting"), icon: Icons.Color);
        return Ui.Card(Ui.Stack(preview, swatches, more), Loc.T("Tastaturbeleuchtung", "Keyboard lighting"), icon: Icons.Color, indent: false);
    }

    private Border ScenesCard()
    {
        var scenes = new WrapPanel();
        var i = 1;
        foreach (var s in Cc.Settings.Scenes)
        {
            var scene = s;
            var active = string.Equals(Cc.ActiveScene, scene.Name, StringComparison.OrdinalIgnoreCase);
            var b = Ui.Button(scene.Name, () => { Cc.ApplyScene(scene); Main.Navigate("dashboard"); }, accent: active, icon: ScenesPage.IconGlyph(scene.Icon));
            if (i <= 9 && Cc.Settings.General.SceneHotkeys)
            {
                b.ToolTip = ScenesPage.HotkeyText(i);
                // Small hotkey number after the name.
                if (b.Content is StackPanel sp)
                    sp.Children.Add(new TextBlock { Text = i.ToString(), FontSize = 11, Opacity = 0.65, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            if (active) System.Windows.Automation.AutomationProperties.SetName(b, scene.Name + Loc.T(" (aktiv)", " (active)"));
            i++;
            scenes.Children.Add(b);
        }
        return Ui.Card(scenes, Loc.T("Szenen", "Scenes"), Loc.T("Ein Klick stellt Licht, Lüfter und Leistung auf einmal um.", "One click switches light, fans and performance at once."), Icons.Star);
    }
}
