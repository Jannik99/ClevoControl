using ClevoControl.Core.Native;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Lighting;

/// <summary>
/// Reads a small screen area (e.g. a health bar). Works with any game that runs in
/// borderless/windowed mode or exclusive fullscreen that Windows can still capture.
/// </summary>
public sealed unsafe class RegionSampler : IDisposable
{
    private const int MaxW = 160, MaxH = 24;
    private readonly IntPtr _memDc, _bitmap, _oldBitmap;
    private readonly uint* _pixels;

    public RegionSampler()
    {
        var screen = Win32.GetDC(IntPtr.Zero);
        _memDc = Win32.CreateCompatibleDC(screen);
        Win32.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Win32.BITMAPINFOHEADER), biWidth = MaxW, biHeight = -MaxH, biPlanes = 1, biBitCount = 32,
        };
        _bitmap = Win32.CreateDIBSection(_memDc, bmi, 0, out var bits, IntPtr.Zero, 0);
        _pixels = (uint*)bits;
        _oldBitmap = Win32.SelectObject(_memDc, _bitmap);
        Win32.SetStretchBltMode(_memDc, Win32.HALFTONE);
        Win32.SetBrushOrgEx(_memDc, 0, 0, IntPtr.Zero);
    }

    private bool Grab(MonitorInfo m, ScreenRegionSettings r, out int w, out int h)
    {
        var sx = m.X + (int)Math.Round(r.X * m.Width);
        var sy = m.Y + (int)Math.Round(r.Y * m.Height);
        var sw = Math.Max(1, (int)Math.Round(r.Width * m.Width));
        var sh = Math.Max(1, (int)Math.Round(r.Height * m.Height));
        w = Math.Clamp(sw, 1, MaxW);
        h = Math.Clamp(sh, 1, MaxH);
        var screen = Win32.GetDC(IntPtr.Zero);
        try { return Win32.StretchBlt(_memDc, 0, 0, w, h, screen, sx, sy, sw, sh, Win32.SRCCOPY); }
        finally { Win32.ReleaseDC(IntPtr.Zero, screen); }
    }

    private RgbColor Pixel(int x, int y)
    {
        var p = _pixels[y * MaxW + x];
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

    public void Dispose()
    {
        Win32.SelectObject(_memDc, _oldBitmap);
        Win32.DeleteObject(_bitmap);
        Win32.DeleteDC(_memDc);
    }
}
