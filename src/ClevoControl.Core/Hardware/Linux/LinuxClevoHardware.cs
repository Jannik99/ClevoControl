using ClevoControl.Core.Hardware.PerKey;
using System.Globalization;
using ClevoControl.Core.Platform;

namespace ClevoControl.Core.Hardware.Linux;

/// <summary>
/// Clevo laptops on Linux through the open-source tuxedo-drivers kernel modules
/// (clevo_acpi / clevo_wmi + tuxedo_io). Both drivers send the same EC commands as Clevo's
/// Windows driver, so behaviour matches <see cref="ClevoHardware"/> as far as the drivers expose it:
/// <list type="bullet">
/// <item>Keyboard: LED class devices <c>/sys/class/leds/rgb:kbd_backlight*</c> or <c>white:kbd_backlight</c>.</item>
/// <item>Fans, temperatures, performance profile: ioctls on <c>/dev/tuxedo_io</c>.</item>
/// <item>Charge limit: the standard <c>charge_control_*_threshold</c> battery attributes, if present.</item>
/// </list>
/// Not available: fan RPM (the driver only reports duty), the EC's own keyboard animations and
/// reading back the active performance profile.
/// </summary>
public sealed class LinuxClevoHardware : IClevoHardware, IDisposable
{
    private readonly ITuxedoIo _io;
    private readonly List<string> _rgbLeds;
    private readonly string? _whiteLed;
    private readonly int _whiteMax;
    private byte _brightness = 255;
    private bool _on = true;
    private bool _effectWarned;

    public LinuxClevoHardware(ITuxedoIo io)
    {
        _io = io;
        var interfaceName = "?";
        var version = "?";
        try { interfaceName = io.InterfaceName(); } catch { }
        try { version = io.ModuleVersion(); } catch { }
        Description = $"tuxedo-drivers {version} ({(interfaceName.Length > 0 ? interfaceName : "no Clevo interface")})";

        var leds = LinuxSys.P("sys/class/leds");
        var names = Directory.Exists(leds)
            ? Directory.GetDirectories(leds).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList()
            : [];
        // Kernel naming: the first zone is "rgb:kbd_backlight", further zones get "_1", "_2".
        _rgbLeds = names.Where(n => n.StartsWith("rgb:kbd_backlight", StringComparison.Ordinal)).Select(n => Path.Combine(leds, n)).ToList();
        _whiteLed = names.Where(n => n.StartsWith("white:kbd_backlight", StringComparison.Ordinal)).Select(n => Path.Combine(leds, n)).FirstOrDefault();
        _whiteMax = _whiteLed is null ? 0 : ReadInt(Path.Combine(_whiteLed, "max_brightness")) ?? 2;

        // Per-key keyboards (tuxedo-drivers ite_8291 / ite_829x) also register their 120/126 LEDs as rgb:kbd_backlight*.
        try
        {
            _perKey = LinuxSysfsPerKey.TryCreate(_rgbLeds);
        }
        catch (Exception ex)
        {
            Log.Warn("Per-key: sysfs detection failed: " + ex.Message);
        }
        if (_perKey is not null) _rgbLeds = _rgbLeds.Except(_perKey.Leds).ToList();
        KeyboardType = ClassifyKeyboard(_rgbLeds.Count, _whiteLed is not null, _perKey is not null);
    }

    /// <summary>
    /// Keyboard type from what the drivers expose: a detected per-key controller, exactly 3 RGB LEDs (clevo_leds 3-zone),
    /// otherwise 1 zone (also for the 4 zones of an ITE 8291 ce00 rev 0002, which are all set to the same color).
    /// </summary>
    internal static KeyboardType ClassifyKeyboard(int rgbLeds, bool white, bool perKey) => perKey ? KeyboardType.PerKeyRgb : rgbLeds switch
    {
        3 => KeyboardType.ThreeZoneRgb,
        >= 1 => KeyboardType.SingleZoneRgb,
        _ => white ? KeyboardType.WhiteOnly : KeyboardType.None,
    };

    private readonly LinuxSysfsPerKey? _perKey;

    public IPerKeyKeyboard? PerKey => _perKey;

    public bool IsReal => true;

    public string Description { get; }

    public KeyboardType KeyboardType { get; }

    // ---- Fans ---------------------------------------------------------------------------

    internal static FanReading DecodeFanInfo(uint raw)
    {
        var duty = (int)(raw & 0xFF);
        var temp = (sbyte)((raw >> 16) & 0xFF);
        return new FanReading(0, (int)Math.Round(duty * 100 / 255.0), Math.Max(0, (int)temp));
    }

    public (FanReading Cpu, FanReading Gpu, FanReading? Gpu2) ReadFans()
    {
        var cpu = DecodeFanInfo(_io.ReadFanInfo(0));
        var gpu = DecodeFanInfo(_io.ReadFanInfo(1));
        FanReading? gpu2 = null;
        try
        {
            var third = DecodeFanInfo(_io.ReadFanInfo(2));
            if (third.TemperatureC > 1) gpu2 = third; // the driver reports <= 1 °C for a missing fan
        }
        catch { }
        return (cpu, gpu, gpu2);
    }

    public void SetFanMode(FanMode mode)
    {
        if (mode == FanMode.Max) _io.SetFanSpeed(0xFFFFFF);
        else _io.SetFanAuto(0x0F); // all fans back to EC control
    }

    // Note: the driver turns duties below 31 into 0 (fan off) and raises 31..62 to 63, like the EC would.
    public void SetFanDuty(byte cpu, byte gpu1, byte gpu2) => _io.SetFanSpeed(cpu | ((uint)gpu1 << 8) | ((uint)gpu2 << 16));

    public void SetPerformanceProfile(PerformanceProfile profile) => _io.SetPerformanceProfile(ClevoHardware.ProfileToEc(profile));

    // tuxedo_io has no getter for the profile; ClevoControl's own settings remember it.
    public PerformanceProfile? ReadStoredPerformanceProfile() => null;

    // ---- Keyboard -----------------------------------------------------------------------

    /// <summary>
    /// The driver dims red and blue on 1-zone keyboards (×180/255, ×200/255) to balance the LEDs.
    /// ClevoControl already applies its own (user-adjustable) white balance, so the driver's scaling is undone.
    /// </summary>
    internal static RgbColor CompensateDriverGain(RgbColor c)
    {
        // Exact integer ratios (v × 255 / 180) avoid floating-point drift at x.5.
        static byte Up(byte v, int driverFactor) => (byte)Math.Min(255, Math.Round(v * 255.0 / driverFactor, MidpointRounding.AwayFromZero));
        return new RgbColor(Up(c.R, 180), c.G, Up(c.B, 200));
    }

    public void SetKeyboardColor(RgbColor color)
    {
        _perKey?.SetAll(color);
        SetZones(color);
    }

    private void SetZones(RgbColor color)
    {
        if (_rgbLeds.Count == 0) return;
        for (var z = 0; z < _rgbLeds.Count; z++) SetKeyboardZoneColor(z, color);
    }

    public void SetKeyboardZoneColor(int zone, RgbColor c)
    {
        if (_rgbLeds.Count == 0) return;
        var led = _rgbLeds[Math.Clamp(zone, 0, _rgbLeds.Count - 1)];
        // Only clevo_leds (1 or 2 LEDs) scales the channels; other drivers with more zones do not.
        if (KeyboardType == KeyboardType.SingleZoneRgb && _rgbLeds.Count < 3) c = CompensateDriverGain(c);
        Write(Path.Combine(led, "multi_intensity"), FormatIntensity(ReadIndex(led), c));
    }

    /// <summary>Formats the color in the channel order given by the LED's multi_index file.</summary>
    internal static string FormatIntensity(string? multiIndex, RgbColor c)
    {
        var order = (multiIndex ?? "red green blue").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', order.Select(ch => ch switch
        {
            "red" => c.R,
            "green" => c.G,
            "blue" => c.B,
            _ => 0,
        }));
    }

    private static string? ReadIndex(string led)
    {
        try { return File.ReadAllText(Path.Combine(led, "multi_index")).Trim(); }
        catch { return null; }
    }

    public void SetKeyboardBrightness(byte level)
    {
        _brightness = level;
        _perKey?.SetBrightness(level);
        if (_on) WriteBrightness(level);
    }

    public void SetKeyboardPower(bool on)
    {
        _on = on;
        _perKey?.SetPower(on);
        WriteBrightness(on ? _brightness : (byte)0);
    }

    private void WriteBrightness(byte level)
    {
        foreach (var led in _rgbLeds) Write(Path.Combine(led, "brightness"), level.ToString(CultureInfo.InvariantCulture));
        if (_whiteLed is not null)
        {
            // White keyboards only have a few steps (max_brightness 2 or 5).
            var steps = (int)Math.Round(level / 255.0 * _whiteMax);
            if (level > 0 && steps == 0) steps = 1;
            Write(Path.Combine(_whiteLed, "brightness"), steps.ToString(CultureInfo.InvariantCulture));
        }
    }

    public void SetKeyboardHardwareEffect(HardwareEffect effect)
    {
        // The Linux drivers do not expose the EC's own animations; ClevoControl's software effects still work.
        if (_effectWarned) return;
        _effectWarned = true;
        Log.Warn($"Hardware keyboard effect {effect} is not available on Linux (tuxedo-drivers does not expose it).");
    }

    // ---- Charge limit -------------------------------------------------------------------

    private static string? Battery() => LinuxSys.BatteryDir();

    public ChargeLimitInfo? ReadChargeLimit()
    {
        if (Battery() is not { } b) return null;
        var start = ReadInt(Path.Combine(b, "charge_control_start_threshold"));
        var stop = ReadInt(Path.Combine(b, "charge_control_end_threshold"));
        if (start is null || stop is null) return null;
        var starts = ReadOptions(b, "charge_control_start_available_thresholds") ?? Enumerable.Range(4, 16).Select(i => i * 5).ToList(); // 20..95
        var stops = ReadOptions(b, "charge_control_end_available_thresholds") ?? Enumerable.Range(8, 13).Select(i => i * 5).ToList();     // 40..100
        return new ChargeLimitInfo(stop < 100, start.Value, stop.Value, starts, stops);
    }

    private static List<int>? ReadOptions(string battery, string name)
    {
        try
        {
            var values = File.ReadAllText(Path.Combine(battery, name))
                .Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var v) ? v : -1).Where(v => v is >= 0 and <= 100).ToList();
            return values.Count > 0 ? values : null;
        }
        catch
        {
            return null;
        }
    }

    public void SetChargeLimit(bool enabled, int start, int stop)
    {
        var info = ReadChargeLimit() ?? throw new NotSupportedException("This battery has no charge thresholds in /sys/class/power_supply.");
        var b = Battery()!;
        if (!enabled) { start = info.StartOptions.Min(); stop = 100; }
        else if (!info.StartOptions.Contains(start) || !info.StopOptions.Contains(stop) || start >= stop)
            throw new ArgumentException("Charge thresholds must be one of the offered values, start < stop.");

        // The kernel may reject start >= current stop (or stop <= current start), so order the writes.
        var startFile = Path.Combine(b, "charge_control_start_threshold");
        var stopFile = Path.Combine(b, "charge_control_end_threshold");
        if (start < info.Stop)
        {
            Write(startFile, start.ToString(CultureInfo.InvariantCulture));
            Write(stopFile, stop.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Write(stopFile, stop.ToString(CultureInfo.InvariantCulture));
            Write(startFile, start.ToString(CultureInfo.InvariantCulture));
        }
    }

    // ---- Diagnostics --------------------------------------------------------------------

    public byte[] ReadSpecBuffer()
    {
        var buf = new byte[256];
        var text = System.Text.Encoding.ASCII.GetBytes($"{_io.InterfaceName()};kbd={KeyboardType};leds={_rgbLeds.Count}");
        text.AsSpan(0, Math.Min(text.Length, buf.Length)).CopyTo(buf);
        return buf;
    }

    public byte[] ReadFanBuffer()
    {
        var buf = new byte[256];
        for (var i = 0; i < 3; i++)
        {
            try { BitConverter.TryWriteBytes(buf.AsSpan(i * 4), _io.ReadFanInfo(i)); } catch { }
        }
        return buf;
    }

    // ---- helpers ------------------------------------------------------------------------

    private static int? ReadInt(string path)
    {
        try { return int.TryParse(File.ReadAllText(path).Trim(), out var v) ? v : null; }
        catch { return null; }
    }

    private static void Write(string path, string value)
    {
        try
        {
            File.WriteAllText(path, value);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException($"No permission to write {path} – install the udev rule from contrib/linux (see docs/LINUX.md).");
        }
    }

    public void Dispose() => _io.Dispose();
}
