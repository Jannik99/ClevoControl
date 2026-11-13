using System.Runtime.InteropServices;

namespace ClevoControl.Core.Hardware;

/// <summary>
/// <c>SetDCHU_DataEx</c> (command + 256-byte request → 256-byte response) is only exported by the
/// newer InsydeDCHU.dll copies that ship inside Clevo's Store apps, not by the one in the driver
/// store. Files inside <c>WindowsApps</c> cannot be loaded directly, so the DLL is copied to the
/// local cache of this user first (it is never redistributed with ClevoControl).
/// </summary>
public sealed unsafe class DchuEx
{
    private readonly delegate* unmanaged<int, byte*, int, byte*, int> _ex;
    private readonly object _sync = new();

    private DchuEx(IntPtr lib) =>
        _ex = (delegate* unmanaged<int, byte*, int, byte*, int>)NativeLibrary.GetExport(lib, "SetDCHU_DataEx");

    public static DchuEx? TryLoad(string primaryPath)
    {
        // 1) The driver-store DLL may already export it on newer driver versions.
        if (TryOpen(primaryPath) is { } direct) return direct;

        // 2) Copy from one of Clevo's Store apps into our cache.
        try
        {
            var cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClevoControl", "dchu");
            var cached = Path.Combine(cacheDir, DchuNative.DllName);
            var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            var source = Directory.EnumerateDirectories(apps, "CLEVOCO.ControlCenter*")
                .Concat(Directory.EnumerateDirectories(apps, "CLEVOCO.*"))
                .SelectMany(d => SafeFiles(d))
                .FirstOrDefault(HasExExport);
            if (source is null) return File.Exists(cached) ? TryOpen(cached) : null;
            Directory.CreateDirectory(cacheDir);
            if (!File.Exists(cached) || new FileInfo(cached).Length != new FileInfo(source).Length)
                File.Copy(source, cached, true);
            return TryOpen(cached);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, DchuNative.DllName, SearchOption.AllDirectories).ToList(); }
        catch { return []; }
    }

    private static bool HasExExport(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return System.Text.Encoding.ASCII.GetString(bytes).Contains("SetDCHU_DataEx", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static DchuEx? TryOpen(string path)
    {
        try
        {
            if (!NativeLibrary.TryLoad(path, out var lib)) return null;
            return NativeLibrary.TryGetExport(lib, "SetDCHU_DataEx", out _) ? new DchuEx(lib) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Sends a 256-byte request with <paramref name="command"/> and returns the 256-byte response.</summary>
    public byte[] Call(int command, ReadOnlySpan<byte> request)
    {
        var input = new byte[256];
        request[..Math.Min(request.Length, 256)].CopyTo(input);
        var output = new byte[256];
        lock (_sync)
        {
            fixed (byte* i = input)
            fixed (byte* o = output)
                _ex(command, i, 256, o);
        }
        return output;
    }
}
