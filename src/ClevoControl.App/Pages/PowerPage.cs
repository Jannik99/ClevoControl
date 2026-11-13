using System.Windows;
using System.Windows.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class PowerPage(ControlCenter cc) : AppPage(cc)
{
    private PowerSettings P => Cc.Settings.Power;

    public static string ProfileName(PerformanceProfile p) => p switch
    {
        PerformanceProfile.Quiet => Loc.T("Leise", "Quiet"),
        PerformanceProfile.PowerSaving => Loc.T("Stromsparen", "Power saving"),
        PerformanceProfile.Performance => Loc.T("Performance", "Performance"),
        PerformanceProfile.Entertainment => Loc.T("Entertainment", "Entertainment"),
        _ => p.ToString(),
    };

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Leistung", "Performance")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Clevo-Leistungsprofile, Windows-Energiemodus und Automatik für Akku/Netzteil.", "Clevo performance profiles, Windows power mode and battery/AC automation.")));

        var profiles = new WrapPanel();
        foreach (var p in Enum.GetValues<PerformanceProfile>())
        {
            var prof = p;
            var (icon, desc) = p switch
            {
                PerformanceProfile.Quiet => (Icons.Moon, Loc.T("Leise Lüfter, weniger Leistung", "Quiet fans, less power")),
                PerformanceProfile.PowerSaving => (Icons.BatterySaver, Loc.T("Maximale Akkulaufzeit", "Maximum battery life")),
                PerformanceProfile.Performance => (Icons.Lightning, Loc.T("Volle Leistung", "Full power")),
                _ => (Icons.Video, Loc.T("Ausgewogen (Standard)", "Balanced (default)")),
            };
            profiles.Children.Add(Ui.Tile(icon, ProfileName(p), desc, P.Profile == p, "profile", () => { P.Profile = prof; Cc.ApplyPower(); }, 190));
        }
        var profileStack = Ui.Stack(profiles, Ui.Switch(Loc.T("Windows-Energiemodus mitschalten", "Switch Windows power mode too"), P.SyncWindowsPowerMode,
            v => { P.SyncWindowsPowerMode = v; Cc.ApplyPower(); }, Loc.T("Leise/Stromsparen → Beste Energieeffizienz, Performance → Beste Leistung", "Quiet/power saving → best efficiency, performance → best performance")));
        root.Children.Add(Ui.Card(profileStack, Loc.T("Leistungsprofil", "Performance profile")));

        var auto = new StackPanel();
        auto.Children.Add(Ui.Switch(Loc.T("Automatisch umschalten", "Switch automatically"), P.AutoSwitch, v => { P.AutoSwitch = v; Cc.ApplyPower(); },
            Loc.T("Wechselt das Profil, sobald du das Netzteil ein- oder aussteckst.", "Changes the profile when you plug or unplug the charger.")));
        auto.Children.Add(Ui.Row(Loc.T("Am Netzteil", "On AC power"), Ui.Combo(Enum.GetValues<PerformanceProfile>().Select(p => (p, ProfileName(p))), P.ProfileOnAc, p => { P.ProfileOnAc = p; Cc.ApplyPower(); })));
        auto.Children.Add(Ui.Row(Loc.T("Im Akkubetrieb", "On battery"), Ui.Combo(Enum.GetValues<PerformanceProfile>().Select(p => (p, ProfileName(p))), P.ProfileOnBattery, p => { P.ProfileOnBattery = p; Cc.ApplyPower(); })));
        root.Children.Add(Ui.Card(auto, Loc.T("Akku / Netzteil", "Battery / AC")));

        var current = PowerService.GetWindowsPowerMode();
        var win = new WrapPanel();
        foreach (var (mode, title, icon) in new[]
                 {
                     (WindowsPowerMode.BestEfficiency, Loc.T("Beste Effizienz", "Best efficiency"), Icons.BatterySaver),
                     (WindowsPowerMode.Balanced, Loc.T("Ausbalanciert", "Balanced"), Icons.Tune),
                     (WindowsPowerMode.BestPerformance, Loc.T("Beste Leistung", "Best performance"), Icons.Lightning),
                 })
        {
            var m = mode;
            win.Children.Add(Ui.Tile(icon, title, null, current == mode, "winmode", () => PowerService.SetWindowsPowerMode(m), 170));
        }
        root.Children.Add(Ui.Card(win, Loc.T("Windows-Energiemodus", "Windows power mode"), Loc.T("Der Regler aus den Windows-Einstellungen, direkt hier.", "The slider from Windows settings, right here.")));

        var rates = DisplayService.GetRefreshRates();
        var display = new StackPanel();
        display.Children.Add(Ui.Text(Loc.T($"Aktuell: {DisplayService.GetRefreshRate()} Hz · verfügbar: {string.Join(", ", rates)} Hz", $"Current: {DisplayService.GetRefreshRate()} Hz · available: {string.Join(", ", rates)} Hz"), 12.5, sub: true, bottom: 12));
        display.Children.Add(Ui.Switch(Loc.T("Im Akkubetrieb Bildwiederholrate senken", "Lower refresh rate on battery"), P.LowRefreshOnBattery,
            v => { P.LowRefreshOnBattery = v; Cc.ApplyPower(); }, Loc.T("Spart spürbar Strom; am Netzteil wieder die höchste Rate.", "Saves noticeable power; highest rate again on AC.")));
        if (rates.Count > 1)
            display.Children.Add(Ui.Row(Loc.T("Rate im Akkubetrieb", "Rate on battery"), Ui.Combo(rates.Select(r => (r, $"{r} Hz")), rates.OrderBy(r => Math.Abs(r - P.BatteryRefreshRate)).First(),
                r => { P.BatteryRefreshRate = r; Cc.ApplyPower(); }, 120)));
        var manual = new WrapPanel();
        foreach (var r in rates)
        {
            var rate = r;
            manual.Children.Add(Ui.Button($"{r} Hz", () => { DisplayService.SetRefreshRate(rate); ((MainWindow)Application.Current.MainWindow!).Navigate("power"); }));
        }
        display.Children.Add(manual);
        root.Children.Add(Ui.Card(display, Loc.T("Bildschirm", "Display")));
        return root;
    }
}
