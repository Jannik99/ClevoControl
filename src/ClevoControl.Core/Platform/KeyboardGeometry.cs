namespace ClevoControl.Core.Platform;

/// <summary>
/// Layout-independent key identifiers for per-key lighting: the PC set-1 scan code of the physical key,
/// extended keys as 0xE0xx (e.g. Up = 0xE048). The Fn key sends no scan code and gets <see cref="Fn"/>.
/// Stored in the settings, so a painted keyboard keeps its colors when the user switches QWERTZ ↔ QWERTY.
/// </summary>
public static class KeyIds
{
    public const int Esc = 0x01, Backspace = 0x0E, Tab = 0x0F, Enter = 0x1C, LeftCtrl = 0x1D, LeftShift = 0x2A, RightShift = 0x36,
        LeftAlt = 0x38, Space = 0x39, CapsLock = 0x3A, F1 = 0x3B, F11 = 0x57, F12 = 0x58,
        NumLock = 0x45, NumMultiply = 0x37, NumMinus = 0x4A, NumPlus = 0x4E, NumDecimal = 0x53,
        Num7 = 0x47, Num8 = 0x48, Num9 = 0x49, Num4 = 0x4B, Num5 = 0x4C, Num6 = 0x4D, Num1 = 0x4F, Num2 = 0x50, Num3 = 0x51, Num0 = 0x52,
        NumEnter = 0xE01C, RightCtrl = 0xE01D, NumDivide = 0xE035, PrintScreen = 0xE037, RightAlt = 0xE038,
        Home = 0xE047, Up = 0xE048, PageUp = 0xE049, Left = 0xE04B, Right = 0xE04D, End = 0xE04F, Down = 0xE050, PageDown = 0xE051,
        Insert = 0xE052, Delete = 0xE053, Win = 0xE05B, Menu = 0xE05D;

    /// <summary>Pseudo id of the Fn key (handled by the EC, it has no scan code).</summary>
    public const int Fn = 0x0100;

    /// <summary>The key next to Enter (US backslash, ISO "#").</summary>
    public const int Hash = 0x2B;

    /// <summary>The extra ISO key left of Z ("&lt;" on German keyboards).</summary>
    public const int IsoBackslash = 0x56;

    /// <summary>Scan codes of F1..F12.</summary>
    public static int Function(int n) => n switch
    {
        >= 1 and <= 10 => F1 + n - 1,
        11 => F11,
        12 => F12,
        _ => throw new ArgumentOutOfRangeException(nameof(n)),
    };

    /// <summary>Scan code of a virtual key that is not a character key (Esc, F-keys, navigation, numpad …), or 0.</summary>
    internal static int FromVk(int vk) => vk switch
    {
        0x1B => Esc,
        >= 0x70 and <= 0x7B => Function(vk - 0x6F),
        0x2C => PrintScreen,
        0x2D => Insert,
        0x2E => Delete,
        0x08 => Backspace,
        0x09 => Tab,
        0x0D => Enter,
        0x14 => CapsLock,
        0x20 => Space,
        0x5D => Menu,
        0x25 => Left,
        0x26 => Up,
        0x27 => Right,
        0x28 => Down,
        0x24 => Home,
        0x23 => End,
        0x21 => PageUp,
        0x22 => PageDown,
        0x90 => NumLock,
        0x6F => NumDivide,
        0x6A => NumMultiply,
        0x6D => NumMinus,
        0x6B => NumPlus,
        0x6E => NumDecimal,
        0x60 => Num0,
        0x61 => Num1,
        0x62 => Num2,
        0x63 => Num3,
        0x64 => Num4,
        0x65 => Num5,
        0x66 => Num6,
        0x67 => Num7,
        0x68 => Num8,
        0x69 => Num9,
        _ => 0,
    };
}

/// <summary>
/// One key of the keyboard picture with its per-key id, in pixels. <see cref="LowerWidth"/> &gt; 0 marks the
/// L-shaped ISO Enter key (upper part <see cref="Width"/>, lower part right-aligned, two rows high).
/// </summary>
public readonly record struct KeyRect(int Id, string Label, double X, double Y, double Width, double Height, double LowerWidth = 0);

/// <summary>
/// Positions of all keys (including modifiers, Fn and the number pad) for the per-key editor and for
/// effects that move across the keyboard. Built from <see cref="KeyboardLayout.MainRows"/> /
/// <see cref="KeyboardLayout.NumpadRows"/>, so it has the same shape as the remapping keyboard.
/// </summary>
public static class KeyboardGeometry
{
    public static IReadOnlyList<KeyRect> Build(KeyboardLayout layout, bool numpad, double unit = 44, double gap = 4)
    {
        var keys = new List<KeyRect>();
        var main = layout.MainRows();
        var width = AddMain(keys, layout, main, unit, gap);
        if (numpad) AddNumpad(keys, layout.NumpadRows(), width + unit * 0.5, unit, gap);
        return keys;
    }

    private static double Px(double w, double unit, double gap) => w * unit + (w - 1) * gap;

    private static double AddMain(List<KeyRect> keys, KeyboardLayout layout, IReadOnlyList<IReadOnlyList<KeyCap>> rows, double unit, double gap)
    {
        double maxX = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            var x = 0.0;
            var y = r * (unit + gap);
            var row = rows[r];
            for (var i = 0; i < row.Count; i++)
            {
                var (label, vk, w) = row[i];
                var px = Px(w, unit, gap);
                var isLowerEnter = vk == 0x0D && label.Length == 0;
                if (!isLowerEnter)
                {
                    var id = IdFor(layout, r, i, row, vk);
                    if (id != 0)
                    {
                        // ISO Enter: the upper part is joined with the lower part of the next row.
                        var lower = vk == 0x0D && r + 1 < rows.Count
                            ? rows[r + 1].FirstOrDefault(k => k.Vk == 0x0D && k.Label.Length == 0)
                            : default;
                        keys.Add(lower.Width > 0
                            ? new KeyRect(id, label, x, y, px, 2 * unit + gap, Px(lower.Width, unit, gap))
                            : new KeyRect(id, label, x, y, px, unit));
                    }
                }
                x += px + gap;
            }
            maxX = Math.Max(maxX, x - gap);
        }
        return maxX;
    }

    /// <summary>Per-key id of a key on the main block (row and index in <see cref="KeyboardLayout.MainRows"/>).</summary>
    internal static int IdFor(KeyboardLayout layout, int row, int index, IReadOnlyList<KeyCap> keys, int vk)
    {
        if (vk == 0)
        {
            // Modifiers have no virtual key in the picture: identify them by position.
            if (row == 4) return index == 0 ? KeyIds.LeftShift : index == keys.Count - 1 ? KeyIds.RightShift : 0;
            if (row == 5)
            {
                return index switch
                {
                    0 => KeyIds.LeftCtrl,
                    1 => KeyIds.Fn,
                    2 => KeyIds.Win,
                    3 => KeyIds.LeftAlt,
                    5 => KeyIds.RightAlt,
                    7 => KeyIds.RightCtrl,
                    _ => 0,
                };
            }
            return 0;
        }
        if (row is >= 1 and <= 4 && vk is not (0x08 or 0x09 or 0x0D or 0x14) && layout.ScanForVk(vk) is { } scan) return scan;
        return KeyIds.FromVk(vk);
    }

    private static void AddNumpad(List<KeyRect> keys, IReadOnlyList<IReadOnlyList<KeyCap>> rows, double x0, double unit, double gap)
    {
        var step = unit + gap;
        for (var r = 0; r < rows.Count; r++)
        {
            var x = x0;
            var y = r * step;
            foreach (var (label, vk, w) in rows[r])
            {
                var px = Px(w, unit, gap);
                var id = KeyIds.FromVk(vk);
                if (id == KeyIds.NumPlus)
                    keys.Add(new KeyRect(id, label, x, y, px, 2 * unit + gap)); // "+" is two rows high
                else if (id != 0)
                    keys.Add(new KeyRect(id, label, x, y, px, unit));
                else if (vk == 0 && label.Length == 0 && r == rows.Count - 2)
                    keys.Add(new KeyRect(KeyIds.NumEnter, "↵", x, y, px, 2 * unit + gap)); // gap below 3 = number pad Enter
                x += px + gap;
            }
        }
    }

    /// <summary>Key centres scaled to 0..1 (x from left to right, y from top to bottom), for effects.</summary>
    public static IReadOnlyDictionary<int, (double X, double Y)> NormalizedCenters(IReadOnlyList<KeyRect> keys)
    {
        var result = new Dictionary<int, (double X, double Y)>();
        if (keys.Count == 0) return result;
        var maxX = keys.Max(k => k.X + k.Width);
        var maxY = keys.Max(k => k.Y + k.Height);
        foreach (var k in keys) result.TryAdd(k.Id, ((k.X + k.Width / 2) / maxX, (k.Y + k.Height / 2) / maxY));
        return result;
    }
}
