using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClevoControl.App.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class LightingPage(ControlCenter cc) : AppPage(cc)
{
    private KeyboardSettings Kb => Cc.Settings.Keyboard;
    private ContentControl? _options;
    private Border? _preview;
    private System.Windows.Media.Effects.DropShadowEffect? _glow;
    private DispatcherTimer? _timer;

    public static string EffectName(LightingEffectKind e) => e switch
    {
        LightingEffectKind.Static => Loc.T("Statisch", "Static"),
        LightingEffectKind.Breathing => Loc.T("Atmen", "Breathing"),
        LightingEffectKind.Spectrum => Loc.T("Regenbogen", "Rainbow"),
        LightingEffectKind.Gradient => Loc.T("Farbverlauf", "Gradient"),
        LightingEffectKind.Candle => Loc.T("Kerze", "Candle"),
        LightingEffectKind.Daylight => Loc.T("Tageslicht", "Daylight"),
        LightingEffectKind.Ambient => Loc.T("Ambilight", "Ambient light"),
        LightingEffectKind.Music => Loc.T("Musik", "Music"),
        LightingEffectKind.Temperature => Loc.T("Temperatur", "Temperature"),
        LightingEffectKind.Battery => Loc.T("Akkustand", "Battery level"),
        LightingEffectKind.ScreenRegion => Loc.T("Lebensleiste", "Health bar"),
        LightingEffectKind.GameCs2 => "Counter-Strike 2",
        LightingEffectKind.Reactive => Loc.T("Tipp-Reaktiv", "Reactive typing"),
        LightingEffectKind.CpuLoad => Loc.T("CPU-Last", "CPU load"),
        LightingEffectKind.OnAir => "On Air",
        LightingEffectKind.Pomodoro => "Pomodoro",
        LightingEffectKind.Heartbeat => Loc.T("Herzschlag", "Heartbeat"),
        LightingEffectKind.Lava => Loc.T("Lavalampe", "Lava lamp"),
        LightingEffectKind.Fire => Loc.T("Feuer", "Fire"),
        LightingEffectKind.Police => Loc.T("Polizei", "Police"),
        LightingEffectKind.Hardware => Loc.T("Hardware-Effekt", "Hardware effect"),
        LightingEffectKind.Off => Loc.T("Aus", "Off"),
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
        LightingEffectKind.Reactive => (Icons.Keyboard, Loc.T("Leuchtet beim Tippen", "Lights up on keys")),
        LightingEffectKind.CpuLoad => (Icons.Diagnostic, Loc.T("Blau → Rot", "Blue → red")),
        LightingEffectKind.OnAir => (Icons.Broadcast, Loc.T("Rot bei Mikrofon", "Red when mic in use")),
        LightingEffectKind.Pomodoro => (Icons.Timer, Loc.T("Fokus-Timer", "Focus timer")),
        LightingEffectKind.Heartbeat => (Icons.Pulse, Loc.T("Doppelter Puls", "Double pulse")),
        LightingEffectKind.Lava => (Icons.Layers, Loc.T("Langsames Wabern", "Slow drift")),
        LightingEffectKind.Fire => (Icons.Fire, Loc.T("Lodernde Flammen", "Blazing flames")),
        LightingEffectKind.Police => (Icons.Bell, Loc.T("Rot/Blau-Blitzer", "Red/blue strobe")),
        LightingEffectKind.Hardware => (Icons.Chip, Loc.T("Läuft im Controller", "Runs in the EC")),
        LightingEffectKind.Off => (Icons.Power, Loc.T("Beleuchtung aus", "Light off")),
        _ => (Icons.Circle, ""),
    };

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Beleuchtung", "Lighting")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Freie RGB-Farbe, stufenlose Helligkeit und viele Effekte.", "Free RGB color, stepless brightness and lots of effects.")
            + (Cc.Hardware.KeyboardType == KeyboardType.SingleZoneRgb ? Loc.T(" Deine Tastatur hat eine Zone – alle Tasten leuchten in derselben Farbe.", " Your keyboard has one zone – all keys share one color.") : "")));

        // Preview
        _preview = new Border { Height = 70, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 0, 18) };
        _glow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 40, ShadowDepth = 0, Opacity = 0.9 };
        _preview.Effect = _glow;
        root.Children.Add(_preview);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) =>
        {
            var c = Cc.Lighting.KeyboardIsOn ? Cc.Lighting.CurrentColor : RgbColor.Black;
            _preview.Background = c.ToBrush();
            _glow.Color = c.ToWpf();
        };
        _timer.Start();

        // Effects gallery
        var gallery = new WrapPanel();
        foreach (var e in Enum.GetValues<LightingEffectKind>())
        {
            var effect = e;
            var (icon, desc) = EffectInfo(e);
            gallery.Children.Add(Ui.Tile(icon, EffectName(e), desc, Kb.Effect == e, "effect", () =>
            {
                Kb.Effect = effect;
                Kb.Enabled = true;
                Cc.ApplyKeyboard();
                _options!.Content = BuildOptions();
            }, 150));
        }
        root.Children.Add(Ui.Card(gallery, Loc.T("Effekt", "Effect")));

        // Color + options side by side
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var colorCard = BuildColorCard();
        _options = new ContentControl { Content = BuildOptions() };
        var right = new StackPanel();
        right.Children.Add(Ui.Card(Ui.Stack(
            Ui.Switch(Loc.T("Beleuchtung an", "Light on"), Kb.Enabled, on => { Kb.Enabled = on; Cc.ApplyKeyboard(); }),
            Ui.SliderRow(Loc.T("Helligkeit (Hardware)", "Brightness (hardware)"), 0, 100, Kb.Brightness, v => { Kb.Brightness = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} %"),
            Ui.Row(Loc.T("Aus bei Inaktivität", "Off when idle"),
                Ui.Combo(new[] { 0, 1, 2, 5, 10, 15, 30, 60 }.Select(m => (m, m == 0 ? Loc.T("nie", "never") : $"{m} min")), Kb.IdleTimeoutMinutes,
                    m => { Kb.IdleTimeoutMinutes = m; Cc.ApplyKeyboard(); }, 120),
                Loc.T("Geht beim nächsten Tastendruck wieder an", "Turns back on with the next key press"), 0)
        ), Loc.T("Allgemein", "General")));
        right.Children.Add(_options);
        Grid.SetColumn(right, 2);
        grid.Children.Add(colorCard);
        grid.Children.Add(right);
        root.Children.Add(grid);

        root.Children.Add(BuildWhiteBalance());
        return root;
    }

    public override void OnLeave() => _timer?.Stop();

    private Border BuildColorCard()
    {
        var picker = new ColorPicker(Kb.Color);
        picker.ColorChanged += c =>
        {
            Kb.Color = c;
            if (Kb.Effect == LightingEffectKind.Off) Kb.Effect = LightingEffectKind.Static;
            Cc.ApplyKeyboard();
        };

        var favorites = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        void FillFavorites()
        {
            favorites.Children.Clear();
            foreach (var f in Kb.Favorites.ToList())
            {
                var col = f;
                var sw = Ui.Swatch(col, 28, () => { picker.SetColor(col); Kb.Color = col; Cc.ApplyKeyboard(); },
                    col.ToHex() + Loc.T(" · Rechtsklick: entfernen", " · right-click: remove"));
                sw.MouseRightButtonUp += (_, _) => { Kb.Favorites.Remove(col); Cc.ApplyKeyboard(); FillFavorites(); };
                favorites.Children.Add(sw);
            }
            var add = new Button { Content = "+", Width = 28, Height = 28, Padding = new Thickness(0), ToolTip = Loc.T("Aktuelle Farbe merken", "Save current color") };
            add.Click += (_, _) => { if (!Kb.Favorites.Contains(Kb.Color)) Kb.Favorites.Add(Kb.Color); Cc.ApplyKeyboard(); FillFavorites(); };
            favorites.Children.Add(add);
        }
        FillFavorites();

        var secondary = new ColorPicker(Kb.SecondaryColor);
        secondary.ColorChanged += c => { Kb.SecondaryColor = c; Cc.ApplyKeyboard(); };
        var expander = new Expander
        {
            Header = Ui.Text(Loc.T("Zweitfarbe (Farbverlauf, Tipp-Reaktiv)", "Second color (gradient, reactive)"), 13, sub: true),
            Content = secondary,
            Margin = new Thickness(0, 12, 0, 0),

        };
        return Ui.Card(Ui.Stack(picker, favorites, expander), Loc.T("Farbe", "Color"));
    }

    private UIElement BuildOptions()
    {
        var s = new StackPanel();
        FrameworkElement Speed() => Ui.SliderRow(Loc.T("Geschwindigkeit", "Speed"), 0.1, 5, Kb.Speed, v => { Kb.Speed = v; Cc.ApplyKeyboard(); }, v => $"{v:0.0}×", 0.1);

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
                s.Children.Add(Speed());
                if (Kb.Effect == LightingEffectKind.Reactive)
                    s.Children.Add(Ui.Text(Loc.T("Grundfarbe = Farbe (gedimmt), Aufleuchten = Zweitfarbe. Sind beide gleich, wechselt die Farbe bei jedem Tastendruck.",
                        "Base = color (dimmed), flash = second color. If both are equal, the color changes with every key press."), 12, sub: true));
                break;

            case LightingEffectKind.Ambient:
            {
                var monitors = ScreenSampler.GetMonitors();
                s.Children.Add(Ui.Row(Loc.T("Bildschirm", "Monitor"), Ui.Combo(monitors.Select(m => (m.Index, $"{m.Index + 1}: {m.Width}×{m.Height}{(m.Primary ? " ★" : "")}")),
                    Kb.AmbientMonitor, i => { Kb.AmbientMonitor = i; Cc.ApplyKeyboard(); }, 180)));
                s.Children.Add(Ui.SliderRow(Loc.T("Farbverstärkung", "Saturation boost"), 0.5, 3, Kb.AmbientSaturationBoost, v => { Kb.AmbientSaturationBoost = v; Cc.ApplyKeyboard(); }, v => $"{v:0.0}×", 0.1));
                s.Children.Add(Ui.SliderRow(Loc.T("Glättung", "Smoothing"), 0, 0.95, Kb.AmbientSmoothing, v => { Kb.AmbientSmoothing = v; Cc.ApplyKeyboard(); }, v => $"{v * 100:0} %", 0.05));
                s.Children.Add(Ui.SliderRow("FPS", 5, 60, Kb.AmbientFps, v => { Kb.AmbientFps = (int)v; Cc.ApplyKeyboard(); }));
                s.Children.Add(Ui.Text(Loc.T("Hinweis: Kopiergeschützte Streams (z. B. Netflix in Edge) zeigen der Bildschirmaufnahme oft nur Schwarz.",
                    "Note: copy-protected streams (e.g. Netflix in Edge) often appear black to screen capture."), 12, sub: true));
                break;
            }

            case LightingEffectKind.Music:
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
                s.Children.Add(Ui.Text(Loc.T("Einstellungen dazu findest du unter „Spiele & Apps“.", "You find the settings under “Games & apps”."), 12.5, sub: true, bottom: 10));
                s.Children.Add(Ui.Button(Loc.T("Zu Spiele & Apps", "Open Games & apps"), () => ((MainWindow)Application.Current.MainWindow!).Navigate("games")));
                break;

            case LightingEffectKind.Hardware:
                s.Children.Add(Ui.Row(Loc.T("Effekt", "Effect"), Ui.Combo(Enum.GetValues<HardwareEffect>().Select(h => (h, HardwareEffectName(h))), Kb.HardwareEffect,
                    h => { Kb.HardwareEffect = h; Cc.ApplyKeyboard(); }, 180)));
                s.Children.Add(Ui.Text(Loc.T("Diese Effekte berechnet der Tastatur-Controller selbst – null CPU-Last, laufen sogar ohne die App weiter. Geschwindigkeit und Farben legt die Firmware fest.",
                    "These effects run inside the keyboard controller – zero CPU load, they even keep running without the app. Speed and colors are fixed by the firmware."), 12, sub: true));
                break;

            default:
                return new ContentControl();
        }
        return Ui.Card(s, Loc.T("Effekt-Optionen", "Effect options"));
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
            Ui.Button(Loc.T("Standard", "Default"), () => { Kb.GainRed = 0.71; Kb.GainGreen = 1; Kb.GainBlue = 0.78; Cc.ApplyKeyboard(); ((MainWindow)Application.Current.MainWindow!).Navigate("lighting"); }),
            Ui.Button(Loc.T("Aus (100 %)", "Off (100 %)"), () => { Kb.GainRed = 1; Kb.GainGreen = 1; Kb.GainBlue = 1; Cc.ApplyKeyboard(); ((MainWindow)Application.Current.MainWindow!).Navigate("lighting"); })));
        return Ui.Card(s, Loc.T("Weißabgleich", "White balance"),
            Loc.T("Die LEDs sind ab Werk rot- und blaustichig. Stell die Regler so ein, dass „Weiß testen“ wirklich weiß aussieht – dann stimmen alle Farben.",
                  "The LEDs are red/blue-heavy from the factory. Adjust until “Test white” looks truly white – then all colors are accurate."));
    }
}
