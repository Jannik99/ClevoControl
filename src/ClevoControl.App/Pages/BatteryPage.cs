using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.App.Pages;

public sealed class BatteryPage(ControlCenter cc) : AppPage(cc)
{
    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Akku", "Battery")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Zustand, Kapazität und Verschleiß deines Akkus.", "Condition, capacity and wear of your battery.")));

        var b = BatteryService.Read();
        if (!b.Present)
        {
            root.Children.Add(Ui.Card(Ui.Text(Loc.T("Kein Akku gefunden.", "No battery found."))));
            return root;
        }

        var big = Ui.Text($"{b.Percent} %", 40, true);
        var state = Ui.Text(b.Charging ? Loc.T("Wird geladen", "Charging") : b.OnAc ? Loc.T("Am Netzteil", "On AC power") : Loc.T("Akkubetrieb", "On battery"), 14, sub: true);
        var bar = new ProgressBar { Height = 10, Maximum = 100, Value = b.Percent, Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(50, 56, 71)), Foreground = RgbColor.FromHsv(120 * b.Percent / 100.0, 0.75, 0.9).ToBrush() };
        root.Children.Add(Ui.Card(Ui.Stack(big, state, bar)));

        string Mwh(int? v) => v is { } x ? $"{x / 1000.0:0.0} Wh" : "–";
        var rows = new StackPanel();
        void Add(string label, string value) => rows.Children.Add(Ui.Row(label, Ui.Text(value, 13.5, true), bottom: 10));
        Add(Loc.T("Akkugesundheit", "Battery health"), b.HealthPercent is { } h ? $"{h:0} %" : "–");
        Add(Loc.T("Kapazität bei Auslieferung", "Design capacity"), Mwh(b.DesignCapacityMWh));
        Add(Loc.T("Aktuelle volle Kapazität", "Full charge capacity"), Mwh(b.FullChargeCapacityMWh));
        Add(Loc.T("Verbleibend", "Remaining"), Mwh(b.RemainingCapacityMWh));
        Add(Loc.T("Ladezyklen", "Cycle count"), b.CycleCount?.ToString() ?? "–");
        Add(Loc.T("Spannung", "Voltage"), b.Voltage is { } v ? $"{v:0.00} V" : "–");
        Add(b.RateMw is > 0 ? Loc.T("Ladeleistung", "Charge rate") : Loc.T("Verbrauch", "Power draw"), b.RateMw is { } r && r != 0 ? $"{Math.Abs(r) / 1000.0:0.0} W" : "–");
        if (b.Remaining is { } rem) Add(Loc.T("Restlaufzeit (Windows)", "Time left (Windows)"), $"{(int)rem.TotalHours} h {rem.Minutes:00} min");
        if (b.Manufacturer is { Length: > 0 } m) Add(Loc.T("Hersteller", "Manufacturer"), m);
        if (b.Chemistry is { } c) Add(Loc.T("Zelltyp", "Chemistry"), c);
        root.Children.Add(Ui.Card(rows, Loc.T("Details", "Details")));

        root.Children.Add(Ui.InfoBar(Loc.T(
            "Akku schonen: Viele Clevo-Modelle haben „FlexiCharger“ (nur zwischen z. B. 40 und 80 % laden). Wenn dein Modell das unterstützt, findest du es im BIOS-Setup (beim Start meist F2) und kannst dort Start- und Stopp-Schwelle festlegen.",
            "Battery care: many Clevo models have “FlexiCharger” (only charge between e.g. 40 and 80 %). If your model supports it, you find it in the BIOS setup (usually F2 at boot) where you can set start and stop thresholds.")));
        return root;
    }
}
