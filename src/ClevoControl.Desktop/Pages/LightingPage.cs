using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ClevoControl.Core;
using ClevoControl.Core.Hardware.Linux;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Platform;
using ClevoControl.Core.Settings;
using ClevoControl.Desktop.Controls;

namespace ClevoControl.Desktop.Pages;

public sealed class LightingPage(ControlCenter cc) : AppPage(cc)
{
    private KeyboardSettings Kb => Cc.Settings.Keyboard;
    private ContentControl? _options, _color;
    private readonly Dictionary<LightingEffectKind, RadioButton> _tiles = [];
    private Border? _preview;
    private DropShadowEffect? _glow;
    private DispatcherTimer? _timer;

    public static string EffectName(LightingEffectKind e) => e switch
    {
        LightingEffectKind.Static => Loc.T("Statisch", "Static"),
        LightingEffectKind.Breathing => Loc.T("Atmen", "Breathing"),
        LightingEffectKind.Spectrum => Loc.T("Regenbogen", "Rainbow"),
        LightingEffectKind.Gradient => Loc.T("Farbverlauf", "Gradient"),
        LightingEffectKind.Candle => Loc.T("Kerze", "Candle"),
        LightingEffectKind.Daylight => Loc.T("Tageslicht", "Daylight"),
        LightingEffectKind.Ambient => Loc.T("Bildschirmfarbe", "Screen color"),
        LightingEffectKind.Music => Loc.T("Musik", "Music"),
        LightingEffectKind.Temperature => Loc.T("Temperatur", "Temperature"),
        LightingEffectKind.Battery => Loc.T("Akkustand", "Battery level"),
        LightingEffectKind.ScreenRegion => Loc.T("Lebensleiste", "Health bar"),
        LightingEffectKind.GameCs2 => "Counter-Strike 2",
        LightingEffectKind.Reactive => Loc.T("Tastendruck", "Key press"),
        LightingEffectKind.CpuLoad => Loc.T("CPU-Last", "CPU load"),
        LightingEffectKind.OnAir => "On Air",
        LightingEffectKind.Pomodoro => "Pomodoro",
        LightingEffectKind.Heartbeat => Loc.T("Herzschlag", "Heartbeat"),
        LightingEffectKind.Lava => Loc.T("Lavalampe", "Lava lamp"),
        LightingEffectKind.Fire => Loc.T("Feuer", "Fire"),
        LightingEffectKind.Police => Loc.T("Polizei", "Police"),
        LightingEffectKind.Hardware => Loc.T("Eingebaute Effekte", "Built-in effects"),
        LightingEffectKind.Off => Loc.T("Aus", "Off"),
        LightingEffectKind.PerKey => Loc.T("Pro Taste", "Per key"),
        _ => e.ToString(),
    };

    private static (string Icon, string Desc) EffectInfo(LightingEffectKind e) => e switch
    {
        LightingEffectKind.Static => (Icons.Color, Loc.T("Eine feste Farbe", "One fixed color")),
        LightingEffectKind.Breathing => (Icons.Circle, Loc.T("Sanftes Pulsieren", "Smooth pulsing")),
        LightingEffectKind.Spectrum => (Icons.Sparkle, Loc.T("Alle Farben", "All colors")),
        LightingEffectKind.Gradient => (Icons.Swap, Loc.T("Zwischen 2 Farben", "Between 2 colors")),
        LightingEffectKind.Candle => (Icons.Lamp, Loc.T("Flackerndes Licht", "Flickering light")),
        LightingEffectKind.Daylight => (Icons.Brightness, Loc.T("Warm am Abend", "Warm in the evening")),
        LightingEffectKind.Ambient => (Icons.Monitor, Loc.T("Folgt dem Bildschirm", "Follows the screen")),
        LightingEffectKind.Music => (Icons.Music, Loc.T("Reagiert auf Sound", "Reacts to sound")),
        LightingEffectKind.Temperature => (Icons.Thermometer, Loc.T("Grün → Rot", "Green → red")),
        LightingEffectKind.Battery => (Icons.Battery, Loc.T("Zeigt Ladestand", "Shows charge")),
        LightingEffectKind.ScreenRegion => (Icons.Heart, Loc.T("Für jedes Spiel", "For any game")),
        LightingEffectKind.GameCs2 => (Icons.Target, Loc.T("Leben, Flash, Bombe", "Health, flash, bomb")),
        LightingEffectKind.Reactive => (Icons.Keyboard, Loc.T("Leuchtet beim Drücken auf", "Lights up on key press")),
        LightingEffectKind.CpuLoad => (Icons.Diagnostic, Loc.T("Blau → Rot", "Blue → red")),
        LightingEffectKind.OnAir => (Icons.Broadcast, Loc.T("Rot bei Mikrofon", "Red when mic in use")),
        LightingEffectKind.Pomodoro => (Icons.Timer, Loc.T("Fokus-Timer", "Focus timer")),
        LightingEffectKind.Heartbeat => (Icons.Pulse, Loc.T("Doppelter Puls", "Double pulse")),
        LightingEffectKind.Lava => (Icons.Layers, Loc.T("Langsames Wabern", "Slow drift")),
        LightingEffectKind.Fire => (Icons.Fire, Loc.T("Lodernde Flammen", "Blazing flames")),
        LightingEffectKind.Police => (Icons.Bell, Loc.T("Rot/Blau-Blitzer", "Red/blue strobe")),
        LightingEffectKind.Hardware => (Icons.Chip, Loc.T("Läuft auch ohne App", "Runs without the app")),
        LightingEffectKind.Off => (Icons.Power, Loc.T("Beleuchtung aus", "Light off")),
        LightingEffectKind.PerKey => (Icons.Keyboard, Loc.T("Jede Taste eigene Farbe", "Own color per key")),
        _ => (Icons.Circle, ""),
    };

    public override Control Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Beleuchtung", "Lighting")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Farbe, Helligkeit und Effekte deiner Tastaturbeleuchtung.", "Color, brightness and effects of your keyboard light.")
            + (Cc.Hardware.KeyboardType == KeyboardType.SingleZoneRgb ? Loc.T(" Deine Tastatur hat eine Zone – alle Tasten leuchten in derselben Farbe.", " Your keyboard has one zone – all keys share one color.") : "")
            + (Cc.Hardware.PerKey is not null ? Loc.T(" Deine Tastatur hat RGB pro Taste – unter „Tasten einfärben“ bekommt jede Taste ihre eigene Farbe.", " Your keyboard has per-key RGB – under “Paint keys” every key gets its own color.") : "")));

        // Preview
        _preview = new Border { Height = 70, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 18) };
        _glow = new DropShadowEffect { BlurRadius = 40, OffsetX = 0, OffsetY = 0, Opacity = 0.9 };
        _preview.Effect = _glow;
        root.Children.Add(_preview);
        void UpdatePreview()
        {
            var c = Cc.Lighting.KeyboardIsOn ? Cc.Lighting.CurrentColor : RgbColor.Black;
            _preview.Background = c.ToBrush();
            _glow.Color = c.ToAva();
        }
        UpdatePreview();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => UpdatePreview();
        _timer.Start();

        // Effects gallery
        var gallery = new StackPanel();
        void AddGroup(string title, params LightingEffectKind[] kinds)
        {
            var header = Ui.Section(title);
            if (gallery.Children.Count > 0) header.Margin = new Thickness(0, 12, 0, 8);
            gallery.Children.Add(header);
            var wrap = Ui.TileGrid(150, 8);
            foreach (var e in kinds)
            {
                var effect = e;
                var (icon, desc) = EffectInfo(e);
                var tile = Ui.Tile(icon, EffectName(e), desc, Kb.Effect == e, "effect", () =>
                {
                    Kb.Effect = effect;
                    Kb.Enabled = true;
                    Cc.ApplyKeyboard();
                    _options!.Content = BuildOptions();
                    _color!.Content = BuildColorCard();
                }, 150);
                _tiles[effect] = tile;
                wrap.Children.Add(tile);
            }
            gallery.Children.Add(wrap);
        }
        // "Per key" only exists when a per-key controller was really detected.
        var basics = new List<LightingEffectKind> { LightingEffectKind.Static };
        if (Cc.Hardware.PerKey is not null) basics.Add(LightingEffectKind.PerKey);
        basics.AddRange([LightingEffectKind.Breathing, LightingEffectKind.Spectrum, LightingEffectKind.Gradient, LightingEffectKind.Hardware]);
        AddGroup(Loc.T("Grundlagen", "Basics"), [.. basics]);
        AddGroup(Loc.T("Stimmung", "Ambience"), LightingEffectKind.Candle, LightingEffectKind.Daylight, LightingEffectKind.Lava, LightingEffectKind.Fire, LightingEffectKind.Heartbeat, LightingEffectKind.Police);
        AddGroup(Loc.T("Reagiert auf …", "Reacts to …"), LightingEffectKind.Ambient, LightingEffectKind.Music, LightingEffectKind.Reactive, LightingEffectKind.Temperature, LightingEffectKind.CpuLoad, LightingEffectKind.Battery, LightingEffectKind.OnAir, LightingEffectKind.Pomodoro);
        AddGroup(Loc.T("Spiele", "Games"), LightingEffectKind.ScreenRegion, LightingEffectKind.GameCs2);
        root.Children.Add(Ui.Card(gallery, Loc.T("Effekt", "Effect"), icon: Icons.Sparkle, indent: false));

        // One column in the order of use: effect → its options → color → general.
        _options = new ContentControl { Content = BuildOptions() };
        root.Children.Add(_options);
        _color = new ContentControl { Content = BuildColorCard() };
        root.Children.Add(_color);
        if (Cc.Hardware.PerKey is { } perKey) root.Children.Add(BuildPerKeyCard(perKey));
        root.Children.Add(Ui.Card(Ui.Stack(
            Ui.Switch(Loc.T("Beleuchtung an", "Light on"), Kb.Enabled, on => { Kb.Enabled = on; Cc.ApplyKeyboard(); }),
            Ui.SliderRow(Loc.T("Helligkeit", "Brightness"), 0, 100, Kb.Brightness, v => { Kb.Brightness = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} %"),
            Ui.Row(Loc.T("Aus bei Inaktivität", "Off when idle"),
                Ui.Combo(new[] { 0, 1, 2, 5, 10, 15, 30, 60 }.Select(m => (m, m == 0 ? Loc.T("nie", "never") : $"{m} min")), Kb.IdleTimeoutMinutes,
                    m => { Kb.IdleTimeoutMinutes = m; Cc.ApplyKeyboard(); }, 120),
                Loc.T("Geht beim nächsten Tastendruck wieder an", "Turns back on with the next key press"), 0)
        ), Loc.T("Allgemein", "General"), icon: Icons.Brightness));

        root.Children.Add(BuildWhiteBalance());
        return root;
    }

    public override void OnLeave() => _timer?.Stop();

    /// <summary>
    /// False for effects whose colors are computed by the engine (see LightingEngine.Render): the color picker has no effect there.
    /// Music (pulse mode), health bar and CS2 (brightness display / waiting) and On Air (idle) still use the color.
    /// </summary>
    public static bool UsesColor(LightingEffectKind e) => e is not (LightingEffectKind.Spectrum or LightingEffectKind.Daylight
        or LightingEffectKind.Ambient or LightingEffectKind.Temperature or LightingEffectKind.Battery or LightingEffectKind.CpuLoad
        or LightingEffectKind.Pomodoro or LightingEffectKind.Lava or LightingEffectKind.Fire or LightingEffectKind.Police);

    /// <summary>Only these effects read <see cref="KeyboardSettings.SecondaryColor"/>.</summary>
    public static bool UsesSecondColor(LightingEffectKind e) => e is LightingEffectKind.Gradient or LightingEffectKind.Reactive;

    private Border BuildColorCard()
    {
        if (!UsesColor(Kb.Effect))
            return Ui.Card(Ui.Text(Loc.T("Dieser Effekt bestimmt die Farben selbst.", "This effect chooses its own colours."), 13, sub: true),
                Loc.T("Farbe", "Color"), icon: Icons.Color);

        var picker = new ColorPicker(Kb.Color);
        picker.ColorChanged += c =>
        {
            Kb.Color = c;
            if (Kb.Effect == LightingEffectKind.Off) Kb.Effect = LightingEffectKind.Static;
            Cc.ApplyKeyboard();
        };

        var left = new StackPanel();
        var favorites = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        void FillFavorites(int focus = -1)
        {
            favorites.Children.Clear();
            foreach (var f in Kb.Favorites.ToList())
            {
                var col = f;
                void Select()
                {
                    picker.SetColor(col);
                    Kb.Color = col;
                    Cc.ApplyKeyboard();
                }
                // Click handled here (not by Ui.Swatch) so a right click (context menu) does not also select the color.
                var sw = Ui.Swatch(col, 30, null, col.ToHex() + Loc.T(" · Entf oder Rechtsklick: entfernen", " · Delete or right-click: remove"));
                Ui.Clickable(sw, col.ToHex(), Select);
                sw.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Select(); };
                var item = new MenuItem { Header = Loc.T("Aus Favoriten entfernen", "Remove from favourites"), Icon = Ui.Icon(Icons.Delete, 14) };
                item.Click += (_, _) => Dispatcher.UIThread.Post(() => RemoveFavorite(col));
                sw.ContextMenu = new ContextMenu { Items = { item } };
                sw.KeyDown += (_, e) =>
                {
                    if (e.Key != Avalonia.Input.Key.Delete) return;
                    e.Handled = true;
                    RemoveFavorite(col);
                };
                favorites.Children.Add(sw);
            }
            var add = Ui.IconButton(Icons.Add, Loc.T("Aktuelle Farbe merken", "Save current color"), () =>
            {
                if (!Kb.Favorites.Contains(Kb.Color)) Kb.Favorites.Add(Kb.Color);
                Cc.ApplyKeyboard();
                FillFavorites(Kb.Favorites.Count);
            });
            add.Width = add.Height = 30;
            add.MinWidth = add.MinHeight = 0;
            add.Padding = new Thickness(0);
            add.CornerRadius = new CornerRadius(15);
            add.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            add.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
            favorites.Children.Add(add);
            if (focus >= 0 && favorites.Children.Count > 0)
            {
                var target = favorites.Children[Math.Min(focus, favorites.Children.Count - 1)];
                Dispatcher.UIThread.Post(() => target.Focus(), DispatcherPriority.Loaded);
            }
        }
        void RemoveFavorite(RgbColor col)
        {
            var index = Kb.Favorites.IndexOf(col);
            if (index < 0) return;
            Kb.Favorites.RemoveAt(index);
            Cc.ApplyKeyboard();
            FillFavorites(index);
            Ui.ShowUndo(left, Loc.T($"{col.ToHex()} aus den Favoriten entfernt.", $"{col.ToHex()} removed from favourites."), () =>
            {
                if (!Kb.Favorites.Contains(col)) Kb.Favorites.Insert(Math.Min(index, Kb.Favorites.Count), col);
                Cc.ApplyKeyboard();
                FillFavorites(index);
            });
        }
        FillFavorites();
        left.Children.Add(picker);
        left.Children.Add(favorites);

        // Primary color left, second color (only for effects that use it) right.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        grid.Children.Add(left);
        if (UsesSecondColor(Kb.Effect))
        {
            var secondary = new ColorPicker(Kb.SecondaryColor);
            secondary.ColorChanged += c => { Kb.SecondaryColor = c; Cc.ApplyKeyboard(); };
            var expander = new Expander
            {
                Header = Ui.Text(Kb.Effect == LightingEffectKind.Reactive ? Loc.T("Zweite Farbe (Aufleuchten)", "Second color (flash)") : Loc.T("Zweite Farbe", "Second color"), 13, sub: true),
                Content = secondary,
                IsExpanded = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            Grid.SetColumn(expander, 2);
            grid.Children.Add(expander);
        }
        return Ui.Card(grid, Loc.T("Farbe", "Color"), icon: Icons.Color, indent: false);
    }

    /// <summary>Per-key editor: paint keys in the color of the color card. Only built when a per-key controller was detected.</summary>
    private Border BuildPerKeyCard(Core.Hardware.PerKey.IPerKeyKeyboard perKey)
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Text(Loc.T("Klicke auf Tasten oder ziehe darüber, um sie in der gewählten Farbe einzufärben. Rechte Maustaste oder Entf löscht. Nicht eingefärbte Tasten bleiben dunkel.",
            "Click or drag over keys to paint them in the chosen color. Right mouse button or Delete erases. Keys without a color stay dark."), 12.5, sub: true, bottom: 12));
        var keyboard = new PerKeyKeyboard(perKey.HasLed) { Margin = new Thickness(0, 0, 0, 12) };
        keyboard.SetColors(Kb.PerKeyColors);
        s.Children.Add(keyboard);

        void Changed()
        {
            keyboard.SetColors(Kb.PerKeyColors);
            // Painting switches to the "per key" effect (the tile's selection handler applies it).
            if (Kb.Effect != LightingEffectKind.PerKey && _tiles.TryGetValue(LightingEffectKind.PerKey, out var tile)) tile.IsChecked = true;
            else Cc.ApplyKeyboard();
        }

        keyboard.Painted += (id, erase) =>
        {
            if (erase ? !Kb.PerKeyColors.Remove(id) : Kb.PerKeyColors.TryGetValue(id, out var old) && old == Kb.Color) return;
            if (!erase) Kb.PerKeyColors[id] = Kb.Color;
            Changed();
        };
        s.Children.Add(Ui.Wrap(
            Ui.Button(Loc.T("Alle", "All"), () =>
            {
                foreach (var id in keyboard.PaintableKeys) Kb.PerKeyColors[id] = Kb.Color;
                Changed();
            }, icon: Icons.Brush, tip: Loc.T("Alle Tasten in der gewählten Farbe", "All keys in the chosen color")),
            Ui.Button(Loc.T("Zurücksetzen", "Reset"), () =>
            {
                if (Kb.PerKeyColors.Count == 0) return;
                var before = new Dictionary<int, RgbColor>(Kb.PerKeyColors);
                Kb.PerKeyColors.Clear();
                Changed();
                Ui.ShowUndo(s, Loc.T("Alle Tastenfarben gelöscht.", "All key colors cleared."), () =>
                {
                    Kb.PerKeyColors = new Dictionary<int, RgbColor>(before);
                    Changed();
                });
            }, icon: Icons.Delete, tip: Loc.T("Alle Tastenfarben löschen", "Clear all key colors"))));
        return Ui.Card(s, Loc.T("Tasten einfärben", "Paint keys"), perKey.Description, Icons.Keyboard, indent: false);
    }

    private Control BuildOptions()
    {
        var s = new StackPanel();
        Control Speed() => Ui.SliderRow(Loc.T("Geschwindigkeit", "Speed"), 0.1, 5, Kb.Speed, v => { Kb.Speed = v; Cc.ApplyKeyboard(); }, v => $"{v:0.0}×", 0.1);

        // Linux: screen capture depends on the desktop (X11 / grim on Wayland).
        void ScreenCaptureInfo()
        {
            if (ScreenGrabber.UnsupportedReason is { } reason)
                s.Children.Add(Ui.InfoBar(Loc.T("Bildschirmaufnahme ist auf diesem Desktop nicht möglich: ", "Screen capture is not available on this desktop: ") + reason, warning: true));
        }

        switch (Kb.Effect)
        {
            case LightingEffectKind.Breathing:
            case LightingEffectKind.Spectrum:
            case LightingEffectKind.Gradient:
            case LightingEffectKind.Candle:
            case LightingEffectKind.Heartbeat:
            case LightingEffectKind.Lava:
            case LightingEffectKind.Fire:
            case LightingEffectKind.Police:
            case LightingEffectKind.Reactive:
                if (Kb.Effect == LightingEffectKind.Reactive && Cc.KeyboardInputError is { } keyError)
                    s.Children.Add(Ui.InfoBar(Loc.T("Tastendrücke können nicht gelesen werden: ", "Key presses cannot be read: ") + keyError, warning: true));
                s.Children.Add(Speed());
                if (Kb.Effect == LightingEffectKind.Reactive)
                    s.Children.Add(Ui.Text(Loc.T("Grundfarbe = deine Farbe (gedimmt), Aufleuchten = zweite Farbe. Sind beide gleich, wechselt die Farbe bei jedem Tastendruck. Gedrückt halten löst nur einmal aus.",
                        "Base = your color (dimmed), flash = second color. If both are equal, the color changes with every key press. Holding a key only triggers once."), 12, sub: true));
                break;

            case LightingEffectKind.Ambient:
            {
                ScreenCaptureInfo();
                var monitors = ScreenSampler.GetMonitors();
                s.Children.Add(Ui.Row(Loc.T("Bildschirm", "Monitor"), Ui.Combo(monitors.Select(m => (m.Index, $"{m.Index + 1}: {m.Width}×{m.Height}{(m.Primary ? " ★" : "")}")),
                    Kb.AmbientMonitor, i => { Kb.AmbientMonitor = i; Cc.ApplyKeyboard(); }, 180)));
                s.Children.Add(Ui.SliderRow(Loc.T("Farbverstärkung", "Saturation boost"), 0.5, 3, Kb.AmbientSaturationBoost, v => { Kb.AmbientSaturationBoost = v; Cc.ApplyKeyboard(); }, v => $"{v:0.0}×", 0.1));
                s.Children.Add(Ui.SliderRow(Loc.T("Glättung", "Smoothing"), 0, 0.95, Kb.AmbientSmoothing, v => { Kb.AmbientSmoothing = v; Cc.ApplyKeyboard(); }, v => $"{v * 100:0} %", 0.05));
                s.Children.Add(Ui.SliderRow(Loc.T("Bildrate", "Frame rate"), 5, 60, Kb.AmbientFps, v => { Kb.AmbientFps = (int)v; Cc.ApplyKeyboard(); }));
                s.Children.Add(Ui.Text(Loc.T("Hinweis: Kopiergeschützte Streams (z. B. Netflix in Edge) zeigen der Bildschirmaufnahme oft nur Schwarz.",
                    "Note: copy-protected streams (e.g. Netflix in Edge) often appear black to screen capture."), 12, sub: true));
                break;
            }

            case LightingEffectKind.Music:
                if (OperatingSystem.IsLinux() && !Shell.Has("parec"))
                    s.Children.Add(Ui.InfoBar(Loc.T("Der Musikmodus braucht „parec“ (Paket pulseaudio-utils bzw. pipewire-pulse).",
                        "Music mode needs 'parec' (package pulseaudio-utils or pipewire-pulse)."), warning: true));
                s.Children.Add(Ui.Row(Loc.T("Modus", "Mode"), Ui.Combo(new[]
                {
                    (MusicColorMode.Spectrum, Loc.T("Bass/Mitten/Höhen = R/G/B", "Bass/mid/treble = R/G/B")),
                    (MusicColorMode.Pulse, Loc.T("Puls in deiner Farbe", "Pulse in your color")),
                    (MusicColorMode.HueBeat, Loc.T("Farbwechsel im Takt", "Hue follows the beat")),
                }, Kb.MusicMode, m => { Kb.MusicMode = m; Cc.ApplyKeyboard(); }, 230)));
                s.Children.Add(Ui.SliderRow(Loc.T("Empfindlichkeit", "Sensitivity"), 0.3, 3, Kb.MusicSensitivity, v => { Kb.MusicSensitivity = v; Cc.ApplyKeyboard(); }, v => $"{v:0.0}×", 0.1));
                s.Children.Add(Speed());
                break;

            case LightingEffectKind.Temperature:
                s.Children.Add(Ui.SliderRow(Loc.T("Grün bis", "Green up to"), 30, 80, Kb.TemperatureCool, v => { Kb.TemperatureCool = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} °C"));
                s.Children.Add(Ui.SliderRow(Loc.T("Rot ab", "Red from"), 60, 100, Kb.TemperatureHot, v => { Kb.TemperatureHot = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} °C"));
                break;

            case LightingEffectKind.Daylight:
                s.Children.Add(Ui.SliderRow(Loc.T("Farbtemperatur Tag", "Day color temperature"), 3000, 9000, Kb.DaylightDayKelvin, v => { Kb.DaylightDayKelvin = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} K", 100));
                s.Children.Add(Ui.SliderRow(Loc.T("Farbtemperatur Nacht", "Night color temperature"), 1200, 4000, Kb.DaylightNightKelvin, v => { Kb.DaylightNightKelvin = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} K", 100));
                s.Children.Add(Ui.SliderRow(Loc.T("Helligkeit nachts", "Brightness at night"), 5, 100, Kb.DaylightNightBrightness, v => { Kb.DaylightNightBrightness = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} %"));
                break;

            case LightingEffectKind.Pomodoro:
                s.Children.Add(Ui.SliderRow(Loc.T("Arbeitszeit", "Focus time"), 5, 90, Kb.PomodoroWorkMinutes, v => { Kb.PomodoroWorkMinutes = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} min"));
                s.Children.Add(Ui.SliderRow(Loc.T("Pause", "Break"), 1, 30, Kb.PomodoroBreakMinutes, v => { Kb.PomodoroBreakMinutes = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} min"));
                s.Children.Add(Ui.Text(Loc.T("Grün → Rot während der Arbeitszeit, Blinken kurz vor Schluss, blaues Atmen in der Pause. Startet beim Auswählen.",
                    "Green → red while focusing, blinks shortly before the end, blue breathing during the break. Starts when selected."), 12, sub: true));
                break;

            case LightingEffectKind.OnAir:
                s.Children.Add(Ui.Text(Loc.T("Sobald eine App (Teams, Discord, OBS …) das Mikrofon benutzt, pulsiert die Tastatur rot. Sonst leuchtet sie in deiner Farbe.",
                    "As soon as an app (Teams, Discord, OBS …) uses the microphone, the keyboard pulses red. Otherwise it shows your color."), 12.5, sub: true));
                break;

            case LightingEffectKind.ScreenRegion:
            case LightingEffectKind.GameCs2:
                if (Kb.Effect == LightingEffectKind.ScreenRegion) ScreenCaptureInfo();
                s.Children.Add(Ui.Text(Loc.T("Die Einstellungen dazu findest du unter „Spiele & Integrationen“.", "Settings are under “Games & integrations”."), 12.5, sub: true, bottom: 10));
                s.Children.Add(Ui.Button(Loc.T("Zu Spiele & Integrationen", "Open Games & integrations"), () => (Owner as MainWindow)?.Navigate("games")));
                break;

            case LightingEffectKind.Hardware:
                if (Cc.Hardware is LinuxClevoHardware)
                    s.Children.Add(Ui.InfoBar(Loc.T("Mit dem Linux-Treiber nicht verfügbar – nutze die Software-Effekte.",
                        "Not available with the Linux driver – use the software effects."), warning: true));
                s.Children.Add(Ui.Row(Loc.T("Effekt", "Effect"), Ui.Combo(Enum.GetValues<HardwareEffect>().Select(h => (h, HardwareEffectName(h))), Kb.HardwareEffect,
                    h => { Kb.HardwareEffect = h; Cc.ApplyKeyboard(); }, 180)));
                s.Children.Add(Ui.Text(Loc.T("Diese Effekte berechnet die Tastatur selbst – keine CPU-Last, sie laufen auch ohne die App. Tempo und Farben legt die Firmware fest.",
                    "The keyboard computes these effects itself – no CPU load, they even run without the app. Speed and colors are set by the firmware."), 12, sub: true));
                break;

            default:
                return new ContentControl();
        }
        return Ui.Card(s, Loc.T("Effekt-Optionen", "Effect options"), icon: Icons.Tune);
    }

    public static string HardwareEffectName(HardwareEffect h) => h switch
    {
        HardwareEffect.Breathe => Loc.T("Atmen", "Breathe"),
        HardwareEffect.Cycle => Loc.T("Farbwechsel", "Cycle"),
        HardwareEffect.Wave => Loc.T("Welle", "Wave"),
        HardwareEffect.Dance => Loc.T("Tanz", "Dance"),
        HardwareEffect.Flash => Loc.T("Blitz", "Flash"),
        HardwareEffect.RandomColor => Loc.T("Zufallsfarben", "Random colors"),
        HardwareEffect.Tempo => "Tempo",
        _ => h.ToString(),
    };

    private Border BuildWhiteBalance()
    {
        var s = new StackPanel();
        s.Children.Add(Ui.SliderRow(Loc.T("Rot", "Red"), 0.3, 1, Kb.GainRed, v => { Kb.GainRed = v; Cc.ApplyKeyboard(); }, v => $"{v * 100:0} %", 0.01, Ui.Gradient(Colors.Black, Colors.Red)));
        s.Children.Add(Ui.SliderRow(Loc.T("Grün", "Green"), 0.3, 1, Kb.GainGreen, v => { Kb.GainGreen = v; Cc.ApplyKeyboard(); }, v => $"{v * 100:0} %", 0.01, Ui.Gradient(Colors.Black, Colors.Lime)));
        s.Children.Add(Ui.SliderRow(Loc.T("Blau", "Blue"), 0.3, 1, Kb.GainBlue, v => { Kb.GainBlue = v; Cc.ApplyKeyboard(); }, v => $"{v * 100:0} %", 0.01, Ui.Gradient(Colors.Black, Color.FromRgb(40, 120, 255))));
        s.Children.Add(Ui.Wrap(
            Ui.Button(Loc.T("Weiß testen", "Test white"), () => Cc.Lighting.Override(RgbColor.White, TimeSpan.FromSeconds(8))),
            Ui.Button(Loc.T("Empfohlen", "Recommended"), () => { Kb.GainRed = 0.71; Kb.GainGreen = 1; Kb.GainBlue = 0.78; Cc.ApplyKeyboard(); (Owner as MainWindow)?.Navigate("lighting"); }),
            Ui.Button(Loc.T("Ohne Korrektur", "No correction"), () => { Kb.GainRed = 1; Kb.GainGreen = 1; Kb.GainBlue = 1; Cc.ApplyKeyboard(); (Owner as MainWindow)?.Navigate("lighting"); })));
        var wbExpander = new Expander
        {
            Header = Ui.Text(Loc.T("Erweitert: Weißabgleich", "Advanced: white balance"), 14, true),
            Content = s, IsExpanded = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        s.Children.Insert(0, Ui.Text(Loc.T("Die LEDs sind ab Werk rot- und blaustichig. Stell die Regler so ein, dass „Weiß testen“ wirklich weiß aussieht – dann stimmen alle Farben.",
            "The LEDs are red/blue-heavy from the factory. Adjust until “Test white” looks truly white – then all colors are accurate."), 12.5, sub: true, bottom: 12));
        return Ui.Card(wbExpander);
    }
}
