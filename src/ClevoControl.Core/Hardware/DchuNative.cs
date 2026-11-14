using System.Runtime.InteropServices;

namespace ClevoControl.Core.Hardware;

/// <summary>
/// Thin wrapper around Clevo's <c>InsydeDCHU.dll</c>, which ships with the Clevo
/// "Control Center" driver package (AcpiBridge). The DLL is loaded at runtime from the
/// Windows driver store – it is proprietary and is NOT redistributed with this project.
/// </summary>
/// <remarks>
/// Signatures were determined from the P/Invoke metadata of Clevo's own apps:
/// <code>
/// int SetDCHU_Data(int command, byte[] data, int length);
/// int GetDCHU_Data_Buffer(int command, ref byte buffer);      // 256-byte buffer
/// int GetDCHU_Data_Integer(int command, ref int value);
/// int ReadAppSettings(int page, int offset, int length, ref byte buffer);
/// int WriteAppSettings(int page, int offset, int length, ref byte buffer);
/// </code>
/// </remarks>
public sealed unsafe class DchuNative : IDisposable
{
    public const string DllName = "InsydeDCHU.dll";
    public const string PathOverrideEnvVar = "CLEVOCONTROL_DCHU_PATH";

    private readonly IntPtr _lib;
    private readonly delegate* unmanaged<int, byte*, int, int> _set;
    private readonly delegate* unmanaged<int, byte*, int> _getBuffer;
    private readonly delegate* unmanaged<int, int*, int> _getInteger;
    private readonly delegate* unmanaged<int, int, int, byte*, int> _readSettings;
    private readonly delegate* unmanaged<int, int, int, byte*, int> _writeSettings;
    private readonly object _sync = new();

    public string LibraryPath { get; }

    private DchuNative(string path)
    {
        LibraryPath = path;
        _lib = NativeLibrary.Load(path);
        _set = (delegate* unmanaged<int, byte*, int, int>)NativeLibrary.GetExport(_lib, "SetDCHU_Data");
        _getBuffer = (delegate* unmanaged<int, byte*, int>)NativeLibrary.GetExport(_lib, "GetDCHU_Data_Buffer");
        _getInteger = (delegate* unmanaged<int, int*, int>)NativeLibrary.GetExport(_lib, "GetDCHU_Data_Integer");
        _readSettings = (delegate* unmanaged<int, int, int, byte*, int>)NativeLibrary.GetExport(_lib, "ReadAppSettings");
        _writeSettings = (delegate* unmanaged<int, int, int, byte*, int>)NativeLibrary.GetExport(_lib, "WriteAppSettings");
    }

    /// <summary>Tries to locate and load InsydeDCHU.dll. Returns null if it is not installed.</summary>
    public static DchuNative? TryLoad(out string? error)
    {
        error = null;
        foreach (var candidate in FindCandidates())
        {
            try
            {
                return new DchuNative(candidate);
            }
            catch (Exception ex)
            {
                error = $"{candidate}: {ex.Message}";
            }
        }
        error ??= "InsydeDCHU.dll was not found. Is the Clevo Control Center driver (AcpiBridge) installed?";
        return null;
    }

    public static IEnumerable<string> FindCandidates()
    {
        var env = Environment.GetEnvironmentVariable(PathOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) yield return env;

        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var repo = Path.Combine(windir, "System32", "DriverStore", "FileRepository");
        IEnumerable<string> fromDriverStore = [];
        try
        {
            fromDriverStore = Directory.EnumerateDirectories(repo, "acpibridge*")
                .Select(d => Path.Combine(d, DllName))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch { /* not accessible */ }
        foreach (var p in fromDriverStore) yield return p;

        // Fallback: copies bundled with Clevo's Store apps (read-only, but loadable).
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        IEnumerable<string> fromApps = [];
        try
        {
            fromApps = Directory.EnumerateDirectories(apps, "CLEVOCO.*")
                .SelectMany(d => SafeEnumerate(d))
                .ToList();
        }
        catch { /* WindowsApps is often not listable */ }
        foreach (var p in fromApps) yield return p;
    }

    private static IEnumerable<string> SafeEnumerate(string dir)
    {
        try { return Directory.EnumerateFiles(dir, DllName, SearchOption.AllDirectories).ToList(); }
        catch { return []; }
    }

    /// <summary>Executes a DCHU "set" command with a 32-bit argument (little endian).</summary>
    public int Set(int command, uint argument)
    {
        Span<byte> data = stackalloc byte[4];
        BitConverter.TryWriteBytes(data, argument);
        return Set(command, data);
    }

    /// <summary>
    /// Runs one driver call under the lock. If another call hangs (e.g. the EC is still waking up
    /// after sleep), callers give up after a few seconds instead of freezing the whole app.
    /// </summary>
    private T Call<T>(string what, Func<T> call)
    {
        if (!Monitor.TryEnter(_sync, TimeSpan.FromSeconds(4)))
        {
            Log.Warn($"DCHU busy, skipped {what}");
            throw new TimeoutException($"Clevo driver busy ({what}).");
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return call();
        }
        finally
        {
            Monitor.Exit(_sync);
            if (sw.ElapsedMilliseconds > 1000) Log.Warn($"DCHU call {what} took {sw.ElapsedMilliseconds} ms");
        }
    }

    public int Set(int command, ReadOnlySpan<byte> data)
    {
        // The DLL expects a writable buffer; copy to be safe.
        var buf = data.ToArray();
        return Call($"Set 0x{command:X2} {Convert.ToHexString(buf)}", () =>
        {
            fixed (byte* p = buf) return _set(command, p, buf.Length);
        });
    }

    public byte[] GetBuffer(int command, int size = 256)
    {
        var buf = new byte[Math.Max(size, 256)];
        Call($"GetBuffer 0x{command:X2}", () =>
        {
            fixed (byte* p = buf) return _getBuffer(command, p);
        });
        return buf;
    }

    public int GetInteger(int command)
    {
        return Call($"GetInteger 0x{command:X2}", () =>
        {
            int value = 0;
            _getInteger(command, &value);
            return value;
        });
    }

    public byte[] ReadAppSettings(int page, int offset, int length)
    {
        var buf = new byte[Math.Max(length, 1)];
        Call($"ReadAppSettings {page}/{offset}", () =>
        {
            fixed (byte* p = buf) return _readSettings(page, offset, length, p);
        });
        return buf;
    }

    public void WriteAppSettings(int page, int offset, ReadOnlySpan<byte> data)
    {
        var buf = data.ToArray();
        Call($"WriteAppSettings {page}/{offset}", () =>
        {
            fixed (byte* p = buf) return _writeSettings(page, offset, buf.Length, p);
        });
    }

    public void Dispose()
    {
        // Intentionally not freeing the library: the driver DLL keeps internal handles and
        // unloading it while another thread might still call into it is not worth the risk.
    }
}
