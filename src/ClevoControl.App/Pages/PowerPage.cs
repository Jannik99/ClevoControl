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

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Leistung", "Performance")));
        root.Children.Add(ProfileCard());
        root.Children.Add(ThrottleCard());
        root.Children.Add(AutoSwitchCard());
        return root;
    }

    private Border ProfileCard()
    {
        var profiles = new WrapPanel();
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
            profiles.Children.Add(Ui.Tile(icon, ProfileName(p), desc, P.Profile == p, "profile", () => { P.Profile = prof; Cc.ApplyPower(); Refresh(); }, 205));
        }

        // Windows power mode lives in the same card: it is either derived from the profile or chosen by hand.
        var current = PowerService.GetWindowsPowerMode();
        var win = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (mode, title, icon) in new[]
                 {
                     (WindowsPowerMode.BestEfficiency, Loc.T("Beste Energieeffizienz", "Best power efficiency"), Icons.BatterySaver),
                     (WindowsPowerMode.Balanced, Loc.T("Ausbalanciert", "Balanced"), Icons.Tune),
                     (WindowsPowerMode.BestPerformance, Loc.T("Beste Leistung", "Best performance"), Icons.Lightning),
                 })
        {
            var m = mode;
            win.Children.Add(Ui.Tile(icon, title, null, current == mode, "winmode", () => PowerService.SetWindowsPowerMode(m), 205));
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
            Refresh();
        }, Loc.T("Stromsparen und Leise → Beste Energieeffizienz · Maximale Leistung → Beste Leistung",
                 "Power saving and Quiet → best power efficiency · Max performance → best performance"));

        var s = Ui.Stack(profiles, new Border { Height = 8 }, sync, Ui.Text(Loc.T("Windows-Energiemodus", "Windows power mode"), 13, true, bottom: 2), hint, win);
        return Ui.Card(s, Loc.T("Leistungsprofil", "Performance profile"), Loc.T("Wie viel Strom CPU und GPU bekommen.", "How much power CPU and GPU get."), Icons.Lightning);
    }

    private Border ThrottleCard()
    {
        var c = Cc.Settings.Cooling;
        var current = CoolingService.Detect();
        var tiles = new WrapPanel();
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
            Icons.Thermometer);
    }

    private Border AutoSwitchCard()
    {
        var auto = new StackPanel();
        auto.Children.Add(Ui.Switch(Loc.T("Automatisch umschalten", "Switch automatically"), P.AutoSwitch, v => { P.AutoSwitch = v; Cc.ApplyPower(); Refresh(); },
            Loc.T("Wechselt das Profil, sobald du das Netzteil ein- oder aussteckst.", "Changes the profile when you plug in or unplug the charger.")));
        auto.Children.Add(Ui.Row(Loc.T("Am Netzteil", "Plugged in"), Ui.Combo(ProfileItems(), P.ProfileOnAc, p => { P.ProfileOnAc = p; Cc.ApplyPower(); })));
        auto.Children.Add(Ui.Row(Loc.T("Im Akkubetrieb", "On battery"), Ui.Combo(ProfileItems(), P.ProfileOnBattery, p => { P.ProfileOnBattery = p; Cc.ApplyPower(); }), bottom: 0));
        return Ui.Card(auto, Loc.T("Akku und Netzteil", "Battery and charger"), icon: Icons.Plug);
    }

    private static void Refresh() => ((MainWindow)Application.Current.MainWindow!).Navigate("power");
}
