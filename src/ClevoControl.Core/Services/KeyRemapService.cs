using System.Diagnostics;
using System.Runtime.InteropServices;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Services;

/// <summary>
/// Lightweight FlexiKey replacement: a trigger key (optionally with Ctrl/Alt/Shift/Win) can send a
/// shortcut, type a text, launch a program/URL or simply be disabled. Runs on the keyboard hook.
/// </summary>
public sealed class KeyRemapService
{
    private volatile List<KeyMapping> _mappings = [];
    private volatile bool _enabled;
    private readonly HashSet<int> _swallowedDown = [];

    public void Apply(KeyRemapSettings settings)
    {
        _mappings = settings.Mappings.Where(m => m.Enabled && m.Key != 0).Select(m => m.Clone()).ToList();
        _enabled = settings.Enabled;
    }

    /// <summary>Hook filter: returns true when the key event must be swallowed.</summary>
    public bool Filter(int vk, bool down)
    {
        if (!_enabled) return false;
        if (!down)
        {
            lock (_swallowedDown) return _swallowedDown.Remove(vk);
        }
        var ctrl = IsDown(0x11);
        var alt = IsDown(0x12);
        var shift = IsDown(0x10);
        var win = IsDown(0x5B) || IsDown(0x5C);
        var m = _mappings.FirstOrDefault(x => x.Key == vk && x.Ctrl == ctrl && x.Alt == alt && x.Shift == shift && x.Win == win);
        if (m is null) return false;
        lock (_swallowedDown) _swallowedDown.Add(vk);
        if (m.Action != KeyActionType.Disable) ThreadPool.QueueUserWorkItem(_ => Execute(m));
        return true;
    }

    private static void Execute(KeyMapping m)
    {
        try
        {
            switch (m.Action)
            {
                case KeyActionType.Shortcut:
                    Thread.Sleep(30);
                    ReleaseModifiers();
                    SendShortcut(m.Value);
                    break;
                case KeyActionType.Text:
                    Thread.Sleep(30);
                    ReleaseModifiers();
                    SendText(m.Value);
                    break;
                case KeyActionType.Launch:
                    if (!string.IsNullOrWhiteSpace(m.Value))
                        Process.Start(new ProcessStartInfo(m.Value.Trim().Trim('"')) { UseShellExecute = true });
                    break;
            }
        }
        catch
        {
            // invalid program path etc. – ignore
        }
    }

    // ---- key names -------------------------------------------------------------------------

    private static readonly Dictionary<string, int> Names = BuildNames();

    private static Dictionary<string, int> BuildNames()
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = 0x11, ["Strg"] = 0x11, ["Control"] = 0x11, ["Alt"] = 0x12, ["Shift"] = 0x10, ["Umschalt"] = 0x10, ["Win"] = 0x5B,
            ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Space"] = 0x20, ["Leertaste"] = 0x20,
            ["Backspace"] = 0x08, ["Del"] = 0x2E, ["Delete"] = 0x2E, ["Entf"] = 0x2E, ["Ins"] = 0x2D, ["Insert"] = 0x2D, ["Einfg"] = 0x2D,
            ["Home"] = 0x24, ["Pos1"] = 0x24, ["End"] = 0x23, ["Ende"] = 0x23, ["PgUp"] = 0x21, ["PageUp"] = 0x21, ["BildAuf"] = 0x21,
            ["PgDn"] = 0x22, ["PageDown"] = 0x22, ["BildAb"] = 0x22, ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
            ["PrintScreen"] = 0x2C, ["Druck"] = 0x2C, ["Pause"] = 0x13, ["ScrollLock"] = 0x91, ["CapsLock"] = 0x14, ["NumLock"] = 0x90,
            ["Apps"] = 0x5D, ["Menu"] = 0x5D,
            ["VolumeUp"] = 0xAF, ["VolumeDown"] = 0xAE, ["VolumeMute"] = 0xAD, ["Mute"] = 0xAD,
            ["MediaPlayPause"] = 0xB3, ["PlayPause"] = 0xB3, ["MediaNext"] = 0xB0, ["MediaPrev"] = 0xB1, ["MediaStop"] = 0xB2,
            ["Calculator"] = 0xB7, ["Mail"] = 0xB4, ["BrowserHome"] = 0xAC,
            // German layout (OEM) keys – names without "+" so shortcuts stay parseable
            ["Plus"] = 0xBB, ["Minus"] = 0xBD, ["Komma"] = 0xBC, ["Punkt"] = 0xBE, ["Raute"] = 0xBF, ["Ü"] = 0xBA, ["Ö"] = 0xC0,
            ["Ä"] = 0xDE, ["ß"] = 0xDB, ["Zirkumflex"] = 0xDC, ["Akut"] = 0xDD, ["Kleiner"] = 0xE2,
            ["Num0"] = 0x60, ["Num1"] = 0x61, ["Num2"] = 0x62, ["Num3"] = 0x63, ["Num4"] = 0x64, ["Num5"] = 0x65, ["Num6"] = 0x66,
            ["Num7"] = 0x67, ["Num8"] = 0x68, ["Num9"] = 0x69, ["NumMultiply"] = 0x6A, ["NumAdd"] = 0x6B, ["NumSubtract"] = 0x6D,
            ["NumDecimal"] = 0x6E, ["NumDivide"] = 0x6F,
        };
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (var c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (var i = 1; i <= 24; i++) d["F" + i] = 0x6F + i;
        return d;
    }

    public static string KeyName(int vk)
    {
        foreach (var (name, code) in Names)
            if (code == vk && name.Length > 0 && !name.Equals("Strg", StringComparison.Ordinal) && !name.Equals("Umschalt", StringComparison.Ordinal))
                return name;
        return $"0x{vk:X2}";
    }

    public static string Describe(KeyMapping m)
    {
        var parts = new List<string>();
        if (m.Ctrl) parts.Add("Ctrl");
        if (m.Alt) parts.Add("Alt");
        if (m.Shift) parts.Add("Shift");
        if (m.Win) parts.Add("Win");
        parts.Add(KeyName(m.Key));
        return string.Join("+", parts);
    }

    /// <summary>Parses "Ctrl+Shift+Esc" into virtual-key codes (modifiers first).</summary>
    public static List<int>? ParseShortcut(string text)
    {
        var keys = new List<int>();
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Names.TryGetValue(part, out var vk)) return null;
            keys.Add(vk);
        }
        return keys.Count == 0 ? null : keys;
    }

    // ---- input injection ---------------------------------------------------------------------

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    // x64 INPUT is 40 bytes: 4-byte type, padding, then the 32-byte union (MOUSEINPUT is the largest member).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KEYBDINPUT Ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }

    private const uint KEYUP = 0x0002, UNICODE = 0x0004, EXTENDED = 0x0001;

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static INPUT Key(int vk, bool up, bool unicode = false, char ch = '\0')
    {
        var flags = up ? KEYUP : 0;
        if (unicode) flags |= UNICODE;
        else if (vk is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x5D) flags |= EXTENDED;
        return new INPUT { Type = 1, Ki = new KEYBDINPUT { Vk = unicode ? (ushort)0 : (ushort)vk, Scan = unicode ? ch : (ushort)0, Flags = flags } };
    }

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count > 0) SendInput((uint)inputs.Count, [.. inputs], Marshal.SizeOf<INPUT>());
    }

    private static void ReleaseModifiers()
    {
        var list = new List<INPUT>();
        foreach (var vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if (IsDown(vk)) list.Add(Key(vk, true));
        Send(list);
    }

    public static void SendShortcut(string shortcut)
    {
        var keys = ParseShortcut(shortcut);
        if (keys is null) return;
        var list = new List<INPUT>();
        foreach (var k in keys) list.Add(Key(k, false));
        for (var i = keys.Count - 1; i >= 0; i--) list.Add(Key(keys[i], true));
        Send(list);
    }

    public static void SendText(string text)
    {
        var list = new List<INPUT>();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n')
            {
                list.Add(Key(0x0D, false));
                list.Add(Key(0x0D, true));
                continue;
            }
            list.Add(Key(0, false, true, ch));
            list.Add(Key(0, true, true, ch));
        }
        Send(list);
    }
}
