using System.Buffers.Binary;

namespace ClevoControl.Core.Platform;

/// <summary>
/// Reads key events from /dev/input (evdev) without grabbing the keyboard, so other programs
/// still receive every key. Used for the reactive typing effect and the scene hotkeys
/// (Ctrl+Alt+Shift+0…9, +F). Needs read access to /dev/input/event* (group "input").
/// Key codes are never stored or logged.
/// </summary>
public sealed class LinuxKeyboard : IDisposable
{
    private const int EvKey = 1;
    private const int KeyLeftCtrl = 29, KeyRightCtrl = 97, KeyLeftShift = 42, KeyRightShift = 54, KeyLeftAlt = 56, KeyRightAlt = 100;
    private const int KeyF = 33;

    private readonly List<FileStream> _devices = [];
    private volatile bool _running = true;
    private readonly HashSet<int> _held = [];
    private readonly object _sync = new();

    /// <summary>Raised for every key press (not for auto-repeat).</summary>
    public event Action? KeyPressed;

    /// <summary>Ctrl+Alt+Shift hotkeys: 0–9 for the digit keys, 20 for F (same ids as on Windows).</summary>
    public event Action<int>? Hotkey;

    /// <summary>Why no keyboard could be opened (permissions), or null.</summary>
    public string? Error { get; }

    public LinuxKeyboard()
    {
        var found = Keyboards(LinuxSys.ReadText("proc/bus/input/devices"));
        if (found.Count == 0) { Error = "no keyboard found in /proc/bus/input/devices"; return; }
        foreach (var ev in found)
        {
            try
            {
                var fs = new FileStream(LinuxSys.P("dev/input/" + ev), FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, false);
                _devices.Add(fs);
                new Thread(() => Read(fs)) { IsBackground = true, Name = "evdev " + ev }.Start();
            }
            catch (UnauthorizedAccessException)
            {
                Error = "no permission for /dev/input – add your user to the group 'input' (see docs/LINUX.md)";
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        }
        if (_devices.Count > 0) Error = null;
        else Log.Warn("Keyboard events unavailable: " + Error);
    }

    public bool Active => _devices.Count > 0;

    /// <summary>Event device names (event3 …) of real keyboards in /proc/bus/input/devices.</summary>
    public static List<string> Keyboards(string? devices)
    {
        var list = new List<string>();
        if (devices is null) return list;
        foreach (var block in devices.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            string? handlers = null, ev = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("H: Handlers=", StringComparison.Ordinal)) handlers = line[12..];
                else if (line.StartsWith("B: EV=", StringComparison.Ordinal)) ev = line[6..].Trim();
            }
            if (handlers is null || ev is null || !handlers.Split(' ').Contains("kbd")) continue;
            // Real keyboards report EV_KEY (bit 1) and EV_REP (bit 20); power buttons etc. do not repeat.
            if (!ulong.TryParse(ev, System.Globalization.NumberStyles.HexNumber, null, out var bits) || (bits & (1UL << 20)) == 0) continue;
            var evdev = handlers.Split(' ').FirstOrDefault(h => h.StartsWith("event", StringComparison.Ordinal));
            if (evdev is not null) list.Add(evdev);
        }
        return list;
    }

    private void Read(FileStream fs)
    {
        // struct input_event on 64-bit: timeval (16) + type (2) + code (2) + value (4) = 24 bytes
        var buf = new byte[24 * 64];
        try
        {
            while (_running)
            {
                var n = fs.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                for (var off = 0; off + 24 <= n; off += 24)
                {
                    var type = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(off + 16));
                    if (type != EvKey) continue;
                    var code = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(off + 18));
                    var value = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(off + 20));
                    OnKey(code, value);
                }
            }
        }
        catch { /* device removed or disposed */ }
    }

    /// <summary>value: 1 press, 0 release, 2 auto-repeat.</summary>
    internal void OnKey(int code, int value)
    {
        int? hotkey = null;
        lock (_sync)
        {
            if (value == 0) { _held.Remove(code); return; }
            if (value != 1 || !_held.Add(code)) return;
            var ctrl = _held.Contains(KeyLeftCtrl) || _held.Contains(KeyRightCtrl);
            var alt = _held.Contains(KeyLeftAlt) || _held.Contains(KeyRightAlt);
            var shift = _held.Contains(KeyLeftShift) || _held.Contains(KeyRightShift);
            if (ctrl && alt && shift)
            {
                if (code is >= 2 and <= 10) hotkey = code - 1;   // KEY_1..KEY_9
                else if (code == 11) hotkey = 0;                 // KEY_0
                else if (code == KeyF) hotkey = 20;
            }
        }
        try { KeyPressed?.Invoke(); } catch { }
        if (hotkey is { } h) try { Hotkey?.Invoke(h); } catch (Exception ex) { Log.Error("Hotkey failed", ex); }
    }

    public void Dispose()
    {
        _running = false;
        foreach (var d in _devices) try { d.Dispose(); } catch { }
    }
}
