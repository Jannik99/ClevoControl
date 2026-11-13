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
        FanMode.Auto => Loc.T("Automatisch", "Automatic"),
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
        var l = Ui.Stack(Ui.Text(Loc.T("CPU-LÜFTER", "CPU FAN"), 11.5, true, sub: true), cpu, cpuSub);
        var r = Ui.Stack(Ui.Text(Loc.T("GPU-LÜFTER", "GPU FAN"), 11.5, true, sub: true), gpu, gpuSub);
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
                FanMode.Auto => Loc.T("Firmware regelt", "Firmware decides"),
                FanMode.Max => Loc.T("Volle Kühlung", "Full cooling"),
                _ => Loc.T("Deine Kurve unten", "Your curve below"),
            };
            var icon = m switch { FanMode.Auto => Icons.Auto, FanMode.Max => Icons.Max, _ => Icons.Edit };
            modes.Children.Add(Ui.Tile(icon, ModeName(m), desc, F.Mode == m, "fanmode", () => { F.Mode = mode; Cc.ApplyFans(); }, 170));
        }
        var modeStack = Ui.Stack(modes,
            Ui.Switch(Loc.T("Lüfter je Leistungsprofil merken", "Remember fans per performance profile"), F.PerProfile, v =>
            {
                F.PerProfile = v;
                if (v) Cc.InitFanProfiles();
                Cc.Store.SaveSoon();
            }, Loc.T($"Modus und Kurven gelten dann nur für das aktuelle Profil ({PowerPage.ProfileName(Cc.Settings.Power.Profile)}). Wechselst du das Profil, werden dessen Lüftereinstellungen geladen.",
                     $"Mode and curves then only apply to the current profile ({PowerPage.ProfileName(Cc.Settings.Power.Profile)}). Switching the profile loads its fan settings.")),
            Ui.Text(Loc.T("Tastenkürzel „Lüfter voll“: Strg+Alt+Umschalt+F (wenn Tastenkürzel in den Einstellungen aktiv sind).",
                "Hotkey “fans full”: Ctrl+Alt+Shift+F (when hotkeys are enabled in Settings)."), 12, sub: true));
        root.Children.Add(Ui.Card(modeStack, Loc.T("Modus", "Mode")));
        Cc.FanSettingsSwapped += OnSwapped;

        // Curves
        var cpuCurve = new FanCurveEditor(F.CpuCurve);
        var gpuCurve = new FanCurveEditor(F.GpuCurve);
        cpuCurve.Changed += () => Cc.ApplyFans();
        gpuCurve.Changed += () => Cc.ApplyFans();
        var curves = new StackPanel();
        curves.Children.Add(Ui.Text("CPU", 13, true, bottom: 4));
        curves.Children.Add(cpuCurve);
        curves.Children.Add(Ui.Text("GPU", 13, true, bottom: 4));
        curves.Children.Add(gpuCurve);
        curves.Children.Add(Ui.Switch(Loc.T("GPU-Lüfter reagiert auch auf CPU-Temperatur", "GPU fan also follows CPU temperature"), F.GpuFollowsCpu,
            v => { F.GpuFollowsCpu = v; Cc.ApplyFans(); }, Loc.T("Empfohlen – CPU und GPU teilen sich meist die Heatpipes.", "Recommended – CPU and GPU usually share heat pipes.")));
        curves.Children.Add(Ui.SliderRow(Loc.T("Max. Absenkung pro Sekunde", "Max. ramp-down per second"), 1, 20, F.RampDownPerSecond,
            v => { F.RampDownPerSecond = (int)v; Cc.ApplyFans(); }, v => $"{v:0} %", 1));
        curves.Children.Add(Ui.Wrap(
            Ui.Button(Loc.T("Leise", "Quiet"), () => SetPreset([new(45, 0), new(60, 25), new(70, 40), new(80, 70), new(90, 100)])),
            Ui.Button(Loc.T("Ausgewogen", "Balanced"), () => SetPreset(FanSettings.DefaultCurve())),
            Ui.Button(Loc.T("Kühl", "Cool"), () => SetPreset([new(35, 35), new(50, 50), new(60, 65), new(70, 80), new(80, 100)]))));
        var safety = Ui.InfoBar(Loc.T("Sicherheitsnetz (rot hinterlegt): ab 70 °C mind. 35 %, ab 80 °C mind. 70 %, ab 88 °C immer 100 % – egal was die Kurve sagt. Bei unplausiblen Sensorwerten, Standby und beim Beenden übernimmt wieder die Firmware.",
                "Safety net (red area): from 70 °C at least 35 %, from 80 °C at least 70 %, from 88 °C always 100 % – whatever the curve says. With implausible sensor values, on sleep and on exit the firmware takes over again."));
        curves.Children.Add(safety);
        root.Children.Add(Ui.Card(curves, Loc.T("Eigene Lüfterkurve", "Custom fan curve"), Loc.T("Punkte mit der Maus ziehen. Wirkt nur im Modus „Eigene Kurve“.", "Drag the points. Only active in “Custom curve” mode.")));

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
            cpuCurve.InvalidateVisual();
            gpuCurve.InvalidateVisual();
        }
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
