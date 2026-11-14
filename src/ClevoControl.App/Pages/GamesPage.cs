using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClevoControl.App.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Games;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class GamesPage(ControlCenter cc) : AppPage(cc)
{
    private DispatcherTimer? _timer;
    private readonly List<Action> _updaters = [];
    private KeyboardSettings Kb => Cc.Settings.Keyboard;
    private GameSettings G => Cc.Settings.Games;

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Spiele & Integrationen", "Games & integrations")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Die Tastatur zeigt Leben, Mana & Co. – oder andere Programme steuern sie.", "The keyboard shows health, mana & co. – or other programs control it.")));

        root.Children.Add(RegionCard());
        root.Children.Add(Cs2Card());
        root.Children.Add(HealthDisplayCard());
        root.Children.Add(OpenRgbCard());
        root.Children.Add(ApiCard());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => { foreach (var u in _updaters) u(); };
        _timer.Start();
        return root;
    }

    public override void OnLeave() => _timer?.Stop();

    private void Activate(LightingEffectKind effect)
    {
        Kb.Effect = effect;
        Kb.Enabled = true;
        Cc.ApplyKeyboard();
    }

    private Border HealthDisplayCard()
    {
        var combo = Ui.Combo(new[]
        {
            (HealthDisplay.Hue, Loc.T("Farbton (grün → gelb → rot)", "Hue (green → yellow → red)")),
            (HealthDisplay.Brightness, Loc.T("Helligkeit (deine Farbe, dunkler bei wenig Leben)", "Brightness (your color, darker at low health)")),
            (HealthDisplay.Both, Loc.T("Farbton + Helligkeit", "Hue + brightness")),
        }, Kb.HealthDisplay, h => { Kb.HealthDisplay = h; Cc.ApplyKeyboard(); }, 330);
        return Ui.Card(Ui.Stack(Ui.Row(Loc.T("Darstellung", "Display"), combo, Loc.T("Gilt für Lebensleiste und CS2. Bei wenig Leben pulsiert die Tastatur zusätzlich.", "Applies to health bar and CS2. At low health the keyboard pulses as well."), 0)),
            Loc.T("Darstellung der Lebensanzeige", "Health display style"), icon: Icons.Heart);
    }

    private Border RegionCard()
    {
        var r = Kb.Region;
        var s = new StackPanel();
        var status = Ui.Text("", 12.5, sub: true, bottom: 10);
        var fillBar = new ProgressBar { Height = 8, Maximum = 1, Margin = new Thickness(0, 0, 0, 14), Background = new SolidColorBrush(Color.FromRgb(50, 56, 71)), BorderThickness = new Thickness(0) };
        var barColor = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = r.BarColor.ToBrush(), Margin = new Thickness(8, 0, 0, 0) };
        _updaters.Add(() =>
        {
            var active = Kb.Effect == LightingEffectKind.ScreenRegion;
            var fill = Cc.Lighting.LastRegionFill;
            status.Text = !active
                ? Loc.T("Nicht aktiv.", "Not active.")
                : r.Mode == RegionMode.AreaColor ? Loc.T("Aktiv – Tastatur übernimmt die Farbe des Bereichs.", "Active – keyboard takes the color of the area.")
                : fill is { } f ? Loc.T($"Aktiv – erkannter Füllstand: {f * 100:0} %", $"Active – detected fill: {f * 100:0} %") : Loc.T("Aktiv – warte auf Bild …", "Active – waiting for image …");
            fillBar.Value = active ? fill ?? 0 : 0;
            fillBar.Foreground = Cc.Lighting.CurrentColor.ToBrush();
        });

        s.Children.Add(Ui.Text(Loc.T("1. Spiel starten (Fenster- oder randloser Vollbildmodus funktioniert am zuverlässigsten).\n2. „Bereich auswählen“ und einen Rahmen eng um die volle Lebens- oder Manaleiste ziehen.\n3. Fertig – die Farbe der vollen Leiste wird automatisch gelernt.",
            "1. Start the game (windowed or borderless fullscreen works best).\n2. Click “Select area” and drag a tight box around the full health or mana bar.\n3. Done – the color of the full bar is learned automatically."), 12.5, sub: true, bottom: 12));

        var monitors = ScreenSampler.GetMonitors();
        s.Children.Add(Ui.Row(Loc.T("Bildschirm", "Monitor"), Ui.Combo(monitors.Select(m => (m.Index, $"{m.Index + 1}: {m.Width}×{m.Height}{(m.Primary ? " ★" : "")}")), r.Monitor,
            i => { r.Monitor = i; Cc.ApplyKeyboard(); }, 180)));

        var select = Ui.Button(Loc.T("Bereich auswählen …", "Select area …"), () =>
        {
            var mons = ScreenSampler.GetMonitors();
            var mon = mons.ElementAtOrDefault(r.Monitor) ?? mons.FirstOrDefault(m => m.Primary) ?? mons.First();
            var main = Application.Current.MainWindow!;
            main.Hide();
            Thread.Sleep(250);
            var w = new RegionSelectWindow(mon);
            w.ShowDialog();
            if (w.Result is { } rect)
            {
                r.X = rect.X; r.Y = rect.Y; r.Width = rect.Width; r.Height = rect.Height;
                using var sampler = new RegionSampler();
                if (sampler.DominantColor(mon, r) is { } c) r.BarColor = c;
                barColor.Background = r.BarColor.ToBrush();
                Activate(LightingEffectKind.ScreenRegion);
            }
            main.Show();
            main.Activate();
        }, accent: true);
        var learn = Ui.Button(Loc.T("Leistenfarbe neu lernen", "Re-learn bar color"), () =>
        {
            var mons = ScreenSampler.GetMonitors();
            var mon = mons.ElementAtOrDefault(r.Monitor) ?? mons.First();
            using var sampler = new RegionSampler();
            if (sampler.DominantColor(mon, r) is { } c) { r.BarColor = c; barColor.Background = c.ToBrush(); Cc.ApplyKeyboard(); }
        });
        var activate = Ui.Button(Loc.T("Effekt aktivieren", "Activate effect"), () => Activate(LightingEffectKind.ScreenRegion));
        var buttons = Ui.Wrap(select, learn, activate);
        s.Children.Add(buttons);

        var colorRow = new StackPanel { Orientation = Orientation.Horizontal };
        colorRow.Children.Add(Ui.Text(Loc.T("Gelernte Leistenfarbe", "Learned bar color"), 12.5, sub: true));
        colorRow.Children.Add(barColor);
        colorRow.Margin = new Thickness(0, 4, 0, 14);
        s.Children.Add(colorRow);

        s.Children.Add(Ui.Row(Loc.T("Erkennen", "Detect"), Ui.Combo(new[]
        {
            (RegionMode.BarFill, Loc.T("Füllstand einer Leiste", "Fill level of a bar")),
            (RegionMode.AreaColor, Loc.T("Farbe des Bereichs übernehmen", "Copy the area's color")),
        }, r.Mode, m => { r.Mode = m; Cc.ApplyKeyboard(); }, 240)));
        s.Children.Add(Ui.Switch(Loc.T("Senkrechte Leiste", "Vertical bar"), r.VerticalBar, v => { r.VerticalBar = v; Cc.ApplyKeyboard(); }, Loc.T("Füllt sich von unten nach oben", "Fills from bottom to top")));
        s.Children.Add(Ui.SliderRow(Loc.T("Farbtoleranz", "Color tolerance"), 0.05, 0.8, r.Tolerance, v => { r.Tolerance = v; Cc.ApplyKeyboard(); }, v => $"{v * 100:0} %", 0.01));
        s.Children.Add(Ui.SliderRow(Loc.T("Pulsieren unter", "Pulse below"), 0, 60, r.WarnBelowPercent, v => { r.WarnBelowPercent = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} %"));

        var fullPick = new ColorPicker(r.FullColor);
        fullPick.ColorChanged += c => { r.FullColor = c; Cc.ApplyKeyboard(); };
        var emptyPick = new ColorPicker(r.EmptyColor);
        emptyPick.ColorChanged += c => { r.EmptyColor = c; Cc.ApplyKeyboard(); };
        s.Children.Add(new Expander
        {
            Header = Ui.Text(Loc.T("Farben für voll/leer anpassen (z. B. Mana: blau → lila)", "Customize full/empty colors (e.g. mana: blue → purple)"), 13, sub: true),
            Content = Ui.Stack(Ui.Text(Loc.T("Voll", "Full"), 12.5, true, bottom: 6), fullPick, Ui.Text(Loc.T("Leer", "Empty"), 12.5, true, bottom: 6), emptyPick),
            Margin = new Thickness(0, 0, 0, 14),
        });
        s.Children.Add(fillBar);
        s.Children.Add(status);
        return Ui.Card(s, Loc.T("Lebensleiste vom Bildschirm (jedes Spiel)", "Health bar from screen (any game)"), icon: Icons.Heart, subtitle:
            Loc.T("Liest nur Pixel vom Bildschirm – kein Eingriff ins Spiel, kein Anti-Cheat-Risiko.", "Only reads screen pixels – no game modification, no anti-cheat risk."));
    }

    private Border Cs2Card()
    {
        var s = new StackPanel();
        var status = Ui.Text("", 12.5, sub: true, bottom: 10);
        _updaters.Add(() =>
        {
            var st = Cc.Cs2.Latest;
            status.Text = Kb.Effect != LightingEffectKind.GameCs2
                ? Loc.T("Nicht aktiv.", "Not active.")
                : Cc.Cs2.Error is { } err ? Loc.T("Fehler: ", "Error: ") + err
                : st is null || DateTime.UtcNow - st.Received > TimeSpan.FromSeconds(20)
                    ? Loc.T($"Warte auf CS2 (Port {G.Cs2Port}) …", $"Waiting for CS2 (port {G.Cs2Port}) …")
                    : Loc.T($"Verbunden – Leben {st.Health}, Rüstung {st.Armor}", $"Connected – health {st.Health}, armor {st.Armor}") + (st.Bomb is { } b ? Loc.T($", Bombe: {b}", $", Bomb: {b}") : "");
        });
        s.Children.Add(Ui.Text(Loc.T("Nutzt Valves offizielle „Game State Integration“: CS2 meldet Leben, Blendung, Feuer und Bombe selbst an die App. Kein Eingriff ins Spiel, VAC-sicher.",
            "Uses Valve's official “Game State Integration”: CS2 reports health, flash, fire and bomb to the app itself. No game modification, VAC-safe."), 12.5, sub: true, bottom: 12));
        var install = Ui.Button(Loc.T("CS2-Konfiguration installieren", "Install CS2 config"), () =>
        {
            try
            {
                var path = Cs2Integration.InstallConfig(G.Cs2Port);
                MessageBox.Show(path is null
                        ? Loc.T("CS2 wurde in keiner Steam-Bibliothek gefunden.", "CS2 was not found in any Steam library.")
                        : Loc.T("Konfiguration geschrieben:\n", "Config written:\n") + path + Loc.T("\n\nStarte CS2 neu, damit sie geladen wird.", "\n\nRestart CS2 so it gets loaded."),
                    "ClevoControl");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "ClevoControl"); }
        }, accent: true);
        s.Children.Add(Ui.Wrap(install, Ui.Button(Loc.T("Effekt aktivieren", "Activate effect"), () => Activate(LightingEffectKind.GameCs2))));
        s.Children.Add(Ui.Text(Loc.T("Grün→Rot = Leben · Weiß = geblendet · Orange = brennt · Rotes Blinken = Bombe gelegt",
            "Green→red = health · white = flashed · orange = burning · red blinking = bomb planted"), 12, sub: true, bottom: 8));
        s.Children.Add(status);
        return Ui.Card(s, "Counter-Strike 2", icon: Icons.Target);
    }

    private Border OpenRgbCard()
    {
        var s = new StackPanel();
        var status = Ui.Text("", 12.5, sub: true);
        _updaters.Add(() => status.Text = !G.OpenRgbServerEnabled ? "" :
            Cc.IntegrationError is { } e && e.StartsWith("OpenRGB") ? e :
            Loc.T($"Läuft auf 127.0.0.1:{G.OpenRgbPort} · {Cc.OpenRgbClients} Client(s) verbunden", $"Running on 127.0.0.1:{G.OpenRgbPort} · {Cc.OpenRgbClients} client(s) connected"));
        s.Children.Add(Ui.Switch(Loc.T("OpenRGB-Server aktivieren", "Enable OpenRGB server"), G.OpenRgbServerEnabled, on => { G.OpenRgbServerEnabled = on; Cc.ApplyKeyboard(); },
            Loc.T("Deine Tastatur erscheint als Gerät in Artemis, Aurora & Co.","Your keyboard shows up as a device in Artemis, Aurora & co.")));
        s.Children.Add(Ui.Row(Loc.T("Port", "Port"), PortBox(G.OpenRgbPort, p => { G.OpenRgbPort = p; Cc.ApplyKeyboard(); }), Loc.T("Standard 6742. Läuft das echte OpenRGB parallel, einen anderen Port nehmen.", "Default 6742. If the real OpenRGB is running too, pick another port.")));
        s.Children.Add(Ui.SliderRow(Loc.T("Zurück zu deinem Effekt nach", "Return to your effect after"), 1, 30, G.ExternalHoldSeconds, v => { G.ExternalHoldSeconds = (int)v; Cc.ApplyKeyboard(); }, v => $"{v:0} s"));
        s.Children.Add(Ui.Text(Loc.T("So geht's: In Artemis/Aurora ein „OpenRGB“-Gerät hinzufügen (Host 127.0.0.1, obiger Port). Deren Spiel-Plugins (Dota 2, GTA V, Minecraft, LoL, Euro Truck Simulator …) steuern dann deine Tastatur. Sendet niemand mehr, läuft wieder dein eigener Effekt.",
            "How to: add an “OpenRGB” device in Artemis/Aurora (host 127.0.0.1, port above). Their game plugins (Dota 2, GTA V, Minecraft, LoL, Euro Truck Simulator …) then control your keyboard. When nobody sends anymore, your own effect resumes."), 12, sub: true, bottom: 8));
        s.Children.Add(status);
        return Ui.Card(s, Loc.T("Andere RGB-Programme (OpenRGB-Protokoll)", "Other RGB software (OpenRGB protocol)"), icon: Icons.Plug);
    }

    private Border ApiCard()
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Switch(Loc.T("Lokale HTTP-Schnittstelle", "Local HTTP API"), G.LocalApiEnabled, on => { G.LocalApiEnabled = on; Cc.ApplyKeyboard(); },
            Loc.T("Nur von diesem PC erreichbar (127.0.0.1). Für eigene Skripte, Stream Deck, Home Assistant, Mods …", "Only reachable from this PC (127.0.0.1). For your own scripts, Stream Deck, Home Assistant, mods …")));
        s.Children.Add(Ui.Row(Loc.T("Port", "Port"), PortBox(G.LocalApiPort, p => { G.LocalApiPort = p; Cc.ApplyKeyboard(); })));
        var example = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Text = $"curl -X POST http://127.0.0.1:{G.LocalApiPort}/color -d \"{{\\\"color\\\":\\\"#ff0000\\\",\\\"seconds\\\":3}}\"\n" +
                   $"curl -X POST http://127.0.0.1:{G.LocalApiPort}/scene -d \"{{\\\"name\\\":\\\"Film\\\"}}\"\n" +
                   $"curl -X POST http://127.0.0.1:{G.LocalApiPort}/effect -d \"{{\\\"effect\\\":\\\"Spectrum\\\"}}\"\n" +
                   $"curl http://127.0.0.1:{G.LocalApiPort}/status",
        };
        s.Children.Add(example);
        return Ui.Card(s, Loc.T("Eigene Skripte & Mods", "Your own scripts & mods"), icon: Icons.Code);
    }

    private static StackPanel PortBox(int value, Action<int> changed)
    {
        var t = new TextBox { Text = value.ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Right };
        var err = Ui.Text(Loc.T("Port muss zwischen 1025 und 65535 liegen.", "Port must be between 1025 and 65535."), 12);
        err.Foreground = new SolidColorBrush(Color.FromRgb(232, 90, 80));
        err.Visibility = Visibility.Collapsed;
        err.MaxWidth = 220;
        err.HorizontalAlignment = HorizontalAlignment.Right;
        err.Margin = new Thickness(0, 4, 0, 0);
        t.LostFocus += (_, _) =>
        {
            if (int.TryParse(t.Text, out var p) && p is > 1024 and < 65536)
            {
                err.Visibility = Visibility.Collapsed;
                value = p;
                changed(p);
            }
            else
            {
                err.Visibility = Visibility.Visible;
                t.ToolTip = err.Text;
                t.Text = value.ToString();
            }
        };
        return Ui.Stack(t, err);
    }
}
