using System.Windows;
using System.Windows.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class PowerPage(ControlCenter cc) : AppPage(cc)
{
    private PowerSettings P => Cc.Settings.Power;

    /// <summary>Profiles ordered from least to most power (used for tiles, combos and the quick tile).</summary>
    public static readonly PerformanceProfile[] ProfileOrder =
        [PerformanceProfile.PowerSaving, PerformanceProfile.Quiet, PerformanceProfile.Entertainment, PerformanceProfile.Performance];

    public static string ProfileName(PerformanceProfile p) => p switch
    {
        PerformanceProfile.Quiet => Loc.T("Leise", "Quiet"),
        PerformanceProfile.PowerSaving => Loc.T("Stromsparen", "Power saving"),
        PerformanceProfile.Performance => Loc.T("Maximale Leistung", "Max performance"),
        PerformanceProfile.Entertainment => Loc.T("Ausgewogen", "Balanced"),
        _ => p.ToString(),
    };

    public static IEnumerable<(PerformanceProfile, string)> ProfileItems() => ProfileOrder.Select(p => (p, ProfileName(p)));

    // Tiles are updated in place after a change (ApplyPower may switch the profile and the Windows power mode).
    private readonly List<(PerformanceProfile Profile, RadioButton Tile)> _profileTiles = [];
    private readonly List<(WindowsPowerMode Mode, RadioButton Tile)> _modeTiles = [];
    private bool _syncing;
    private Border? _autoInfo;

    public override UIElement Build()
    {
        _profileTiles.Clear();
        _modeTiles.Clear();
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Leistung", "Performance")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Leistungsprofil, Windows-Energiemodus und CPU-Drosselung.", "Performance profile, Windows power mode and CPU throttling.")));
        root.Children.Add(ProfileCard());
        root.Children.Add(ThrottleCard());
        root.Children.Add(AutoSwitchCard());
        // The AC/battery switch changes the profile in the background: keep the tiles in step.
        Cc.StatusChanged += OnStatusChanged;
        return root;
    }

    public override void OnLeave() => Cc.StatusChanged -= OnStatusChanged;

    private void OnStatusChanged() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(SyncTiles);

    /// <summary>Selects the tiles that match the current profile and Windows power mode, without running their actions.</summary>
    private void SyncTiles()
    {
        _syncing = true;
        try
        {
            foreach (var (p, t) in _profileTiles) t.IsChecked = P.Profile == p;
            if (_modeTiles.Count > 0 && PowerService.GetWindowsPowerMode() is { } mode)
                foreach (var (m, t) in _modeTiles) t.IsChecked = m == mode;
        }
        finally { _syncing = false; }
    }

    private void UpdateAutoInfo()
    {
        if (_autoInfo is null) return;
        _autoInfo.Visibility = P.AutoSwitch ? Visibility.Visible : Visibility.Collapsed;
        _autoInfo.Child = Ui.InfoBar(Loc.T(
            $"Wird beim Ein-/Ausstecken automatisch umgestellt (Netzteil: {ProfileName(P.ProfileOnAc)}, Akku: {ProfileName(P.ProfileOnBattery)})",
            $"Switches automatically when you plug in or unplug (charger: {ProfileName(P.ProfileOnAc)}, battery: {ProfileName(P.ProfileOnBattery)})"));
    }

    private Border ProfileCard()
    {
        _autoInfo = new Border();
        UpdateAutoInfo();

        var profiles = Ui.TileGrid(160, 8);
        foreach (var p in ProfileOrder)
        {
            var prof = p;
            var (icon, desc) = p switch
            {
                PerformanceProfile.PowerSaving => (Icons.BatterySaver, Loc.T("Längste Akkulaufzeit", "Longest battery life")),
                PerformanceProfile.Quiet => (Icons.Moon, Loc.T("Leise Lüfter, weniger Leistung", "Quiet fans, less power")),
                PerformanceProfile.Performance => (Icons.Lightning, Loc.T("Volle Leistung, lautere Lüfter", "Full power, louder fans")),
                _ => (Icons.Tune, Loc.T("Standard für Alltag und Filme", "Default for everyday use and video")),
            };
            var tile = Ui.Tile(icon, ProfileName(p), desc, P.Profile == p, "profile", () =>
            {
                if (_syncing) return;
                P.Profile = prof;
                Cc.ApplyPower();
                SyncTiles();
            }, 205);
            _profileTiles.Add((p, tile));
            profiles.Children.Add(tile);
        }

        // Windows power mode lives in the same card: it is either derived from the profile or chosen by hand.
        var current = PowerService.GetWindowsPowerMode();
        var win = Ui.TileGrid(200, 8);
        win.Margin = new Thickness(0, 6, 0, 8);
        foreach (var (mode, title, icon) in new[]
                 {
                     (WindowsPowerMode.BestEfficiency, Loc.T("Beste Energieeffizienz", "Best power efficiency"), Icons.BatterySaver),
                     // "Balanced" is also a profile name in English: name the Windows mode explicitly.
                     (WindowsPowerMode.Balanced, Loc.T("Ausbalanciert", "Windows: Balanced"), Icons.Tune),
                     (WindowsPowerMode.BestPerformance, Loc.T("Beste Leistung", "Best performance"), Icons.Lightning),
                 })
        {
            var m = mode;
            var tile = Ui.Tile(icon, title, null, current == mode, "winmode", () => { if (!_syncing) PowerService.SetWindowsPowerMode(m); }, 205);
            _modeTiles.Add((mode, tile));
            win.Children.Add(tile);
        }
        win.IsEnabled = !P.SyncWindowsPowerMode;
        var hint = Ui.Text(Loc.T("Wird vom Leistungsprofil gesteuert. Schalte „Mitschalten“ aus, um selbst zu wählen.",
            "Controlled by the performance profile. Turn off “Switch too” to choose yourself."), 12, sub: true, bottom: 4);
        hint.Visibility = P.SyncWindowsPowerMode ? Visibility.Visible : Visibility.Collapsed;

        var sync = Ui.Switch(Loc.T("Windows-Energiemodus mitschalten", "Switch Windows power mode too"), P.SyncWindowsPowerMode, v =>
        {
            P.SyncWindowsPowerMode = v;
            win.IsEnabled = !v;
            hint.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            Cc.ApplyPower();
            SyncTiles();
        }, Loc.T("Stromsparen und Leise → Beste Energieeffizienz · Maximale Leistung → Beste Leistung",
                 "Power saving and Quiet → best power efficiency · Max performance → best performance"));

        var s = Ui.Stack(_autoInfo, profiles, new Border { Height = 8 }, sync, Ui.Text(Loc.T("Windows-Energiemodus", "Windows power mode"), 13, true, bottom: 2), hint, win);
        return Ui.Card(s, Loc.T("Leistungsprofil", "Performance profile"), Loc.T("Wie viel Strom CPU und GPU bekommen.", "How much power CPU and GPU get."), Icons.Lightning, indent: false);
    }

    private Border ThrottleCard()
    {
        var c = Cc.Settings.Cooling;
        var current = CoolingService.Detect();
        var tiles = Ui.TileGrid(200, 8);
        foreach (var (mode, icon, title, desc) in new[]
                 {
                     (CoolingMode.Normal, Icons.Auto, Loc.T("Normal", "Normal"), Loc.T("Turbo an, volle Leistung", "Turbo on, full power")),
                     (CoolingMode.Cool, Icons.Thermometer, Loc.T("Kühl", "Cool"), Loc.T("Kein Turbo · meist 10–20 °C kühler, etwas weniger FPS", "No turbo · usually 10–20 °C cooler, slightly lower FPS")),
                     (CoolingMode.VeryCool, Icons.Moon, Loc.T("Sehr kühl", "Very cool"), Loc.T("Max. 80 % Takt · deutlich weniger Leistung", "Max. 80 % clock · noticeably less performance")),
                 })
        {
            var m = mode;
            tiles.Children.Add(Ui.Tile(icon, title, desc, current == mode, "throttle", () =>
            {
                c.Mode = m;
                if (!Cc.ApplyCooling())
                    MessageBox.Show(Loc.T("Windows hat die Änderung abgelehnt (vielleicht verwaltet eine Richtlinie den Energiesparplan).",
                        "Windows refused the change (a policy may manage the power plan)."), "ClevoControl");
            }, 280));
        }
        return Ui.Card(tiles, Loc.T("Prozessor-Drosselung", "CPU throttling"),
            Loc.T("Senkt Temperatur und Lüfterlärm, indem Windows den Prozessor-Turbo begrenzt. Kein Treiber nötig, jederzeit umkehrbar – „Normal“ stellt deine ursprünglichen Werte wieder her.",
                  "Lowers temperature and fan noise by limiting the CPU turbo in Windows. No driver needed, reversible any time – “Normal” restores your original values."),
            Icons.Thermometer, indent: false);
    }

    private Border AutoSwitchCard()
    {
        var auto = new StackPanel();
        var rates = DisplayService.GetRefreshRates();
        var showRates = rates.Count > 1;
        Grid? acRow = null, batteryRow = null;
        auto.Children.Add(Ui.Switch(Loc.T("Automatisch umschalten", "Switch automatically"), P.AutoSwitch, v =>
            {
                P.AutoSwitch = v;
                acRow!.IsEnabled = batteryRow!.IsEnabled = v;
                Cc.ApplyPower();
                UpdateAutoInfo();
                SyncTiles();
            },
            Loc.T("Wechselt das Profil, sobald du das Netzteil ein- oder aussteckst.", "Changes the profile when you plug in or unplug the charger.")));
        acRow = Ui.Row(Loc.T("Am Netzteil", "Plugged in"), Ui.Combo(ProfileItems(), P.ProfileOnAc, p => { P.ProfileOnAc = p; Cc.ApplyPower(); UpdateAutoInfo(); SyncTiles(); }));
        batteryRow = Ui.Row(Loc.T("Im Akkubetrieb", "On battery"), Ui.Combo(ProfileItems(), P.ProfileOnBattery, p => { P.ProfileOnBattery = p; Cc.ApplyPower(); UpdateAutoInfo(); SyncTiles(); }),
            bottom: showRates ? 14 : 0);
        acRow.IsEnabled = batteryRow.IsEnabled = P.AutoSwitch;
        auto.Children.Add(acRow);
        auto.Children.Add(batteryRow);

        // Refresh rate: the biggest battery saver on high-Hz laptop panels. Hidden when there is nothing to choose.
        if (showRates)
        {
            auto.Children.Add(Ui.Row(Loc.T("Bildwiederholrate jetzt", "Refresh rate now"), Ui.Combo(rates.Select(r => (r, $"{r} Hz")), DisplayService.GetRefreshRate(),
                r => DisplayService.SetRefreshRate(r), 120)));
            auto.Children.Add(Ui.Switch(Loc.T("Im Akkubetrieb automatisch senken", "Lower automatically on battery"), P.LowRefreshOnBattery,
                v => { P.LowRefreshOnBattery = v; Cc.ApplyPower(); }, Loc.T("Spart spürbar Strom. Am Netzteil wieder die höchste Rate.", "Saves noticeable power. Highest rate again when plugged in.")));
            auto.Children.Add(Ui.Row(Loc.T("Rate im Akkubetrieb", "Rate on battery"), Ui.Combo(rates.Select(r => (r, $"{r} Hz")), rates.OrderBy(r => Math.Abs(r - P.BatteryRefreshRate)).First(),
                r => { P.BatteryRefreshRate = r; Cc.ApplyPower(); }, 120), bottom: 0));
        }
        return Ui.Card(auto, Loc.T("Akku und Netzteil", "Battery and charger"), icon: Icons.Plug);
    }
}
