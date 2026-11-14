using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.App.Pages;

public sealed class BatteryPage(ControlCenter cc) : AppPage(cc)
{
    private UIElement ChargeLimitCard()
    {
        var info = Cc.Hardware.ReadChargeLimit();
        if (info is null)
        {
            return Ui.SettingsCard(Icons.BatterySaver, Loc.T("Ladelimit (FlexiCharger)", "Charge limit (FlexiCharger)"),
                Loc.T("Dein Modell meldet keine Software-Steuerung dafür. Falls vorhanden, findest du FlexiCharger im BIOS-Setup.",
                      "Your model does not report software control for it. If available, you find FlexiCharger in the BIOS setup."), null);
        }
        bool enabled = info.Enabled;
        int start = info.StartOptions.Contains(info.Start) ? info.Start : info.StartOptions[0];
        int stop = info.StopOptions.Contains(info.Stop) ? info.Stop : info.StopOptions[^1];
        var status = Ui.Text("", 12, sub: true);
        void Save()
        {
            try
            {
                if (start >= stop) { status.Text = Loc.T("Start muss unter Stopp liegen.", "Start must be below stop."); return; }
                Cc.Hardware.SetChargeLimit(enabled, start, stop);
                status.Text = Loc.T("Gespeichert.", "Saved.");
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        var s = Ui.Stack(
            Ui.Switch(Loc.T("Ladelimit aktiv", "Charge limit active"), enabled, v => { enabled = v; Save(); },
                Loc.T("Schont den Akku, wenn der Laptop meist am Netzteil hängt.", "Protects the battery if the laptop is mostly plugged in.")),
            Ui.Row(Loc.T("Laden starten unter", "Start charging below"), Ui.Combo(info.StartOptions.Select(o => (o, $"{o} %")), start, v => { start = v; Save(); }, 120)),
            Ui.Row(Loc.T("Laden stoppen bei", "Stop charging at"), Ui.Combo(info.StopOptions.Select(o => (o, $"{o} %")), stop, v => { stop = v; Save(); }, 120)),
            Ui.InfoBar(Loc.T("Experimentell: nutzt dieselben Befehle wie Clevos Control Center, auf diesem Modell aber noch nicht von uns getestet.",
                "Experimental: uses the same commands as Clevo's Control Center, but not yet tested by us on this model."), true),
            status);
        return Ui.Card(s, Loc.T("Ladelimit (FlexiCharger)", "Charge limit (FlexiCharger)"), icon: Icons.BatterySaver);
    }

    /// <summary>Refresh rate: the biggest battery saver on high-Hz laptop panels.</summary>
    private Border RefreshRateCard()
    {
        var p = Cc.Settings.Power;
        var rates = DisplayService.GetRefreshRates();
        var s = new StackPanel();
        if (rates.Count > 1)
        {
            s.Children.Add(Ui.Row(Loc.T("Bildwiederholrate jetzt", "Refresh rate now"), Ui.Combo(rates.Select(r => (r, $"{r} Hz")), DisplayService.GetRefreshRate(),
                r => DisplayService.SetRefreshRate(r), 120)));
            s.Children.Add(Ui.Switch(Loc.T("Im Akkubetrieb automatisch senken", "Lower automatically on battery"), p.LowRefreshOnBattery,
                v => { p.LowRefreshOnBattery = v; Cc.ApplyPower(); }, Loc.T("Spart spürbar Strom. Am Netzteil wieder die höchste Rate.", "Saves noticeable power. Highest rate again when plugged in.")));
            s.Children.Add(Ui.Row(Loc.T("Rate im Akkubetrieb", "Rate on battery"), Ui.Combo(rates.Select(r => (r, $"{r} Hz")), rates.OrderBy(r => Math.Abs(r - p.BatteryRefreshRate)).First(),
                r => { p.BatteryRefreshRate = r; Cc.ApplyPower(); }, 120), bottom: 0));
        }
        else
        {
            s.Children.Add(Ui.Text(Loc.T($"Dein Bildschirm unterstützt nur {DisplayService.GetRefreshRate()} Hz – hier gibt es nichts einzustellen.", $"Your display only supports {DisplayService.GetRefreshRate()} Hz – nothing to adjust here."), 12.5, sub: true));
        }
        return Ui.Card(s, Loc.T("Bildwiederholrate", "Refresh rate"), icon: Icons.Monitor);
    }

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
        root.Children.Add(Ui.Card(rows, Loc.T("Details", "Details"), icon: Icons.Info));
        root.Children.Add(ChargeLimitCard());

        root.Children.Add(RefreshRateCard());
        return root;
    }
}
