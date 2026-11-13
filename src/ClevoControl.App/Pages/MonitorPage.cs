using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClevoControl.App.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Monitoring;

namespace ClevoControl.App.Pages;

/// <summary>Detailed CPU (per core) and GPU graphs. Counters only run while this page is open.</summary>
public sealed class MonitorPage(ControlCenter cc) : AppPage(cc)
{
    private const int History = 120;
    private CpuMonitor? _cpu;
    private GpuMonitor? _gpu;
    private DispatcherTimer? _timer;
    private readonly List<CpuSample> _cpuHist = [];
    private readonly List<GpuSample> _gpuHist = [];
    private readonly List<Action> _updaters = [];

    private static readonly Color Blue = Color.FromRgb(76, 141, 255), Green = Color.FromRgb(61, 200, 120),
        Orange = Color.FromRgb(255, 170, 60), Purple = Color.FromRgb(180, 120, 255), Red = Color.FromRgb(240, 80, 90);

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Monitor", "Monitor")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Detaillierte Werte von Prozessor und Grafikkarte. Die Messung läuft nur, solange diese Seite offen ist.",
            "Detailed processor and graphics values. Measuring only runs while this page is open.")));

        _cpu = new CpuMonitor();
        root.Children.Add(CpuCard());

        _gpu = new GpuMonitor();
        root.Children.Add(_gpu.Available ? GpuCard() : Ui.Card(Ui.Text(Loc.T("Keine NVIDIA-Grafikkarte gefunden (oder Treiber ohne NVML).", "No NVIDIA GPU found (or driver without NVML)."), sub: true), "GPU", icon: Icons.Monitor));

        root.Children.Add(FanTempCard());

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
        return root;
    }

    private void Tick()
    {
        if (_cpu?.Available == true)
        {
            _cpuHist.Add(_cpu.Sample());
            if (_cpuHist.Count > History) _cpuHist.RemoveAt(0);
        }
        if (_gpu?.Sample() is { } g)
        {
            _gpuHist.Add(g);
            if (_gpuHist.Count > History) _gpuHist.RemoveAt(0);
        }
        foreach (var u in _updaters) u();
    }

    public override void OnLeave()
    {
        _timer?.Stop();
        _cpu?.Dispose();
        _gpu?.Dispose();
        _cpu = null;
        _gpu = null;
    }

    // ---------------------------------------------------------------- CPU

    private Border CpuCard()
    {
        var cpu = _cpu!;
        var s = new StackPanel();
        s.Children.Add(Ui.Text($"{cpu.Name}  ·  {cpu.PhysicalCores} {Loc.T("Kerne", "cores")} / {cpu.LogicalCores} Threads  ·  {Loc.T("Basistakt", "base clock")} {cpu.BaseMhz / 1000:0.0} GHz", 12.5, sub: true, bottom: 12));

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var load = BigGraph(Loc.T("Auslastung", "Load"), "%", Blue, 0, 100, () => _cpuHist.Select(x => x.TotalLoad).ToList());
        var clock = BigGraph(Loc.T("Takt", "Clock"), "GHz", Orange, 0, Math.Max(1, cpu.BaseMhz * 1.9 / 1000), () => _cpuHist.Select(x => x.FrequencyMhz / 1000).ToList(), "0.00");
        Grid.SetColumn(clock, 2);
        top.Children.Add(load);
        top.Children.Add(clock);
        s.Children.Add(top);

        if (cpu.Available)
        {
            s.Children.Add(Ui.Text(Loc.T("Pro Kern (logische Prozessoren)", "Per core (logical processors)"), 13, true, bottom: 8));
            var grid = new UniformGrid { Columns = cpu.LogicalCores > 12 ? 6 : 4 };
            var count = _cpuHist.LastOrDefault()?.Cores.Count ?? cpu.Sample().Cores.Count;
            for (var i = 0; i < count; i++)
            {
                var idx = i;
                var spark = new Sparkline { Height = 38, LineColor = Blue, Max = 100 };
                var label = Ui.Text($"#{i}", 11.5, sub: true);
                var val = Ui.Text("–", 11.5, true);
                val.HorizontalAlignment = HorizontalAlignment.Right;
                var head = new Grid();
                head.Children.Add(label);
                head.Children.Add(val);
                var cell = Ui.CardBorder(Ui.Stack(head, spark), new Thickness(0, 0, 6, 6), new Thickness(10, 6, 10, 6));
                grid.Children.Add(cell);
                _updaters.Add(() =>
                {
                    var values = _cpuHist.Where(h => h.Cores.Count > idx).Select(h => h.Cores[idx].LoadPercent).ToList();
                    if (values.Count == 0) return;
                    spark.SetValues(values);
                    var v = values[^1];
                    val.Text = $"{v:0} %";
                    spark.LineColor = v > 85 ? Red : v > 50 ? Orange : Blue;
                });
            }
            s.Children.Add(grid);
        }
        else
        {
            s.Children.Add(Ui.InfoBar(Loc.T("Windows-Leistungsindikatoren sind nicht verfügbar.", "Windows performance counters are not available."), true));
        }
        return Ui.Card(s, Loc.T("Prozessor", "Processor"), icon: Icons.Chip);
    }

    // ---------------------------------------------------------------- GPU

    private Border GpuCard()
    {
        var gpu = _gpu!;
        var s = new StackPanel();
        s.Children.Add(Ui.Text(gpu.Name + (gpu.DriverVersion is { } d ? $"  ·  {Loc.T("Treiber", "driver")} {d}" : ""), 12.5, sub: true, bottom: 12));

        var grid = new UniformGrid { Columns = 2 };
        double vramTotal = _gpuHist.LastOrDefault()?.VramTotalMb ?? gpu.Sample()?.VramTotalMb ?? 8192;
        grid.Children.Add(BigGraph(Loc.T("Auslastung", "Load"), "%", Green, 0, 100, () => _gpuHist.Select(x => x.LoadPercent).ToList(), margin: true));
        grid.Children.Add(BigGraph(Loc.T("Grafikspeicher", "VRAM"), "MB", Purple, 0, vramTotal, () => _gpuHist.Select(x => x.VramUsedMb).ToList(), "0", true,
            () => _gpuHist.Count > 0 ? $"/ {_gpuHist[^1].VramTotalMb:0} MB" : ""));
        grid.Children.Add(BigGraph(Loc.T("Kerntakt", "Core clock"), "MHz", Orange, 0, 2200, () => _gpuHist.Select(x => x.CoreClockMhz).ToList(), "0", true,
            () => _gpuHist.Count > 0 ? $"{Loc.T("Speicher", "memory")} {_gpuHist[^1].MemoryClockMhz:0} MHz" : ""));
        grid.Children.Add(BigGraph(Loc.T("Leistungsaufnahme", "Power draw"), "W", Red, 0, 150, () => _gpuHist.Select(x => x.PowerW).ToList(), "0", true,
            () => _gpuHist.Count > 0 && _gpuHist[^1].PowerLimitW is { } l ? $"{Loc.T("Limit", "limit")} {l:0} W · P{_gpuHist[^1].PState}" : ""));
        s.Children.Add(grid);
        s.Children.Add(Ui.InfoBar(Loc.T("Bei Laptops mit umschaltbarer Grafik hält das Auslesen die Grafikkarte wach. Deshalb misst diese Seite nur, solange sie geöffnet ist.",
            "On laptops with switchable graphics, reading these values keeps the GPU awake – that is why this page only measures while it is open.")));
        return Ui.Card(s, Loc.T("Grafikkarte", "Graphics card"), icon: Icons.Monitor);
    }

    // ---------------------------------------------------------------- Fans / temps (EC)

    private Border FanTempCard()
    {
        var grid = new UniformGrid { Columns = 2 };
        List<SensorSnapshotView> hist() => Cc.Sensors.History.Select(h => new SensorSnapshotView(h)).ToList();
        grid.Children.Add(BigGraph(Loc.T("CPU-Temperatur", "CPU temperature"), "°C", Red, 30, 100, () => hist().Select(h => (double)h.S.Cpu.TemperatureC).ToList(), margin: true));
        grid.Children.Add(BigGraph(Loc.T("GPU-Temperatur", "GPU temperature"), "°C", Red, 30, 100, () => hist().Select(h => (double)h.S.Gpu.TemperatureC).ToList(), margin: true));
        grid.Children.Add(BigGraph(Loc.T("CPU-Lüfter", "CPU fan"), "RPM", Blue, 0, 6000, () => hist().Select(h => (double)h.S.Cpu.Rpm).ToList(), margin: true));
        grid.Children.Add(BigGraph(Loc.T("GPU-Lüfter", "GPU fan"), "RPM", Blue, 0, 6000, () => hist().Select(h => (double)h.S.Gpu.Rpm).ToList(), margin: true));
        return Ui.Card(grid, Loc.T("Temperaturen & Lüfter (Controller)", "Temperatures & fans (EC)"), icon: Icons.Thermometer);
    }

    private sealed record SensorSnapshotView(SensorSnapshot S);

    private FrameworkElement BigGraph(string title, string unit, Color color, double min, double max, Func<List<double>> values,
        string format = "0", bool margin = false, Func<string>? extra = null)
    {
        var value = Ui.Text("–", 24, true);
        var unitText = Ui.Text(" " + unit, 13, sub: true);
        unitText.VerticalAlignment = VerticalAlignment.Bottom;
        unitText.Margin = new Thickness(0, 0, 0, 4);
        var extraText = Ui.Text("", 12, sub: true);
        extraText.VerticalAlignment = VerticalAlignment.Bottom;
        extraText.Margin = new Thickness(10, 0, 0, 4);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(value);
        row.Children.Add(unitText);
        row.Children.Add(extraText);
        var spark = new Sparkline { Height = 70, LineColor = color, Min = min, Max = max, Margin = new Thickness(0, 6, 0, 0) };
        _updaters.Add(() =>
        {
            var v = values();
            if (v.Count == 0) return;
            if (v[^1] > spark.Max) spark.Max = v[^1] * 1.1;
            spark.SetValues(v);
            value.Text = v[^1].ToString(format);
            if (extra is not null) extraText.Text = extra();
        });
        var stack = Ui.Stack(Ui.Text(title, 12.5, sub: true), row, spark);
        stack.Margin = new Thickness(0, 0, margin ? 16 : 0, 14);
        return stack;
    }

    private sealed class UniformGrid : System.Windows.Controls.Primitives.UniformGrid;
}
