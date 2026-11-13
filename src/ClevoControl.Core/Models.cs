using System.Globalization;
using System.Text.Json.Serialization;

namespace ClevoControl.Core;

/// <summary>Immutable 24-bit RGB color.</summary>
[JsonConverter(typeof(RgbColorJsonConverter))]
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public override string ToString() => ToHex();

    public static bool TryParse(string? text, out RgbColor color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static RgbColor Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"'{text}' is not a valid color (expected e.g. #FF8800).");

    public RgbColor Scale(double factor)
    {
        factor = Math.Clamp(factor, 0, 1);
        return new RgbColor((byte)Math.Round(R * factor), (byte)Math.Round(G * factor), (byte)Math.Round(B * factor));
    }

    public static RgbColor Lerp(RgbColor a, RgbColor b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new RgbColor(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>h in degrees [0,360), s and v in [0,1].</summary>
    public static RgbColor FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new RgbColor((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public (double H, double S, double V) ToHsv()
    {
        double r = R / 255d, g = G / 255d, b = B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    /// <summary>Approximate color of a black body at the given temperature (Kelvin, 1000–40000).</summary>
    public static RgbColor FromKelvin(double kelvin)
    {
        var t = Math.Clamp(kelvin, 1000, 40000) / 100;
        double r, g, b;
        if (t <= 66)
        {
            r = 255;
            g = 99.4708025861 * Math.Log(t) - 161.1195681661;
            b = t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        }
        else
        {
            r = 329.698727446 * Math.Pow(t - 60, -0.1332047592);
            g = 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
            b = 255;
        }
        static byte C(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        return new RgbColor(C(r), C(g), C(b));
    }
}

public sealed class RgbColorJsonConverter : JsonConverter<RgbColor>
{
    public override RgbColor Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
        => RgbColor.TryParse(reader.GetString(), out var c) ? c : RgbColor.White;

    public override void Write(System.Text.Json.Utf8JsonWriter writer, RgbColor value, System.Text.Json.JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToHex());
}

/// <summary>Keyboard backlight hardware type as reported by the EC (spec buffer byte 0x0F).</summary>
public enum KeyboardType
{
    Unknown,
    None,
    WhiteOnly,
    SingleZoneRgb,
    ThreeZoneRgb,
    PerKeyRgb,
}

/// <summary>Effects that run inside the embedded controller itself (no CPU usage).</summary>
public enum HardwareEffect
{
    Breathe,
    Cycle,
    Wave,
    Dance,
    Flash,
    RandomColor,
    Tempo,
}

public enum FanMode
{
    Auto,
    Max,
    Custom,
}

/// <summary>Clevo performance profiles (the four modes of the Clevo Control Center).</summary>
public enum PerformanceProfile
{
    Quiet,
    PowerSaving,
    Performance,
    Entertainment,
}

/// <summary>Windows 10/11 power mode slider ("power overlay").</summary>
public enum WindowsPowerMode
{
    BestEfficiency,
    Balanced,
    BestPerformance,
}

/// <summary>Clevo "FlexiCharger": charging starts below <see cref="Start"/> % and stops at <see cref="Stop"/> %.</summary>
public sealed record ChargeLimitInfo(bool Enabled, int Start, int Stop, IReadOnlyList<int> StartOptions, IReadOnlyList<int> StopOptions);

public sealed record FanReading(int Rpm, int DutyPercent, int TemperatureC);

public sealed record SensorSnapshot(
    DateTime Timestamp,
    FanReading Cpu,
    FanReading Gpu,
    FanReading? Gpu2,
    double CpuLoadPercent,
    double MemoryUsedPercent,
    double MemoryTotalGb)
{
    public static SensorSnapshot Empty => new(DateTime.Now, new(0, 0, 0), new(0, 0, 0), null, 0, 0, 0);
}
