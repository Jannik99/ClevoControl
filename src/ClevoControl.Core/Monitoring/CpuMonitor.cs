using System.Diagnostics;
using System.Management;

namespace ClevoControl.Core.Monitoring;

public sealed record CoreSample(string Name, double LoadPercent);

public sealed record CpuSample(DateTime Time, double TotalLoad, double FrequencyMhz, IReadOnlyList<CoreSample> Cores);

/// <summary>
/// Per-logical-processor load and the current effective clock, via Windows performance counters
/// ("Processor Information"). Only runs while someone needs it (detail page open).
/// </summary>
public sealed class CpuMonitor : IDisposable
{
    private readonly List<(string Name, PerformanceCounter Counter)> _cores = [];
    private readonly PerformanceCounter? _total;
    private readonly PerformanceCounter? _performance;
    private readonly double _baseMhz;

    // ---- Linux: /proc/stat per CPU + cpufreq ----------------------------------------------
    private readonly bool _linux = OperatingSystem.IsLinux();
    private Dictionary<string, (ulong Idle, ulong Total)> _lastLinux = [];
    private (ulong Idle, ulong Total) _lastLinuxTotal;

    private CpuSample SampleLinux()
    {
        var stat = Platform.LinuxSys.ReadText("proc/stat");
        var per = Platform.LinuxHardwareInfo.ParsePerCpu(stat);
        var cores = new List<CoreSample>();
        var next = new Dictionary<string, (ulong, ulong)>();
        foreach (var (name, idle, total) in per)
        {
            var load = _lastLinux.TryGetValue(name, out var prev) && total > prev.Total
                ? 100.0 * (1 - (double)(idle - prev.Idle) / (total - prev.Total)) : 0;
            cores.Add(new CoreSample("#" + name[3..], Math.Clamp(load, 0, 100)));
            next[name] = (idle, total);
        }
        _lastLinux = next;
        double totalLoad = 0;
        if (stat is not null && Platform.LinuxSys.ParseProcStat(stat) is var (ti, tt))
        {
            if (tt > _lastLinuxTotal.Total) totalLoad = Math.Clamp(100.0 * (1 - (double)(ti - _lastLinuxTotal.Idle) / (tt - _lastLinuxTotal.Total)), 0, 100);
            _lastLinuxTotal = (ti, tt);
        }
        return new CpuSample(DateTime.Now, totalLoad, Platform.LinuxHardwareInfo.CurrentMhz(), cores);
    }

    public CpuMonitor()
    {
        Name = "CPU";
        if (_linux)
        {
            var info = Platform.LinuxHardwareInfo.Cpu();
            Name = info.Name;
            PhysicalCores = info.Physical;
            LogicalCores = info.Logical;
            _baseMhz = info.BaseMhz;
            SampleLinux(); // prime the deltas
            return;
        }
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var o in s.Get())
            {
                Name = (o["Name"] as string)?.Trim() ?? Name;
                _baseMhz = Convert.ToDouble(o["MaxClockSpeed"]);
                PhysicalCores = Convert.ToInt32(o["NumberOfCores"]);
                LogicalCores = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                break;
            }
        }
        catch { }

        try
        {
            var cat = new PerformanceCounterCategory("Processor Information");
            var instances = cat.GetInstanceNames()
                .Where(i => !i.Contains("_Total", StringComparison.Ordinal))
                .Select(i => (Name: i, Parts: i.Split(',')))
                .Where(x => x.Parts.Length == 2 && int.TryParse(x.Parts[0], out _) && int.TryParse(x.Parts[1], out _))
                .OrderBy(x => int.Parse(x.Parts[0])).ThenBy(x => int.Parse(x.Parts[1]))
                .ToList();
            var index = 0;
            foreach (var (inst, _) in instances)
                _cores.Add(($"#{index++}", new PerformanceCounter("Processor Information", "% Processor Utility", inst, true)));
            _total = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
            _performance = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
            foreach (var c in _cores) c.Counter.NextValue();
            _total.NextValue();
            _performance.NextValue();
        }
        catch
        {
            // Counters unavailable (rare, e.g. corrupted perf counter registry).
        }
    }

    public string Name { get; }
    public int PhysicalCores { get; }
    public int LogicalCores { get; }
    public double BaseMhz => _baseMhz;
    public bool Available => _linux || _cores.Count > 0;

    public CpuSample Sample()
    {
        if (_linux) return SampleLinux();
        var cores = _cores.Select(c => new CoreSample(c.Name, Clamp(c.Counter.NextValue()))).ToList();
        var total = _total is null ? 0 : Clamp(_total.NextValue());
        var mhz = _performance is null || _baseMhz <= 0 ? 0 : _baseMhz * _performance.NextValue() / 100.0;
        return new CpuSample(DateTime.Now, total, mhz, cores);
    }

    private static double Clamp(float v) => Math.Clamp(v, 0, 100);

    public void Dispose()
    {
        foreach (var c in _cores) c.Counter.Dispose();
        _total?.Dispose();
        _performance?.Dispose();
    }
}
