using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.Desktop.Pages;

public sealed class BatteryPage(ControlCenter cc) : AppPage(cc)
{
    private DispatcherTimer? _timer;

    private Control ChargeLimitCard()
    {
        var info = Cc.Hardware.ReadChargeLimit();
        if (info is null)
        {
            return Ui.SettingsCard(Icons.BatterySaver, Loc.T("Ladelimit (FlexiCharger)", "Charge limit (FlexiCharger)"),
                OperatingSystem.IsWindows()
                    ? Loc.T("Dein Modell meldet keine Software-Steuerung dafür. Falls vorhanden, findest du FlexiCharger im BIOS-Setup.",
                          "Your model does not report software control for it. If available, you find FlexiCharger in the BIOS setup.")
                    : Loc.T("Der Akkutreiber bietet keine Ladeschwellen (charge_control_*_threshold in /sys/class/power_supply). Falls vorhanden, findest du FlexiCharger im BIOS-Setup.",
                          "The battery driver offers no charge thresholds (charge_control_*_threshold in /sys/class/power_supply). If available, you find FlexiCharger in the BIOS setup."), null);
        }

        // The controls are a draft: nothing is written to the EC until "Apply" is clicked.
        var applied = info;
        bool enabled = info.Enabled;
        int stop = info.StopOptions.Contains(info.Stop) ? info.Stop : info.StopOptions[^1];
        int start = info.StartOptions.Contains(info.Start) ? info.Start : info.StartOptions[0];

        var status = new StackPanel { IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
        void ShowStatus(Control? c)
        {
            status.Children.Clear();
            if (c is not null)
            {
                c.Margin = new Thickness(0);
                status.Children.Add(c);
            }
            status.IsVisible = c is not null;
        }

        Button apply = null!;
        apply = Ui.Button(Loc.T("Übernehmen", "Apply"), () => _ = ApplyAsync(), accent: true);
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        void UpdateApply()
        {
            var dirty = enabled != applied.Enabled || (enabled && (start != applied.Start || stop != applied.Stop));
            apply.IsEnabled = start < stop && dirty;
        }

        // Start options are filtered to values below the chosen stop.
        var startCombo = new ComboBox { Width = 120 };
        void FillStart()
        {
            var options = info.StartOptions.Where(o => o < stop).ToList();
            if (!options.Contains(start)) start = options.Count > 0 ? options[^1] : start;
            startCombo.Items.Clear();
            foreach (var o in options)
            {
                var item = new ComboBoxItem { Content = $"{o} %", Tag = o };
                startCombo.Items.Add(item);
                if (o == start) startCombo.SelectedItem = item;
            }
        }
        FillStart();
        startCombo.SelectionChanged += (_, _) =>
        {
            if (startCombo.SelectedItem is ComboBoxItem { Tag: int v }) start = v;
            UpdateApply();
        };
        var stopCombo = Ui.Combo(info.StopOptions.Select(o => (o, $"{o} %")), stop, v => { stop = v; FillStart(); UpdateApply(); }, 120);

        var startRow = Ui.Row(Loc.T("Laden starten unter", "Start charging below"), startCombo);
        var stopRow = Ui.Row(Loc.T("Laden stoppen bei", "Stop charging at"), stopCombo);
        void UpdateRows() => startRow.IsEnabled = stopRow.IsEnabled = enabled;
        UpdateRows();
        UpdateApply();

        async Task ApplyAsync()
        {
            if (start >= stop) return;
            if (enabled && !applied.Enabled &&
                !await Ui.ConfirmAsync(Owner, Loc.T("Ladelimit ist auf deinem Modell ungetestet. Bei Problemen schaltest du es hier oder im BIOS (FlexiCharger) wieder aus. Fortfahren?",
                    "The charge limit is untested on your model. If there are problems, turn it off here or in the BIOS (FlexiCharger). Continue?")))
                return;
            try
            {
                Cc.Hardware.SetChargeLimit(enabled, start, stop);
                var back = Cc.Hardware.ReadChargeLimit();
                var ok = back is not null && back.Enabled == enabled && (!enabled || (back.Start == start && back.Stop == stop));
                if (back is not null) applied = back;
                if (ok)
                {
                    ShowStatus(Ui.Text(enabled
                        ? Loc.T($"Gespeichert – Laden stoppt bei {stop} %", $"Saved – charging stops at {stop} %")
                        : Loc.T("Gespeichert – Ladelimit aus", "Saved – charge limit off"), 12.5, sub: true));
                }
                else
                {
                    var got = back is null ? "–" : back.Enabled ? $"{back.Start} % – {back.Stop} %" : Loc.T("aus", "off");
                    ShowStatus(Ui.InfoBar(Loc.T($"Dein Modell meldet nach dem Schreiben andere Werte ({got}). Das Ladelimit wirkt hier vermutlich nicht – schalte es im Zweifel im BIOS (FlexiCharger) ein.",
                        $"Your model reports different values after writing ({got}). The charge limit probably does not work here – if in doubt, enable it in the BIOS (FlexiCharger)."), true));
                }
            }
            catch (UnauthorizedAccessException) when (!OperatingSystem.IsWindows())
            {
                ShowStatus(Ui.InfoBar(Loc.T("Keine Schreibrechte. Installiere die udev-Regel (contrib/linux/install.sh) und melde dich neu an.",
                    "No write permission. Install the udev rule (contrib/linux/install.sh) and sign in again."), true));
            }
            catch (Exception ex)
            {
                Log.Error("Charge limit", ex);
                ShowStatus(Ui.InfoBar(Loc.T("Das Ladelimit konnte nicht geschrieben werden. Details stehen im Log.",
                    "The charge limit could not be written. Details are in the log."), true));
            }
            UpdateApply();
        }

        var s = Ui.Stack(
            Ui.Switch(Loc.T("Ladelimit aktiv", "Charge limit active"), enabled, v => { enabled = v; UpdateRows(); UpdateApply(); },
                Loc.T("Schont den Akku, wenn der Laptop meist am Netzteil hängt.", "Protects the battery if the laptop is mostly plugged in.")),
            startRow,
            stopRow,
            Ui.InfoBar(OperatingSystem.IsWindows()
                ? Loc.T("Experimentell: nutzt dieselben Befehle wie Clevos Control Center, auf diesem Modell aber noch nicht von uns getestet.",
                    "Experimental: uses the same commands as Clevo's Control Center, but not yet tested by us on this model.")
                : Loc.T("Experimentell: nutzt die Ladeschwellen des Kernels (/sys/class/power_supply), auf diesem Modell aber noch nicht von uns getestet.",
                    "Experimental: uses the kernel's charge thresholds (/sys/class/power_supply), but not yet tested by us on this model."), true),
            apply,
            status);
        return Ui.Card(s, Loc.T("Ladelimit (FlexiCharger)", "Charge limit (FlexiCharger)"), icon: Icons.BatterySaver);
    }

    public override Control Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Akku", "Battery")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Zustand, Verschleiß und Ladelimit", "Health, wear and charge limit")));

        var b = BatteryService.Read();
        if (!b.Present)
        {
            root.Children.Add(Ui.Card(Ui.Text(Loc.T("Kein Akku gefunden.", "No battery found."))));
            return root;
        }

        var big = Ui.Text("", 40, true);
        var state = Ui.Text("", 14, sub: true);
        var bar = new ProgressBar
        {
            Height = 10, MinHeight = 10, Maximum = 100, Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromRgb(50, 56, 71)),
        };
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
        // Power draw: label and value are refreshed by the timer.
        var rateValue = Ui.Text("", 13.5, true);
        var rateRow = Ui.Row("", rateValue, bottom: 10);
        var rateLabel = (TextBlock)((StackPanel)rateRow.Children[0]).Children[0];
        rows.Children.Add(rateRow);
        if (b.Remaining is { } rem)
            Add(OperatingSystem.IsWindows() ? Loc.T("Restlaufzeit (Windows)", "Time left (Windows)") : Loc.T("Restlaufzeit (System)", "Time left (system)"),
                $"{(int)rem.TotalHours} h {rem.Minutes:00} min");
        if (b.Manufacturer is { Length: > 0 } m) Add(Loc.T("Hersteller", "Manufacturer"), m);
        if (b.Chemistry is { } c) Add(Loc.T("Zelltyp", "Chemistry"), c);
        root.Children.Add(Ui.Card(rows, Loc.T("Details", "Details"), icon: Icons.Info));
        root.Children.Add(ChargeLimitCard());

        void Show(BatteryInfo x)
        {
            big.Text = $"{x.Percent} %";
            state.Text = x.Charging ? Loc.T("Wird geladen", "Charging") : x.OnAc ? Loc.T("Am Netzteil", "On AC power") : Loc.T("Akkubetrieb", "On battery");
            bar.Value = x.Percent;
            bar.Foreground = RgbColor.FromHsv(120 * x.Percent / 100.0, 0.75, 0.9).ToBrush();
            rateLabel.Text = x.RateMw is > 0 ? Loc.T("Ladeleistung", "Charge rate") : Loc.T("Verbrauch", "Power draw");
            rateValue.Text = x.RateMw is { } r && r != 0 ? $"{Math.Abs(r) / 1000.0:0.0} W" : "–";
        }
        Show(b);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) =>
        {
            try
            {
                var now = BatteryService.Read();
                if (now.Present) Show(now);
            }
            catch (Exception ex) { Log.Error("Battery refresh", ex); _timer?.Stop(); }
        };
        _timer.Start();
        return root;
    }

    public override void OnLeave() => _timer?.Stop();
}
