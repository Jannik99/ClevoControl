namespace ClevoControl.Core.Hardware;

/// <summary>
/// Fake hardware used when no Clevo driver is present (e.g. on other machines or during
/// development). Produces plausible sensor values and remembers the last keyboard color.
/// </summary>
public sealed class SimulatedHardware : IClevoHardware
{
    private readonly Random _rng = new();
    private double _cpuTemp = 55, _gpuTemp = 48;
    private FanMode _fanMode = FanMode.Auto;
    private (byte Cpu, byte Gpu)? _duty;
    private PerformanceProfile _profile = PerformanceProfile.Entertainment;

    public SimulatedHardware(string reason) => Description = $"Simulation – {reason}";

    public bool IsReal => false;
    public string Description { get; }
    public KeyboardType KeyboardType => KeyboardType.SingleZoneRgb;

    public RgbColor LastColor { get; private set; } = RgbColor.White;
    public byte LastBrightness { get; private set; } = 255;
    public bool KeyboardOn { get; private set; } = true;

    public (FanReading Cpu, FanReading Gpu, FanReading? Gpu2) ReadFans()
    {
        _cpuTemp = Math.Clamp(_cpuTemp + (_rng.NextDouble() - 0.5) * 3, 38, 92);
        _gpuTemp = Math.Clamp(_gpuTemp + (_rng.NextDouble() - 0.5) * 2, 35, 85);
        int Duty(double t, byte? fixedDuty) => _fanMode switch
        {
            FanMode.Max => 100,
            _ when fixedDuty is { } d => (int)Math.Round(d * 100 / 255.0),
            _ => (int)Math.Clamp((t - 40) * 2, 0, 100),
        };
        var cd = Duty(_cpuTemp, _duty?.Cpu);
        var gd = Duty(_gpuTemp, _duty?.Gpu);
        return (new FanReading(cd * 52, cd, (int)_cpuTemp), new FanReading(gd * 50, gd, (int)_gpuTemp), null);
    }

    public void SetKeyboardColor(RgbColor color) => LastColor = color;
    public void SetKeyboardZoneColor(int zone, RgbColor color) => LastColor = color;
    public void SetKeyboardBrightness(byte level) => LastBrightness = level;
    public void SetKeyboardPower(bool on) => KeyboardOn = on;
    public void SetKeyboardHardwareEffect(HardwareEffect effect) { }

    public void SetFanMode(FanMode mode)
    {
        _fanMode = mode;
        _duty = null;
    }

    public void SetFanDuty(byte cpu, byte gpu1, byte gpu2) => _duty = (cpu, gpu1);

    public void SetPerformanceProfile(PerformanceProfile profile) => _profile = profile;
    public PerformanceProfile? ReadStoredPerformanceProfile() => _profile;
    private ChargeLimitInfo _charge = new(false, 60, 90, [40, 50, 60, 70, 80], [60, 70, 80, 90, 100]);

    public ChargeLimitInfo? ReadChargeLimit() => _charge;

    public void SetChargeLimit(bool enabled, int start, int stop) => _charge = _charge with { Enabled = enabled, Start = start, Stop = stop };

    public byte[] ReadSpecBuffer() => new byte[256];
    public byte[] ReadFanBuffer() => new byte[256];
}
