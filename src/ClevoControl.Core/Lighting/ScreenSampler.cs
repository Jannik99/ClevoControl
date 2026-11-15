using ClevoControl.Core.Platform;

namespace ClevoControl.Core.Lighting;

public sealed record MonitorInfo(int Index, string Name, int X, int Y, int Width, int Height, bool Primary);

/// <summary>
/// Samples the average color of a monitor from a tiny downscaled copy of the screen
/// (Windows: GDI, Linux: X11 or grim). Cheap, works with every GPU; DRM-protected video may appear black.
/// </summary>
public sealed class ScreenSampler : IDisposable
{
    private const int W = 48, H = 27;
    private readonly IScreenGrabber? _grabber = ScreenGrabber.Create();
    private readonly uint[] _pixels = new uint[W * H];

    /// <summary>False if this desktop does not allow screen capture (see <see cref="ScreenGrabber.UnsupportedReason"/>).</summary>
    public bool Available => _grabber is not null;

    public static List<MonitorInfo> GetMonitors()
    {
        try { return ScreenGrabber.Monitors(); }
        catch { return []; }
    }

    /// <summary>
    /// Returns a representative color: saturated, bright pixels are weighted more than grey
    /// ones, and black letterbox bars are ignored.
    /// </summary>
    public RgbColor? Sample(MonitorInfo monitor)
    {
        if (_grabber is null || !_grabber.Grab(monitor.X, monitor.Y, monitor.Width, monitor.Height, W, H, _pixels)) return null;
        return Representative(_pixels);
    }

    internal static RgbColor Representative(ReadOnlySpan<uint> pixels)
    {
        double r = 0, g = 0, b = 0, weight = 0, plainR = 0, plainG = 0, plainB = 0;
        var n = pixels.Length;
        foreach (var p in pixels)
        {
            double pb = p & 0xFF, pg = (p >> 8) & 0xFF, pr = (p >> 16) & 0xFF;
            plainR += pr; plainG += pg; plainB += pb;
            var max = Math.Max(pr, Math.Max(pg, pb));
            if (max < 12) continue; // letterbox / black
            var min = Math.Min(pr, Math.Min(pg, pb));
            var sat = (max - min) / max;
            var w = 0.15 + sat * sat * 3 + max / 255.0;
            r += pr * w; g += pg * w; b += pb * w; weight += w;
        }
        if (weight <= 0) return RgbColor.Black;

        // Keep overall brightness of the plain average, hue/saturation of the weighted one.
        var avgLum = (plainR + plainG + plainB) / (3.0 * n);
        r /= weight; g /= weight; b /= weight;
        var lum = (r + g + b) / 3;
        var scale = lum > 0 ? Math.Clamp(Math.Max(avgLum, 40) / lum, 0.2, 1.6) : 1;
        static byte C(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        return new RgbColor(C(r * scale), C(g * scale), C(b * scale));
    }

    public void Dispose() => _grabber?.Dispose();
}
