using ClevoControl.Core.Hardware;
using ClevoControl.Core.Platform;

namespace ClevoControl.Core.Services;

/// <summary>Polls EC sensors, CPU load and memory once per second and keeps a short history.</summary>
public sealed class SensorService : IDisposable
{
    public const int HistoryLength = 120;

    private readonly IClevoHardware _hw;
    private readonly Timer _timer;
    private readonly LinkedList<SensorSnapshot> _history = new();
    private readonly object _sync = new();
    private ulong _lastIdle, _lastTotal;
    private DateTime _lastErrorLog;

    public SensorService(IClevoHardware hw)
    {
        _hw = hw;
        Latest = SensorSnapshot.Empty;
        _timer = new Timer(_ => Poll(), null, 0, 1000);
    }

    public SensorSnapshot Latest { get; private set; }

    /// <summary>While true (sleep / resume) the EC is not polled.</summary>
    public volatile bool Paused;

    private int _polling;

    /// <summary>Raised on a thread-pool thread after each poll.</summary>
    public event Action<SensorSnapshot>? Updated;

    public IReadOnlyList<SensorSnapshot> History
    {
        get { lock (_sync) return _history.ToList(); }
    }

    private void Poll()
    {
        if (Paused) return;
        // Never stack polls: if a driver call hangs, later ticks are skipped instead of piling up.
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var (cpu, gpu, gpu2) = _hw.ReadFans();
            var (memPct, memTotal) = ReadMemory();
            var snap = new SensorSnapshot(DateTime.Now, cpu, gpu, gpu2, ReadCpuLoad(), memPct, memTotal);
            lock (_sync)
            {
                _history.AddLast(snap);
                while (_history.Count > HistoryLength) _history.RemoveFirst();
            }
            Latest = snap;
            Updated?.Invoke(snap);
        }
        catch (Exception ex)
        {
            // Sensor read failed (e.g. during resume) – try again next tick.
            if (DateTime.UtcNow - _lastErrorLog > TimeSpan.FromMinutes(1))
            {
                _lastErrorLog = DateTime.UtcNow;
                Log.Warn("Sensor read failed: " + ex.Message);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private double ReadCpuLoad()
    {
        if (SystemStatus.CpuTimes() is not var (idle, total)) return 0;
        var dIdle = idle - _lastIdle;
        var dTotal = total - _lastTotal;
        _lastIdle = idle;
        _lastTotal = total;
        return dTotal == 0 ? 0 : Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
    }

    private static (double Percent, double TotalGb) ReadMemory() => SystemStatus.Memory();

    public void Dispose() => _timer.Dispose();
}
