using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ClevoControl.Core.Platform;

/// <summary>A key on the keyboard picture: legend, virtual-key code (0 = not selectable) and width in key units.</summary>
public readonly record struct KeyCap(string Label, int Vk, double Width);

/// <summary>
/// The user's real keyboard layout (QWERTZ, QWERTY, AZERTY, …) for the keyboard picture and key names.
/// <para>
/// Keys are identified by their physical position (PC scan code). The legend comes from the layout
/// (on Windows "Automatic" asks Windows itself, so every installed layout works), the shape (ISO with
/// the extra key next to left Shift, or ANSI) from the layout's region.
/// </para>
/// <para>
/// Virtual-key codes, which the settings store, are what the key really sends: on Windows the code the
/// active Windows layout assigns to that position (what the keyboard hook sees, even when the picture
/// is overridden), on Linux the US code of the position (an identifier of the physical key for keyd).
/// </para>
/// </summary>
public sealed class KeyboardLayout
{
    // ---- layout tables ------------------------------------------------------------------------

    /// <summary>The 48 character keys in this order (PC set-1 scan codes).</summary>
    internal static readonly int[] CharScans =
    [
        0x29, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B,
        0x2B,
        0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28,
        0x56,
        0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35,
    ];

    /// <summary>US virtual-key codes of <see cref="CharScans"/> (the physical-key identifiers on Linux).</summary>
    private static readonly int[] UsVks =
    [
        0xC0, '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 0xBD, 0xBB,
        'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P', 0xDB, 0xDD,
        0xDC,
        'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L', 0xBA, 0xDE,
        0xE2,
        'Z', 'X', 'C', 'V', 'B', 'N', 'M', 0xBC, 0xBE, 0xBF,
    ];

    private sealed record Table(string Id, string Name, string Keys, bool Iso, bool German, char Decimal);

    // Keys: unshifted legends of CharScans (letters upper case). Order: row 1 (13), row 2 (12), the key
    // next to Enter (US backslash / ISO #), home row (11), the ISO key left of Z, bottom row (10).
    private static readonly Table[] Tables =
    [
        new("us", "English (US) – QWERTY", "`1234567890-=" + "QWERTYUIOP[]" + "\\" + "ASDFGHJKL;'" + "\\" + "ZXCVBNM,./", false, false, '.'),
        new("gb", "English (UK) – QWERTY", "`1234567890-=" + "QWERTYUIOP[]" + "#" + "ASDFGHJKL;'" + "\\" + "ZXCVBNM,./", true, false, '.'),
        new("de", "Deutsch – QWERTZ", "^1234567890ß´" + "QWERTZUIOPÜ+" + "#" + "ASDFGHJKLÖÄ" + "<" + "YXCVBNM,.-", true, true, ','),
        new("ch", "Schweiz – QWERTZ", "§1234567890'^" + "QWERTZUIOPÜ¨" + "$" + "ASDFGHJKLÖÄ" + "<" + "YXCVBNM,.-", true, true, '.'),
        new("fr", "Français – AZERTY", "²&é\"'(-è_çà)=" + "AZERTYUIOP^$" + "*" + "QSDFGHJKLMÙ" + "<" + "WXCVBN,;:!", true, false, '.'),
        new("es", "Español – QWERTY", "º1234567890'¡" + "QWERTYUIOP`+" + "Ç" + "ASDFGHJKLÑ´" + "<" + "ZXCVBNM,.-", true, false, '.'),
        new("it", "Italiano – QWERTY", "\\1234567890'Ì" + "QWERTYUIOPÈ+" + "Ù" + "ASDFGHJKLÒÀ" + "<" + "ZXCVBNM,.-", true, false, '.'),
        new("pt", "Português – QWERTY", "\\1234567890'«" + "QWERTYUIOP+´" + "~" + "ASDFGHJKLÇº" + "<" + "ZXCVBNM,.-", true, false, ','),
        new("nordic", "Svenska / Suomi – QWERTY", "§1234567890+´" + "QWERTYUIOPÅ¨" + "'" + "ASDFGHJKLÖÄ" + "<" + "ZXCVBNM,.-", true, false, ','),
        new("no", "Norsk – QWERTY", "|1234567890+\\" + "QWERTYUIOPÅ¨" + "'" + "ASDFGHJKLØÆ" + "<" + "ZXCVBNM,.-", true, false, ','),
        new("dk", "Dansk – QWERTY", "½1234567890+´" + "QWERTYUIOPÅ¨" + "'" + "ASDFGHJKLÆØ" + "<" + "ZXCVBNM,.-", true, false, ','),
    ];

    /// <summary>Display name of a layout id ("de" → "Deutsch – QWERTZ"), or null.</summary>
    public static string? NameOf(string? id) => Tables.FirstOrDefault(t => t.Id == id)?.Name;

    /// <summary>Layouts offered as manual choice (id, display name). "auto" = follow the system.</summary>
    public static IReadOnlyList<(string Id, string Name)> Choices { get; } = Tables.Select(t => (t.Id, t.Name)).ToList();

    // ---- current layout -----------------------------------------------------------------------

    private static readonly object Gate = new();
    private static string _override = "auto";
    private static KeyboardLayout? _current;

    /// <summary>The layout in use: the manual choice, otherwise the system layout.</summary>
    public static KeyboardLayout Current
    {
        get
        {
            lock (Gate) return _current ??= Resolve(_override);
        }
    }

    /// <summary>Sets the manual choice ("auto" or an id from <see cref="Choices"/>) and re-detects.</summary>
    public static void Use(string? choice)
    {
        lock (Gate)
        {
            _override = string.IsNullOrWhiteSpace(choice) ? "auto" : choice;
            _current = null;
        }
    }

    /// <summary>Forgets the cached layout so the next access detects it again (e.g. after the user switched layouts).</summary>
    public static void Refresh()
    {
        lock (Gate) _current = null;
    }

    private static KeyboardLayout Resolve(string choice)
    {
        IntPtr hkl = IntPtr.Zero;
        string? systemId = null;
        try
        {
            if (OperatingSystem.IsWindows()) (hkl, systemId) = WindowsLayout();
            else systemId = MapCode(LinuxLayoutCode());
        }
        catch
        {
            // detection is best effort
        }

        var manual = Tables.FirstOrDefault(t => t.Id.Equals(choice, StringComparison.OrdinalIgnoreCase));
        var table = manual ?? Tables.FirstOrDefault(t => t.Id == systemId);
        // Windows "Automatic": legends straight from the Windows layout (works for any installed layout).
        var osLegends = manual is null && hkl != IntPtr.Zero;
        return new KeyboardLayout(table ?? Tables[0], hkl, osLegends, manual is not null, systemId);
    }

    // ---- instance -----------------------------------------------------------------------------

    private readonly Table _table;
    private readonly IntPtr _hkl;
    private readonly Dictionary<int, string> _labels = [];   // scan → legend
    private readonly Dictionary<int, int> _vks = [];          // scan → virtual key

    private KeyboardLayout(Table table, IntPtr hkl, bool osLegends, bool isManual, string? systemId)
    {
        _table = table;
        _hkl = hkl;
        IsManual = isManual;
        SystemId = systemId;
        for (var i = 0; i < CharScans.Length; i++)
        {
            var scan = CharScans[i];
            var vk = UsVks[i];
            if (hkl != IntPtr.Zero && OperatingSystem.IsWindows())
            {
                var mapped = (int)MapVirtualKeyEx((uint)scan, 1 /* MAPVK_VSC_TO_VK */, hkl);
                if (mapped != 0) vk = mapped;
            }
            _vks[scan] = vk;
            var label = osLegends ? WindowsLegend(vk, scan, hkl) : null;
            _labels[scan] = string.IsNullOrEmpty(label) ? table.Keys[i].ToString() : label;
        }
        if (osLegends && SystemId is null) Name = "Windows";
    }

    /// <summary>Internal: layout from a table, as Linux would build it (tests).</summary>
    internal static KeyboardLayout ForTests(string id) => new(Tables.First(t => t.Id == id), IntPtr.Zero, false, true, id);

    /// <summary>Id of the table used for shape and legends ("de", "us", …).</summary>
    public string Id => _table.Id;

    public string Name { get; } = "";

    /// <summary>True when chosen by hand instead of following the system.</summary>
    public bool IsManual { get; }

    /// <summary>Table id matching the system layout, or null when it has no table (other layouts still work on Windows).</summary>
    public string? SystemId { get; }

    /// <summary>ISO shape: extra key left of Z, tall Enter.</summary>
    public bool Iso => _table.Iso;

    /// <summary>Human-readable description, e.g. "Deutsch – QWERTZ".</summary>
    public string DisplayName => Name.Length > 0 ? Name : _table.Name;

    /// <summary>Virtual-key code of a character key (by scan code).</summary>
    public int VkForScan(int scan) => _vks.TryGetValue(scan, out var vk) ? vk : 0;

    /// <summary>Scan code of a character key with this virtual-key code, if any.</summary>
    public int? ScanForVk(int vk)
    {
        foreach (var scan in CharScans)
            if (_vks[scan] == vk) return scan;
        return null;
    }

    /// <summary>Legend of a character key (e.g. "Ü", "Z", "ñ"), or null for other keys.</summary>
    public string? CharLabel(int vk) => ScanForVk(vk) is { } s ? _labels[s] : null;

    /// <summary>Virtual key of the key showing this legend ("Z" → the key labelled Z on this layout).</summary>
    public int? VkForLabel(string label) => ScanForLabel(label) is { } s ? _vks[s] : null;

    /// <summary>Scan code of the key showing this legend.</summary>
    public int? ScanForLabel(string label)
    {
        foreach (var scan in CharScans)
            if (_labels[scan].Equals(label, StringComparison.OrdinalIgnoreCase)) return scan;
        return null;
    }

    /// <summary>US virtual-key code of a character key position (0 if not a character key).</summary>
    public static int UsVkForScan(int scan)
    {
        var i = Array.IndexOf(CharScans, scan);
        return i < 0 ? 0 : UsVks[i];
    }

    private bool German => _table.German;

    private string L(string de, string en) => German ? de : en;

    /// <summary>Legend of any key on this layout ("Ü", "F5", "Entf"/"Del", "Num 7", …).</summary>
    public string KeyLabel(int vk)
    {
        if (CharLabel(vk) is { } c) return c;
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
        return vk switch
        {
            0x1B => L("Esc", "Esc"), 0x09 => "Tab", 0x0D => "Enter", 0x20 => L("Leertaste", "Space"), 0x08 => L("Rücktaste", "Backspace"),
            0x2E => L("Entf", "Del"), 0x2D => L("Einfg", "Ins"), 0x24 => L("Pos1", "Home"), 0x23 => L("Ende", "End"),
            0x21 => L("Bild↑", "PgUp"), 0x22 => L("Bild↓", "PgDn"), 0x26 => "↑", 0x28 => "↓", 0x25 => "←", 0x27 => "→",
            0x2C => L("Druck", "PrtSc"), 0x13 => "Pause", 0x91 => L("Rollen", "ScrLk"), 0x14 => L("Feststelltaste", "Caps Lock"),
            0x90 => "Num Lock", 0x5D => L("Menütaste", "Menu key"),
            0x6A => "Num *", 0x6B => "Num +", 0x6D => "Num -", 0x6E => "Num " + _table.Decimal, 0x6F => "Num /",
            _ => Services.KeyRemapService.KeyName(vk),
        };
    }

    /// <summary>Rows of the main keyboard (laptop layout with arrow keys in the bottom row).</summary>
    public IReadOnlyList<IReadOnlyList<KeyCap>> MainRows()
    {
        KeyCap C(int scan) => new(_labels[scan], _vks[scan], 1);
        IEnumerable<KeyCap> Range(int from, int to) => CharScans.SkipWhile(s => s != from).TakeWhile(s => s != to).Select(C);

        var row1 = new List<KeyCap> { C(0x29) };
        row1.AddRange(Range(0x02, 0x10));
        row1.Add(new("⌫", 0x08, 3));

        var row2 = new List<KeyCap> { new("Tab", 0x09, 1.5) };
        row2.AddRange(Range(0x10, 0x2B));
        var row3 = new List<KeyCap> { new(L("⇩ Fest", "Caps"), 0x14, 1.85) };
        row3.AddRange(Range(0x1E, 0x56));
        var row4 = new List<KeyCap>();
        if (Iso)
        {
            row2.Add(new("↵", 0x0D, 2.5));
            row3.Add(C(0x2B));
            row3.Add(new("", 0x0D, 2.15)); // lower part of the tall ISO Enter key (drawn joined to the upper part)
            row4.Add(new("⇧", 0, 1.35));
            row4.Add(C(0x56));
        }
        else
        {
            row2.Add(C(0x2B) with { Width = 2.5 });
            row3.Add(new("↵", 0x0D, 3.15));
            row4.Add(new("⇧", 0, 2.35));
        }
        row4.AddRange(Range(0x2C, 0));
        row4.Add(new("⇧", 0, 3.65));

        return
        [
            [
                new("Esc", 0x1B, 1), new("F1", 0x70, 1), new("F2", 0x71, 1), new("F3", 0x72, 1), new("F4", 0x73, 1), new("F5", 0x74, 1),
                new("F6", 0x75, 1), new("F7", 0x76, 1), new("F8", 0x77, 1), new("F9", 0x78, 1), new("F10", 0x79, 1), new("F11", 0x7A, 1),
                new("F12", 0x7B, 1), new(L("Druck", "PrtSc"), 0x2C, 1), new(L("Einfg", "Ins"), 0x2D, 1), new(L("Entf", "Del"), 0x2E, 1),
            ],
            row1, row2, row3, row4,
            [
                new(L("Strg", "Ctrl"), 0, 1.35), new("Fn", 0, 1), new("⊞", 0, 1), new("Alt", 0, 1), new("", 0x20, 4.25),
                new(Id == "us" ? "Alt" : "AltGr", 0, 1), new("☰", 0x5D, 1), new(L("Strg", "Ctrl"), 0, 1.4),
                new("←", 0x25, 1), new("↑", 0x26, 1), new("↓", 0x28, 1), new("→", 0x27, 1),
            ],
        ];
    }

    /// <summary>Rows of the number pad (17" models).</summary>
    public IReadOnlyList<IReadOnlyList<KeyCap>> NumpadRows() =>
    [
        [new(L("Pos1", "Home"), 0x24, 1), new(L("Ende", "End"), 0x23, 1), new(L("Bild↑", "PgUp"), 0x21, 1), new(L("Bild↓", "PgDn"), 0x22, 1)],
        [new("Num", 0x90, 1), new("/", 0x6F, 1), new("*", 0x6A, 1), new("-", 0x6D, 1)],
        [new("7", 0x67, 1), new("8", 0x68, 1), new("9", 0x69, 1), new("+", 0x6B, 1)],
        [new("4", 0x64, 1), new("5", 0x65, 1), new("6", 0x66, 1), new("", 0, 1)],
        [new("1", 0x61, 1), new("2", 0x62, 1), new("3", 0x63, 1), new("", 0, 1)],
        [new("0", 0x60, 2), new(_table.Decimal.ToString(), 0x6E, 1), new("", 0, 1)],
    ];

    /// <summary>
    /// Path data (SVG / XAML mini-language) of the L-shaped ISO Enter key: upper part <paramref name="top"/>
    /// wide, lower part <paramref name="bottom"/> wide and right-aligned, spanning two rows.
    /// </summary>
    public static string IsoEnterPath(double top, double bottom, double unit, double gap, double radius)
    {
        var inv = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.##", inv);
        var r = radius;
        var h = 2 * unit + gap;
        var dx = top - bottom;
        var a = $"A {F(r)},{F(r)} 0 0 1";
        return $"M {F(r)},0 H {F(top - r)} {a} {F(top)},{F(r)} V {F(h - r)} {a} {F(top - r)},{F(h)} H {F(dx + r)} {a} {F(dx)},{F(h - r)} " +
               $"V {F(unit)} H {F(r)} {a} 0,{F(unit - r)} V {F(r)} {a} {F(r)},0 Z";
    }

    // ---- detection ----------------------------------------------------------------------------

    /// <summary>Maps an XKB layout code ("de", "de(nodeadkeys)", "us,de") to a table id.</summary>
    internal static string? MapCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Split(',', '+')[0].Split('(', ':')[0].Trim().ToLowerInvariant();
        return c switch
        {
            "us" => "us",
            "gb" or "uk" or "ie" => "gb",
            "de" or "at" => "de",
            "ch" => "ch",
            "fr" or "be" => "fr",
            "es" or "latam" => "es",
            "it" => "it",
            "pt" or "br" => "pt",
            "se" or "fi" => "nordic",
            "no" => "no",
            "dk" => "dk",
            _ => null,
        };
    }

    /// <summary>Table id for a Windows layout language (from the keyboard layout handle).</summary>
    internal static string? MapCulture(int langId)
    {
        string name;
        try { name = CultureInfo.GetCultureInfo(langId).Name; }
        catch { return null; }
        var parts = name.Split('-');
        var lang = parts[0].ToLowerInvariant();
        var region = parts.Length > 1 ? parts[^1].ToUpperInvariant() : "";
        return lang switch
        {
            "en" => region is "GB" or "IE" ? "gb" : "us",
            "de" => region is "CH" or "LI" ? "ch" : "de",
            "fr" => region == "CH" ? "ch" : "fr",
            "es" => "es",
            "it" => "it",
            "pt" => "pt",
            "sv" or "fi" => "nordic",
            "nb" or "nn" or "no" => "no",
            "da" => "dk",
            _ => null,
        };
    }

    private static (IntPtr Hkl, string? Id) WindowsLayout()
    {
        // The user's default input layout; fall back to the layout of this thread.
        var hkl = IntPtr.Zero;
        if (!SystemParametersInfo(0x59 /* SPI_GETDEFAULTINPUTLANG */, 0, ref hkl, 0) || hkl == IntPtr.Zero)
            hkl = GetKeyboardLayout(0);
        var value = (long)hkl;
        var lang = (int)(value & 0xFFFF);
        var device = (int)((value >> 16) & 0xFFFF);
        // The high word names the keyboard layout (e.g. English UI with a German keyboard = 0x04070409),
        // unless it is a variant handle (0xFxxx, e.g. Dvorak) – then the language decides.
        var layoutLang = (device & 0xF000) == 0xF000 || device == 0 ? lang : device;
        return (hkl, MapCulture(layoutLang) ?? MapCulture(lang));
    }

    private static string? WindowsLegend(int vk, int scan, IntPtr hkl)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var buf = new StringBuilder(8);
        var state = new byte[256];
        // Flag 4: do not change the keyboard state (dead keys stay usable for the user).
        var n = ToUnicodeEx((uint)vk, (uint)scan, state, buf, buf.Capacity, 4, hkl);
        if (n == 0 || buf.Length == 0) return null;
        var s = buf.ToString(0, 1);
        if (char.IsControl(s[0]) || char.IsWhiteSpace(s[0])) return null;
        return s.ToUpper(CultureInfo.CurrentCulture);
    }

    /// <summary>XKB layout of the session (desktop settings first, then X11, then the system default).</summary>
    private static string? LinuxLayoutCode()
    {
        var env = Environment.GetEnvironmentVariable("XKB_DEFAULT_LAYOUT");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").ToUpperInvariant();
        if (desktop.Contains("GNOME") || desktop.Contains("UNITY") || desktop.Contains("CINNAMON") || desktop.Contains("BUDGIE"))
        {
            var gs = Shell.Run("gsettings", ["get", "org.gnome.desktop.input-sources", "sources"], 2000);
            if (ParseGnomeSources(gs) is { } g) return g;
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (home is not null)
        {
            var kde = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "kxkbrc");
            if (File.Exists(kde) && ParseIni(File.ReadAllText(kde), "LayoutList") is { Length: > 0 } k) return k;
        }

        if (Shell.HasX11 && Shell.Run("setxkbmap", ["-query"], 2000) is { } x && ParseColonValue(x, "layout") is { } xl) return xl;
        if (Shell.Run("localectl", ["status"], 2000) is { } lc && ParseColonValue(lc, "X11 Layout") is { } ll) return ll;
        try
        {
            if (File.Exists(LinuxSys.P("etc/default/keyboard")) && ParseIni(File.ReadAllText(LinuxSys.P("etc/default/keyboard")), "XKBLAYOUT") is { } d) return d;
        }
        catch { }
        return null;
    }

    /// <summary>"[('xkb', 'de+nodeadkeys'), ('xkb', 'us')]" → "de+nodeadkeys".</summary>
    internal static string? ParseGnomeSources(string? text)
    {
        if (text is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(text, @"\(\s*'xkb'\s*,\s*'([^']+)'\s*\)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>"   X11 Layout: de" → "de" (localectl / setxkbmap -query).</summary>
    internal static string? ParseColonValue(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var i = line.IndexOf(':');
            if (i > 0 && line[..i].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var v = line[(i + 1)..].Trim();
                if (v.Length > 0 && v != "n/a") return v;
            }
        }
        return null;
    }

    /// <summary>KEY=value or KEY="value" from an ini / shell file.</summary>
    internal static string? ParseIni(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var i = line.IndexOf('=');
            if (i > 0 && line[..i].Trim().Equals(key, StringComparison.Ordinal))
            {
                var v = line[(i + 1)..].Trim().Trim('"');
                return v.Length > 0 ? v : null;
            }
        }
        return null;
    }

    // ---- Win32 --------------------------------------------------------------------------------

    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr hkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder buffer, int size, uint flags, IntPtr hkl);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint action, uint param, ref IntPtr value, uint winIni);
}
