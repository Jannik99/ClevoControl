using ClevoControl.Core.Native;

namespace ClevoControl.Core.Lighting;

public sealed record MonitorInfo(int Index, string Name, int X, int Y, int Width, int Height, bool Primary);

/// <summary>
/// Samples the average color of a monitor by letting GDI downscale it into a tiny bitmap.
/// Cheap (a few ms per frame) and works with every GPU. DRM-protected video may appear black.
/// </summary>
public sealed unsafe class ScreenSampler : IDisposable
{
    private const int W = 48, H = 27;
    private readonly IntPtr _memDc, _bitmap, _oldBitmap;
    private readonly uint* _pixels;

    public ScreenSampler()
    {
        var screen = Win32.GetDC(IntPtr.Zero);
        _memDc = Win32.CreateCompatibleDC(screen);
        Win32.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(Win32.BITMAPINFOHEADER), biWidth = W, biHeight = -H, biPlanes = 1, biBitCount = 32,
        };
        _bitmap = Win32.CreateDIBSection(_memDc, bmi, 0, out var bits, IntPtr.Zero, 0);
        _pixels = (uint*)bits;
        _oldBitmap = Win32.SelectObject(_memDc, _bitmap);
        Win32.SetStretchBltMode(_memDc, Win32.HALFTONE);
        Win32.SetBrushOrgEx(_memDc, 0, 0, IntPtr.Zero);
    }

    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (Win32.GetMonitorInfo(h, ref info))
            {
                var r = info.rcMonitor;
                list.Add(new MonitorInfo(list.Count, info.szDevice, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (info.dwFlags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// Returns a representative color: saturated, bright pixels are weighted more than grey
    /// ones, and black letterbox bars are ignored.
    /// </summary>
    public RgbColor? Sample(MonitorInfo monitor)
    {
        var screen = Win32.GetDC(IntPtr.Zero);
        try
        {
            if (!Win32.StretchBlt(_memDc, 0, 0, W, H, screen, monitor.X, monitor.Y, monitor.Width, monitor.Height, Win32.SRCCOPY))
                return null;
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }

        double r = 0, g = 0, b = 0, weight = 0, plainR = 0, plainG = 0, plainB = 0;
        var n = W * H;
        for (var i = 0; i < n; i++)
        {
            var p = _pixels[i];
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

    public void Dispose()
    {
        Win32.SelectObject(_memDc, _oldBitmap);
        Win32.DeleteObject(_bitmap);
        Win32.DeleteDC(_memDc);
    }
}
