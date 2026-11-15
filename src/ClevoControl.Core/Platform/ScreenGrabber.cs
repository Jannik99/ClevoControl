using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Native;

namespace ClevoControl.Core.Platform;

/// <summary>Reads a screen rectangle downscaled into a small pixel buffer (0x00RRGGBB per pixel).</summary>
public interface IScreenGrabber : IDisposable
{
    /// <summary>Copies the source rectangle scaled to <paramref name="w"/>×<paramref name="h"/> into <paramref name="dest"/> (row-major).</summary>
    bool Grab(int sx, int sy, int sw, int sh, int w, int h, Span<uint> dest);
}

public static partial class ScreenGrabber
{
    /// <summary>Why screen capture is unavailable on this desktop, or null if it works.</summary>
    public static string? UnsupportedReason
    {
        get
        {
            if (OperatingSystem.IsWindows()) return null;
            if (!OperatingSystem.IsLinux()) return "unsupported OS";
            if (Shell.IsWayland)
                return Shell.Has("grim") ? null
                    : "Wayland: screen capture needs 'grim' (wlroots desktops such as Sway/Hyprland). GNOME and KDE on Wayland do not allow it.";
            return Shell.HasX11 ? null : "no display";
        }
    }

    public static IScreenGrabber? Create()
    {
        if (OperatingSystem.IsWindows()) return new GdiGrabber();
        if (!OperatingSystem.IsLinux()) return null;
        if (Shell.IsWayland) return Shell.Has("grim") ? new GrimGrabber() : null;
        return Shell.HasX11 ? X11Grabber.TryCreate() : null;
    }

    public static List<MonitorInfo> Monitors()
    {
        if (OperatingSystem.IsWindows()) return WindowsMonitors();
        if (!OperatingSystem.IsLinux()) return [];
        // xrandr also reports correct geometry for XWayland.
        return ParseXrandrMonitors(Shell.Run("xrandr", "--listmonitors"));
    }

    private static List<MonitorInfo> WindowsMonitors()
    {
        var list = new List<MonitorInfo>();
        Win32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Win32.RECT _, IntPtr _) =>
        {
            var info = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
            if (Win32.GetMonitorInfo(h, ref info))
            {
                var r = info.rcMonitor;
                list.Add(new MonitorInfo(list.Count, info.szDevice, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, (info.dwFlags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>" 0: +*eDP-1 1920/344x1080/193+0+0  eDP-1" → monitor.</summary>
    public static List<MonitorInfo> ParseXrandrMonitors(string? output)
    {
        var list = new List<MonitorInfo>();
        if (output is null) return list;
        foreach (Match m in MonitorRegex().Matches(output))
        {
            int I(int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            list.Add(new MonitorInfo(list.Count, m.Groups[2].Value, I(5), I(6), I(3), I(4), m.Groups[1].Value == "*"));
        }
        return list;
    }

    [GeneratedRegex(@"^\s*\d+: \+(\*?)(\S+) (\d+)/\d+x(\d+)/\d+\+(-?\d+)\+(-?\d+)", RegexOptions.Multiline)]
    private static partial Regex MonitorRegex();

    /// <summary>Box-averages a source image (0x00RRGGBB, stride in pixels) into the destination size.</summary>
    internal static void Downscale(ReadOnlySpan<uint> src, int srcW, int srcH, int stride, Span<uint> dest, int w, int h)
    {
        for (var y = 0; y < h; y++)
        {
            int y0 = y * srcH / h, y1 = Math.Max(y0 + 1, (y + 1) * srcH / h);
            for (var x = 0; x < w; x++)
            {
                int x0 = x * srcW / w, x1 = Math.Max(x0 + 1, (x + 1) * srcW / w);
                long r = 0, g = 0, b = 0, n = 0;
                // Sample at most 4×4 points per block to stay cheap on large areas.
                var sy = Math.Max(1, (y1 - y0) / 4);
                var sx = Math.Max(1, (x1 - x0) / 4);
                for (var yy = y0; yy < y1; yy += sy)
                for (var xx = x0; xx < x1; xx += sx)
                {
                    var p = src[yy * stride + xx];
                    r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF; n++;
                }
                dest[y * w + x] = n == 0 ? 0 : (uint)((r / n) << 16 | (g / n) << 8 | (b / n));
            }
        }
    }
}

/// <summary>Windows: GDI StretchBlt into a DIB section (fast, works with every GPU).</summary>
internal sealed unsafe class GdiGrabber : IScreenGrabber
{
    private const int MaxW = 160, MaxH = 90;
    private readonly IntPtr _memDc, _bitmap, _oldBitmap;
    private readonly uint* _pixels;

    public GdiGrabber()
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

    public bool Grab(int sx, int sy, int sw, int sh, int w, int h, Span<uint> dest)
    {
        w = Math.Min(w, MaxW);
        h = Math.Min(h, MaxH);
        var screen = Win32.GetDC(IntPtr.Zero);
        try
        {
            if (!Win32.StretchBlt(_memDc, 0, 0, w, h, screen, sx, sy, sw, sh, Win32.SRCCOPY)) return false;
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            dest[y * w + x] = _pixels[y * MaxW + x] & 0xFFFFFF;
        return true;
    }

    public void Dispose()
    {
        Win32.SelectObject(_memDc, _oldBitmap);
        Win32.DeleteObject(_bitmap);
        Win32.DeleteDC(_memDc);
    }
}

/// <summary>
/// Linux X11 (also XWayland-only content): XGetImage on the root window. Large areas are read as a
/// few one-pixel rows instead of the whole picture to keep the cost low.
/// </summary>
internal sealed unsafe class X11Grabber : IScreenGrabber
{
    private const string LibX11 = "libX11.so.6";
    private const int ZPixmap = 2;

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint w, uint h, ulong planeMask, int format);
    [DllImport(LibX11)] private static extern int XFree(IntPtr data);
    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(delegate* unmanaged<IntPtr, IntPtr, int> handler);

    // XImage layout (64-bit): width 0, height 4, data 16, bytes_per_line 44, bits_per_pixel 48.
    private const int OffWidth = 0, OffHeight = 4, OffData = 16, OffBytesPerLine = 44, OffBitsPerPixel = 48, OffDestroy = 96;

    private readonly IntPtr _display, _root;
    private static int _handlerSet;

    private X11Grabber(IntPtr display)
    {
        _display = display;
        _root = XDefaultRootWindow(display);
    }

    [UnmanagedCallersOnly]
    private static int IgnoreErrors(IntPtr display, IntPtr error) => 0; // never let Xlib exit the process

    public static X11Grabber? TryCreate()
    {
        try
        {
            if (Interlocked.Exchange(ref _handlerSet, 1) == 0) XSetErrorHandler(&IgnoreErrors);
            var d = XOpenDisplay(IntPtr.Zero);
            return d == IntPtr.Zero ? null : new X11Grabber(d);
        }
        catch (Exception ex)
        {
            Log.Warn("X11 screen capture unavailable: " + ex.Message);
            return null;
        }
    }

    public bool Grab(int sx, int sy, int sw, int sh, int w, int h, Span<uint> dest)
    {
        if (sw <= 0 || sh <= 0) return false;
        // Small areas: one image. Large areas: up to 4 rows per output row.
        if ((long)sw * sh <= 400_000) return GrabRect(sx, sy, sw, sh, w, h, dest);

        var row = new uint[sw];
        var rows = Math.Min(sh, h * 2);
        var acc = new long[w * h * 4];
        for (var i = 0; i < rows; i++)
        {
            var y = sy + (int)((i + 0.5) * sh / rows);
            if (!ReadImage(sx, y, sw, 1, (src, _, _, _) => src[..sw].CopyTo(row))) return false;
            var oy = Math.Min(h - 1, (y - sy) * h / sh);
            for (var x = 0; x < w; x++)
            {
                var p = row[Math.Min(sw - 1, (int)((x + 0.5) * sw / w))];
                var k = (oy * w + x) * 4;
                acc[k] += (p >> 16) & 0xFF; acc[k + 1] += (p >> 8) & 0xFF; acc[k + 2] += p & 0xFF; acc[k + 3]++;
            }
        }
        for (var i = 0; i < w * h; i++)
        {
            var n = Math.Max(1, acc[i * 4 + 3]);
            dest[i] = (uint)((acc[i * 4] / n) << 16 | (acc[i * 4 + 1] / n) << 8 | (acc[i * 4 + 2] / n));
        }
        return true;
    }

    private bool GrabRect(int sx, int sy, int sw, int sh, int w, int h, Span<uint> dest)
    {
        var tmp = new uint[w * h];
        var ok = ReadImage(sx, sy, sw, sh, (src, iw, ih, stride) => ScreenGrabber.Downscale(src, iw, ih, stride, tmp, w, h));
        if (ok) tmp.CopyTo(dest);
        return ok;
    }

    private delegate void ImageReader(ReadOnlySpan<uint> pixels, int width, int height, int stride);

    private bool ReadImage(int x, int y, int w, int h, ImageReader read)
    {
        var img = XGetImage(_display, _root, x, y, (uint)w, (uint)h, ulong.MaxValue, ZPixmap);
        if (img == IntPtr.Zero) return false;
        try
        {
            var p = (byte*)img;
            var iw = *(int*)(p + OffWidth);
            var ih = *(int*)(p + OffHeight);
            var bpl = *(int*)(p + OffBytesPerLine);
            var bpp = *(int*)(p + OffBitsPerPixel);
            var data = *(IntPtr*)(p + OffData);
            if (bpp != 32 || data == IntPtr.Zero) return false;
            read(new ReadOnlySpan<uint>((void*)data, bpl / 4 * ih), iw, ih, bpl / 4);
            return true;
        }
        finally
        {
            // XDestroyImage is a macro calling image->f.destroy_image.
            var destroy = *(delegate* unmanaged<IntPtr, int>*)((byte*)img + OffDestroy);
            if (destroy is not null) destroy(img);
            else XFree(img);
        }
    }

    public void Dispose() => XCloseDisplay(_display);
}

/// <summary>Linux Wayland on wlroots compositors (Sway, Hyprland …) via the <c>grim</c> tool.</summary>
internal sealed class GrimGrabber : IScreenGrabber
{
    public bool Grab(int sx, int sy, int sw, int sh, int w, int h, Span<uint> dest)
    {
        // grim scales on its side; ask for roughly 4× the target so the box filter has something to average.
        var scale = Math.Clamp(4.0 * w / Math.Max(1, sw), 0.02, 1).ToString("0.###", CultureInfo.InvariantCulture);
        var ppm = RunBinary("grim", "-g", $"{sx},{sy} {sw}x{sh}", "-s", scale, "-t", "ppm", "-");
        if (ppm is null || !Ppm.TryDecode(ppm, out var pixels, out var pw, out var ph)) return false;
        ScreenGrabber.Downscale(pixels, pw, ph, pw, dest, w, h);
        return true;
    }

    private static byte[]? RunBinary(string program, params string[] args)
    {
        using var p = Shell.StartStream(program, args);
        if (p is null) return null;
        using var ms = new MemoryStream();
        var copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
        if (!copy.Wait(1500) || !p.WaitForExit(500)) { try { p.Kill(); } catch { } return null; }
        return p.ExitCode == 0 ? ms.ToArray() : null;
    }

    public void Dispose() { }
}

/// <summary>Minimal binary PPM (P6) decoder.</summary>
public static class Ppm
{
    public static bool TryDecode(byte[] data, out uint[] pixels, out int width, out int height)
    {
        pixels = [];
        width = height = 0;
        var pos = 0;
        string? Token()
        {
            while (pos < data.Length)
            {
                if (data[pos] == '#') { while (pos < data.Length && data[pos] != '\n') pos++; }
                else if (char.IsWhiteSpace((char)data[pos])) pos++;
                else break;
            }
            var start = pos;
            while (pos < data.Length && !char.IsWhiteSpace((char)data[pos])) pos++;
            return pos > start ? System.Text.Encoding.ASCII.GetString(data, start, pos - start) : null;
        }
        if (Token() != "P6" || !int.TryParse(Token(), out width) || !int.TryParse(Token(), out height) || !int.TryParse(Token(), out var max) || max != 255)
            return false;
        pos++; // single whitespace after maxval
        var n = width * height;
        if (data.Length - pos < n * 3) return false;
        pixels = new uint[n];
        for (var i = 0; i < n; i++, pos += 3) pixels[i] = (uint)(data[pos] << 16 | data[pos + 1] << 8 | data[pos + 2]);
        return true;
    }
}
