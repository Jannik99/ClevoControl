using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClevoControl.App.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class FansPage(ControlCenter cc) : AppPage(cc)
{
    private DispatcherTimer? _timer;
    private FanSettings F => Cc.Settings.Fan;

    public static string ModeName(FanMode m) => m switch
    {
        FanMode.Auto => Loc.T("Automatisch (BIOS)", "Automatic (BIOS)"),
        FanMode.Max => Loc.T("Maximum", "Maximum"),
        FanMode.Custom => Loc.T("Eigene Kurve", "Custom curve"),
        _ => m.ToString(),
    };

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Lüfter", "Fans")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Lüfterdrehzahl live sehen und selbst bestimmen.", "Watch fan speeds live and take control.")));

        // Live readouts
        var cpu = Ui.Text("", 22, true);
        var cpuSub = Ui.Text("", 12.5, sub: true);
        var gpu = Ui.Text("", 22, true);
        var gpuSub = Ui.Text("", 12.5, sub: true);
        var live = new Grid();
        live.ColumnDefinitions.Add(new ColumnDefinition());
        live.ColumnDefinitions.Add(new ColumnDefinition());
        var l = Ui.Stack(Ui.Text(Loc.T("CPU-Lüfter", "CPU fan"), 11.5, true, sub: true), cpu, cpuSub);
        var r = Ui.Stack(Ui.Text(Loc.T("GPU-Lüfter", "GPU fan"), 11.5, true, sub: true), gpu, gpuSub);
        Grid.SetColumn(r, 1);
        live.Children.Add(l);
        live.Children.Add(r);
        root.Children.Add(Ui.Card(live));

        // Mode
        var modes = new WrapPanel();
        foreach (var m in Enum.GetValues<FanMode>())
        {
            var mode = m;
            var desc = m switch
            {
                FanMode.Auto => Loc.T("Das BIOS regelt", "The BIOS decides"),
                FanMode.Max => Loc.T("Volle Kühlung, laut", "Full cooling, loud"),
                _ => Loc.T("Deine Kurve unten", "Your curve below"),
            };
            var icon = m switch { FanMode.Auto => Icons.Auto, FanMode.Max => Icons.Max, _ => Icons.Edit };
            modes.Children.Add(Ui.Tile(icon, ModeName(m), desc, F.Mode == m, "fanmode", () => { F.Mode = mode; Cc.ApplyFans(); }, 170));
        }
        var modeStack = Ui.Stack(modes,
            Ui.Switch(Loc.T("Eigene Lüftereinstellung pro Leistungsprofil", "Separate fan settings per performance profile"), F.PerProfile, v =>
            {
                F.PerProfile = v;
                if (v) Cc.InitFanProfiles();
                Cc.Store.SaveSoon();
            }, Loc.T($"Modus und Kurven gelten dann nur für das aktuelle Profil ({PowerPage.ProfileName(Cc.Settings.Power.Profile)}). Wechselst du das Profil, werden dessen Lüftereinstellungen geladen.",
                     $"Mode and curves then only apply to the current profile ({PowerPage.ProfileName(Cc.Settings.Power.Profile)}). Switching the profile loads its fan settings.")),
            Ui.Text(Loc.T("Tastenkürzel „Lüfter voll“: Strg+Alt+Umschalt+F (wenn „Globale Tastenkürzel“ in den Einstellungen an ist).",
                "Hotkey “fans full”: Ctrl+Alt+Shift+F (when “Global hotkeys” is on in Settings)."), 12, sub: true));
        root.Children.Add(Ui.Card(modeStack, Loc.T("Modus", "Mode"), icon: Icons.Gauge));
        Cc.FanSettingsSwapped += OnSwapped;

        // Curves
        var cpuCurve = new FanCurveEditor(F.CpuCurve);
        var gpuCurve = new FanCurveEditor(F.GpuCurve);
        cpuCurve.Changed += () => OnCurveEdited();
        gpuCurve.Changed += () => OnCurveEdited();
        var curves = new StackPanel();
        curves.Children.Add(Ui.Text("CPU", 13, true, bottom: 4));
        curves.Children.Add(cpuCurve);
        curves.Children.Add(Ui.Text("GPU", 13, true, bottom: 4));
        curves.Children.Add(gpuCurve);
        curves.Children.Add(Ui.Switch(Loc.T("GPU-Lüfter reagiert auch auf CPU-Temperatur", "GPU fan also follows CPU temperature"), F.GpuFollowsCpu,
            v => { F.GpuFollowsCpu = v; Cc.ApplyFans(); }, Loc.T("Empfohlen – CPU und GPU teilen sich meist die Heatpipes.", "Recommended – CPU and GPU usually share heat pipes.")));
        curves.Children.Add(Ui.SliderRow(Loc.T("Lüfter langsamer werden lassen um höchstens", "Slow fans down by at most"), 1, 20, F.RampDownPerSecond,
            v => { F.RampDownPerSecond = (int)v; Cc.ApplyFans(); }, v => $"{v:0} %/s", 1));
        curves.Children.Add(Ui.Wrap(
            PresetLabel(),
            Ui.Button(Loc.T("Leise Kurve", "Quiet curve"), () => SetPreset([new(45, 0), new(60, 25), new(70, 40), new(80, 70), new(90, 100)])),
            Ui.Button(Loc.T("Ausgewogene Kurve", "Balanced curve"), () => SetPreset(FanSettings.DefaultCurve())),
            Ui.Button(Loc.T("Kühle Kurve", "Cool curve"), () => SetPreset([new(35, 35), new(50, 50), new(60, 65), new(70, 80), new(80, 100)]))));
        var safety = Ui.InfoBar(Loc.T("Sicherheitsnetz (rot hinterlegt): Ab 70 °C mindestens 35 %, ab 80 °C mindestens 70 %, ab 88 °C immer 100 %. Bei Fehlern, im Standby und beim Beenden übernimmt automatisch wieder das BIOS.",
                "Safety net (red area): from 70 °C at least 35 %, from 80 °C at least 70 %, from 88 °C always 100 %. On errors, during sleep and on exit, the BIOS automatically takes over again."));
        curves.Children.Add(safety);
        root.Children.Add(Ui.Card(curves, Loc.T("Eigene Lüfterkurve", "Custom fan curve"), Loc.T("Punkte mit der Maus ziehen. Sobald du die Kurve änderst, wird „Eigene Kurve“ aktiv.", "Drag the points. As soon as you change the curve, “Custom curve” becomes active."), Icons.Edit));

        // Overheat warning
        var cooling = Cc.Settings.Cooling;
        var heat = Ui.Stack(
            Ui.Switch(Loc.T("Hitzewarnung anzeigen", "Show overheat warning"), cooling.OverheatWarning, v => { cooling.OverheatWarning = v; Cc.Store.SaveSoon(); },
                Loc.T("Meldung im Infobereich, wenn CPU oder GPU zu heiß werden – höchstens alle 5 Minuten.", "Notification in the tray when CPU or GPU get too hot – at most every 5 minutes.")),
            Ui.SliderRow(Loc.T("Warnen ab", "Warn from"), 75, 105, cooling.OverheatThreshold, v => { cooling.OverheatThreshold = (int)v; Cc.Store.SaveSoon(); }, v => $"{v:0} °C", 1));
        root.Children.Add(Ui.Card(heat, Loc.T("Hitzewarnung", "Overheat warning"), null, Icons.Warning));

        void Refresh()
        {
            var s = Cc.Sensors.Latest;
            cpu.Text = $"{s.Cpu.Rpm} RPM";
            cpuSub.Text = $"{s.Cpu.TemperatureC} °C · {s.Cpu.DutyPercent} %";
            gpu.Text = $"{s.Gpu.Rpm} RPM";
            gpuSub.Text = $"{s.Gpu.TemperatureC} °C · {s.Gpu.DutyPercent} %";
            cpuCurve.CurrentTemperature = s.Cpu.TemperatureC;
            gpuCurve.CurrentTemperature = F.GpuFollowsCpu ? Math.Max(s.Gpu.TemperatureC, s.Cpu.TemperatureC) : s.Gpu.TemperatureC;
            var accent = ((SolidColorBrush)Application.Current.FindResource("AccentFillColorDefaultBrush")).Color;
            cpuCurve.Accent = gpuCurve.Accent = accent;
            cpuCurve.InvalidateVisual();
            gpuCurve.InvalidateVisual();
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
        return root;

        void SetPreset(List<FanCurvePoint> pts)
        {
            F.CpuCurve.Clear(); F.CpuCurve.AddRange(pts.Select(p => new FanCurvePoint(p.Temperature, p.Duty)));
            F.GpuCurve.Clear(); F.GpuCurve.AddRange(pts.Select(p => new FanCurvePoint(p.Temperature, p.Duty)));
            Cc.ApplyFans();
            if (SwitchToCustom()) return;
            cpuCurve.InvalidateVisual();
            gpuCurve.InvalidateVisual();
        }

        void OnCurveEdited()
        {
            Cc.ApplyFans();
            SwitchToCustom();
        }

        bool SwitchToCustom()
        {
            if (F.Mode == FanMode.Custom) return false;
            F.Mode = FanMode.Custom;
            Cc.ApplyFans();
            Cc.Store.SaveSoon();
            ((MainWindow)Application.Current.MainWindow!).Navigate("fans");
            return true;
        }
    }

    private static TextBlock PresetLabel()
    {
        var t = Ui.Text(Loc.T("Vorlage laden:", "Load preset:"), 13, sub: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(0, 0, 12, 8);
        return t;
    }

    public override void OnLeave()
    {
        _timer?.Stop();
        Cc.FanSettingsSwapped -= OnSwapped;
    }

    // The profile changed and with it the fan settings: rebuild the page so the editors show the new curves.
    private void OnSwapped() => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        if (Application.Current.MainWindow is MainWindow w && w.IsVisible) w.Navigate("fans");
    });
}
