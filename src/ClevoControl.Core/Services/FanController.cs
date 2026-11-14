using ClevoControl.Core.Hardware;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Services;

/// <summary>
/// Applies the fan mode. In custom mode it runs a software fan curve with hard safety rules:
/// <list type="bullet">
/// <item>never below 70 % at ≥ 80 °C and always 100 % at ≥ 88 °C, whatever the curve says,</item>
/// <item>falls back to firmware auto mode if sensor data looks invalid,</item>
/// <item>restores auto mode on exit, sleep and crashes (where possible).</item>
/// </list>
/// </summary>
public sealed class FanController : IDisposable
{
    private readonly IClevoHardware _hw;
    private readonly SensorService _sensors;
    private FanSettings _settings;
    private double _cpuDuty = 100, _gpuDuty = 100;
    private (byte, byte)? _lastSent;
    private DateTime _lastWrite;
    private FanMode? _appliedMode;
    private bool _suspended;
    private readonly object _sync = new();
    private readonly Action? _persist;

    public FanController(IClevoHardware hw, SensorService sensors, FanSettings settings, Action? persist = null)
    {
        _hw = hw;
        _sensors = sensors;
        _settings = settings;
        _persist = persist;
        _sensors.Updated += OnSensors;
        Apply(settings);
    }

    /// <summary>Last duty (percent) requested by the custom curve, for the UI.</summary>
    public (int Cpu, int Gpu) CurveDuty => ((int)_cpuDuty, (int)_gpuDuty);

    public void Apply(FanSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
            if (_suspended) return;
            if (settings.Mode != _appliedMode)
            {
                _lastSent = null;
                switch (settings.Mode)
                {
                    case FanMode.Max:
                        _hw.SetFanMode(FanMode.Max);
                        MarkOverride(true);
                        break;
                    case FanMode.Auto:
                        // Only touch the EC if we actually overrode it (also after a crash). Otherwise the
                        // firmware is already in charge and there is nothing to send.
                        if (settings.OverrideActive) RestoreAutoUnlocked();
                        break;
                    default:
                        _cpuDuty = _gpuDuty = 100; // custom: start high, ramp down gently
                        break;
                }
                Log.Info($"Fan mode {_appliedMode?.ToString() ?? "-"} -> {settings.Mode}");
                _appliedMode = settings.Mode;
            }
        }
    }

    private void OnSensors(SensorSnapshot snap)
    {
        lock (_sync)
        {
            if (_suspended || _settings.Mode != FanMode.Custom) return;
            var cpuT = snap.Cpu.TemperatureC;
            var gpuT = snap.Gpu.TemperatureC;
            if (cpuT is <= 5 or > 110)
            {
                // Implausible reading – let the firmware take over.
                Log.Warn($"Implausible CPU temperature {cpuT} 00b0C, handing fans back to firmware");
                RestoreAutoUnlocked();
                _lastSent = null;
                return;
            }
            if (_settings.GpuFollowsCpu) gpuT = Math.Max(gpuT, cpuT);

            _cpuDuty = Step(_cpuDuty, Safe(Evaluate(_settings.CpuCurve, cpuT), cpuT));
            _gpuDuty = Step(_gpuDuty, Safe(Evaluate(_settings.GpuCurve, gpuT), gpuT));

            var cpu = (byte)Math.Round(_cpuDuty * 2.55);
            var gpu = (byte)Math.Round(_gpuDuty * 2.55);
            // Re-send every 10 s even without change, the EC may have taken over in between.
            if (_lastSent != (cpu, gpu) || DateTime.UtcNow - _lastWrite > TimeSpan.FromSeconds(10))
            {
                _hw.SetFanDuty(cpu, gpu, gpu);
                MarkOverride(true);
                _lastSent = (cpu, gpu);
                _lastWrite = DateTime.UtcNow;
            }
        }
    }

    private double Step(double current, double target)
    {
        if (target >= current) return target; // spin up immediately
        return Math.Max(target, current - Math.Max(1, _settings.RampDownPerSecond));
    }

    /// <summary>Hard safety floor independent of the user curve.</summary>
    public static double Safe(double duty, int temperature)
    {
        if (temperature >= 88) return 100;
        if (temperature >= 80) return Math.Max(duty, 70);
        if (temperature >= 70) return Math.Max(duty, 35);
        return duty;
    }

    public static double Evaluate(IReadOnlyList<FanCurvePoint> curve, int temperature)
    {
        if (curve.Count == 0) return 100;
        var pts = curve.OrderBy(p => p.Temperature).ToList();
        if (temperature <= pts[0].Temperature) return pts[0].Duty;
        for (var i = 1; i < pts.Count; i++)
        {
            if (temperature <= pts[i].Temperature)
            {
                var a = pts[i - 1];
                var b = pts[i];
                var f = (temperature - a.Temperature) / (double)Math.Max(1, b.Temperature - a.Temperature);
                return Math.Clamp(a.Duty + (b.Duty - a.Duty) * f, 0, 100);
            }
        }
        return pts[^1].Duty;
    }

    /// <summary>Before sleep: hand control back to the firmware (only if we had overridden it).</summary>
    public void Suspend()
    {
        lock (_sync)
        {
            _suspended = true;
            if (_settings.OverrideActive) RestoreAutoUnlocked();
            _appliedMode = null;
        }
    }

    /// <summary>After resume (called delayed by the ControlCenter, see OnResume).</summary>
    public void Resume()
    {
        lock (_sync)
        {
            _suspended = false;
            _appliedMode = null;
        }
        Apply(_settings);
    }

    public void SafeRestoreAuto()
    {
        lock (_sync) RestoreAutoUnlocked();
    }

    private void RestoreAutoUnlocked()
    {
        try
        {
            _hw.SetFanMode(FanMode.Auto);
            MarkOverride(false);
        }
        catch (Exception ex)
        {
            Log.Error("Could not hand fans back to firmware", ex);
        }
    }

    private void MarkOverride(bool active)
    {
        if (_settings.OverrideActive == active) return;
        _settings.OverrideActive = active;
        _persist?.Invoke();
    }

    public void Dispose()
    {
        _sensors.Updated -= OnSensors;
        lock (_sync)
        {
            if (_settings.OverrideActive) RestoreAutoUnlocked();
        }
    }
}
