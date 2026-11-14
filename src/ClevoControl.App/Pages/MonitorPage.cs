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
    private GpuEngineMonitor? _engines;
    private readonly Dictionary<string, List<GpuAdapterSample>> _adapterHist = [];
    private DispatcherTimer? _timer;
    private readonly List<CpuSample> _cpuHist = [];
    private readonly List<GpuSample> _gpuHist = [];
    private readonly List<Action> _updaters = [];

    private static readonly Color Blue = Color.FromRgb(76, 141, 255), Green = Color.FromRgb(61, 200, 120),
        Orange = Color.FromRgb(255, 170, 60), Purple = Color.FromRgb(180, 120, 255), Red = Color.FromRgb(240, 80, 90);

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Sensoren", "Sensors")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Auslastung, Takt und Temperaturen von Prozessor und Grafik im Detail.", "Load, clocks and temperatures of processor and graphics in detail.")));

        _cpu = new CpuMonitor();
        root.Children.Add(CpuCard());

        _gpu = new GpuMonitor();
        _engines = new GpuEngineMonitor();
        var adapters = _engines.Sample();
        if (adapters.Count == 0 && _gpu.Available) root.Children.Add(GpuCard(null));
        foreach (var a in adapters) root.Children.Add(GpuCard(a));
        if (adapters.Count == 0 && !_gpu.Available)
            root.Children.Add(Ui.Card(Ui.Text(Loc.T("Keine GPU-Leistungsindikatoren verfügbar.", "No GPU performance counters available."), sub: true), "GPU", icon: Icons.Monitor));
        root.Children.Add(Ui.InfoBar(Loc.T("Bei Laptops mit umschaltbarer Grafik hält das Auslesen der NVIDIA-Details die Grafikkarte wach. Deshalb misst diese Seite nur, solange sie geöffnet ist.",
            "On laptops with switchable graphics, reading the NVIDIA details keeps the GPU awake – that is why this page only measures while it is open.")));

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
        if (_engines is not null)
        {
            foreach (var a in _engines.Sample())
            {
                if (!_adapterHist.TryGetValue(a.Luid, out var h)) _adapterHist[a.Luid] = h = [];
                h.Add(a);
                if (h.Count > History) h.RemoveAt(0);
            }
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
        _engines?.Dispose();
        _engines = null;
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

    /// <summary>
    /// One card per graphics adapter. Load/engines/memory come from Windows (all vendors);
    /// for the NVIDIA card NVML adds clocks and power draw.
    /// </summary>
    private Border GpuCard(GpuAdapterSample? adapter)
    {
        var s = new StackPanel();
        var nvidia = adapter is null || adapter.IsNvidia;
        var withNvml = nvidia && _gpu?.Available == true;
        var name = adapter?.Name ?? _gpu?.Name ?? "GPU";
        var kind = adapter is null ? "" : adapter.DedicatedTotalMb > 512 ? Loc.T("dedizierte Grafik", "dedicated graphics") : Loc.T("integrierte Grafik", "integrated graphics");
        var info = kind + (withNvml && _gpu!.DriverVersion is { } d ? $"  ·  {Loc.T("Treiber", "driver")} {d}" : "");
        if (info.Length > 0) s.Children.Add(Ui.Text(info.TrimStart(' ', '·'), 12.5, sub: true, bottom: 12));

        var grid = new UniformGrid { Columns = 2 };
        List<GpuAdapterSample> Hist() => adapter is not null && _adapterHist.TryGetValue(adapter.Luid, out var h) ? h : [];

        if (adapter is not null)
        {
            var color = adapter.DedicatedTotalMb > 512 ? Green : Blue;
            // CUDA work is not fully visible in the Windows engine counters, so NVIDIA cards use NVML's value when available.
            Func<List<double>> load = withNvml
                ? () => _gpuHist.Select(x => x.LoadPercent).ToList()
                : () => Hist().Select(x => x.LoadPercent).ToList();
            grid.Children.Add(BigGraph(Loc.T("Auslastung", "Load"), "%", color, 0, 100, load, margin: true,
                extra: () => Hist().Count > 0 ? TopEngine(Hist()[^1]) : ""));
            if (adapter.DedicatedTotalMb > 512)
                grid.Children.Add(BigGraph(Loc.T("Grafikspeicher (dediziert)", "Dedicated memory"), "MB", Purple, 0, adapter.DedicatedTotalMb,
                    () => Hist().Select(x => x.DedicatedUsedMb).ToList(), "0", true, () => $"/ {adapter.DedicatedTotalMb:0} MB"));
            else
                grid.Children.Add(BigGraph(Loc.T("Gemeinsamer Grafikspeicher", "Shared GPU memory"), "MB", Purple, 0, 2048,
                    () => Hist().Select(x => x.SharedUsedMb).ToList(), "0", true));
        }
        else if (withNvml)
        {
            grid.Children.Add(BigGraph(Loc.T("Auslastung", "Load"), "%", Green, 0, 100, () => _gpuHist.Select(x => x.LoadPercent).ToList(), margin: true));
            grid.Children.Add(BigGraph(Loc.T("Grafikspeicher", "VRAM"), "MB", Purple, 0, _gpuHist.LastOrDefault()?.VramTotalMb ?? 8192,
                () => _gpuHist.Select(x => x.VramUsedMb).ToList(), "0", true, () => _gpuHist.Count > 0 ? $"/ {_gpuHist[^1].VramTotalMb:0} MB" : ""));
        }

        if (withNvml)
        {
            grid.Children.Add(BigGraph(Loc.T("Kerntakt", "Core clock"), "MHz", Orange, 0, 2200, () => _gpuHist.Select(x => x.CoreClockMhz).ToList(), "0", true,
                () => _gpuHist.Count > 0 ? $"{Loc.T("Speicher", "memory")} {_gpuHist[^1].MemoryClockMhz:0} MHz" : ""));
            grid.Children.Add(BigGraph(Loc.T("Leistungsaufnahme", "Power draw"), "W", Red, 0, 150, () => _gpuHist.Select(x => x.PowerW).ToList(), "0", true,
                () => _gpuHist.Count > 0 && _gpuHist[^1].PowerLimitW is { } l ? $"{Loc.T("Limit", "limit")} {l:0} W · P{_gpuHist[^1].PState}" : ""));
        }
        s.Children.Add(grid);

        if (adapter is not null)
        {
            // Per-engine breakdown like Task Manager (3D, video decode/encode, copy, compute …).
            var engines = new WrapPanel();
            var labels = new Dictionary<string, TextBlock>();
            _updaters.Add(() =>
            {
                if (Hist().Count == 0) return;
                foreach (var (type, load) in Hist()[^1].EngineLoad.OrderBy(e => EngineOrder(e.Key)))
                {
                    // Niche engines (security, VR, overlay …) only appear once they are actually used.
                    if (EngineOrder(type) == 9 && load < 1 && !labels.ContainsKey(type)) continue;
                    if (!labels.TryGetValue(type, out var t))
                    {
                        t = Ui.Text("", 12, sub: true);
                        t.Margin = new Thickness(0, 0, 18, 4);
                        labels[type] = t;
                        engines.Children.Add(t);
                    }
                    t.Text = $"{EngineName(type)}: {load:0} %";
                }
            });
            s.Children.Add(engines);
        }
        return Ui.Card(s, name, icon: Icons.Monitor);
    }

    private static string TopEngine(GpuAdapterSample s)
    {
        if (s.EngineLoad.Count == 0 || s.LoadPercent < 1) return "";
        var top = s.EngineLoad.MaxBy(e => e.Value);
        return EngineName(top.Key);
    }

    private static int EngineOrder(string type) => type switch
    {
        "3D" => 0, "Compute" or "Compute_0" or "Compute_1" => 1, "VideoDecode" => 2, "VideoEncode" => 3, "VideoProcessing" => 4, "Copy" => 5, _ => 9,
    };

    private static string EngineName(string type) => type switch
    {
        "3D" => "3D",
        "VideoDecode" => Loc.T("Video-Dekodierung", "Video decode"),
        "VideoEncode" => Loc.T("Video-Kodierung", "Video encode"),
        "VideoProcessing" => Loc.T("Videoverarbeitung", "Video processing"),
        "Copy" => Loc.T("Kopieren", "Copy"),
        "LegacyOverlay" => "Overlay",
        "GDI Render" => "GDI",
        "Other" => Loc.T("Sonstiges", "Other"),
        _ when type.StartsWith("Compute", StringComparison.Ordinal) => "Compute",
        _ => type,
    };

    // ---------------------------------------------------------------- Fans / temps (EC)

    private Border FanTempCard()
    {
        var grid = new UniformGrid { Columns = 2 };
        List<SensorSnapshotView> hist() => Cc.Sensors.History.Select(h => new SensorSnapshotView(h)).ToList();
        grid.Children.Add(BigGraph(Loc.T("CPU-Temperatur", "CPU temperature"), "°C", Red, 30, 100, () => hist().Select(h => (double)h.S.Cpu.TemperatureC).ToList(), margin: true));
        grid.Children.Add(BigGraph(Loc.T("GPU-Temperatur", "GPU temperature"), "°C", Red, 30, 100, () => hist().Select(h => (double)h.S.Gpu.TemperatureC).ToList(), margin: true));
        grid.Children.Add(BigGraph(Loc.T("CPU-Lüfter", "CPU fan"), "RPM", Blue, 0, 6000, () => hist().Select(h => (double)h.S.Cpu.Rpm).ToList(), margin: true));
        grid.Children.Add(BigGraph(Loc.T("GPU-Lüfter", "GPU fan"), "RPM", Blue, 0, 6000, () => hist().Select(h => (double)h.S.Gpu.Rpm).ToList(), margin: true));
        return Ui.Card(grid, Loc.T("Temperaturen & Lüfter", "Temperatures & fans"), icon: Icons.Thermometer);
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
