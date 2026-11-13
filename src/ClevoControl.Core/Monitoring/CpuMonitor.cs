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

    public CpuMonitor()
    {
        Name = "CPU";
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
    public bool Available => _cores.Count > 0;

    public CpuSample Sample()
    {
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
