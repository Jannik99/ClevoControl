using ClevoControl.Core.Platform;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Lighting;

/// <summary>
/// Reads a small screen area (e.g. a health bar). Works with any game that runs in
/// borderless/windowed mode (or exclusive fullscreen the OS can still capture).
/// Windows: GDI; Linux: X11 or grim.
/// </summary>
public sealed class RegionSampler : IDisposable
{
    private const int MaxW = 160, MaxH = 24;
    private readonly IScreenGrabber? _grabber = ScreenGrabber.Create();
    private readonly uint[] _pixels = new uint[MaxW * MaxH];
    private int _w = 1;

    public bool Available => _grabber is not null;

    private bool Grab(MonitorInfo m, ScreenRegionSettings r, out int w, out int h)
    {
        var sx = m.X + (int)Math.Round(r.X * m.Width);
        var sy = m.Y + (int)Math.Round(r.Y * m.Height);
        var sw = Math.Max(1, (int)Math.Round(r.Width * m.Width));
        var sh = Math.Max(1, (int)Math.Round(r.Height * m.Height));
        w = Math.Clamp(sw, 1, MaxW);
        h = Math.Clamp(sh, 1, MaxH);
        _w = w;
        return _grabber is not null && _grabber.Grab(sx, sy, sw, sh, w, h, _pixels);
    }

    private RgbColor Pixel(int x, int y)
    {
        var p = _pixels[y * _w + x];
        return new RgbColor((byte)(p >> 16), (byte)(p >> 8), (byte)p);
    }

    /// <summary>Average color of the area.</summary>
    public RgbColor? AverageColor(MonitorInfo m, ScreenRegionSettings r)
    {
        if (!Grab(m, r, out var w, out var h)) return null;
        long sr = 0, sg = 0, sb = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = Pixel(x, y);
            sr += c.R; sg += c.G; sb += c.B;
        }
        var n = w * h;
        return new RgbColor((byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
    }

    /// <summary>The most saturated/bright color in the area – used to learn the bar color when it is full.</summary>
    public RgbColor? DominantColor(MonitorInfo m, ScreenRegionSettings r)
    {
        if (!Grab(m, r, out var w, out var h)) return null;
        var buckets = new Dictionary<int, (int Count, long R, long G, long B)>();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = Pixel(x, y);
            var key = (c.R >> 5) << 6 | (c.G >> 5) << 3 | (c.B >> 5);
            buckets.TryGetValue(key, out var v);
            buckets[key] = (v.Count + 1, v.R + c.R, v.G + c.G, v.B + c.B);
        }
        var best = buckets.Values
            .OrderByDescending(v =>
            {
                var c = new RgbColor((byte)(v.R / v.Count), (byte)(v.G / v.Count), (byte)(v.B / v.Count));
                var (_, s, val) = c.ToHsv();
                return v.Count * (0.2 + s) * (0.2 + val);
            })
            .First();
        return new RgbColor((byte)(best.R / best.Count), (byte)(best.G / best.Count), (byte)(best.B / best.Count));
    }

    /// <summary>Fraction (0..1) of the bar that has the bar color.</summary>
    public double? BarFill(MonitorInfo m, ScreenRegionSettings r)
    {
        if (!Grab(m, r, out var w, out var h)) return null;
        var len = r.VerticalBar ? h : w;
        var cross = r.VerticalBar ? w : h;
        var matching = 0;
        for (var i = 0; i < len; i++)
        {
            var hits = 0;
            for (var j = 0; j < cross; j++)
            {
                var c = r.VerticalBar ? Pixel(j, i) : Pixel(i, j);
                if (Distance(c, r.BarColor) <= r.Tolerance) hits++;
            }
            if (hits * 2 >= cross) matching++;
        }
        return (double)matching / len;
    }

    private static double Distance(RgbColor a, RgbColor b)
    {
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db) / 441.7;
    }

    public void Dispose() => _grabber?.Dispose();
}
