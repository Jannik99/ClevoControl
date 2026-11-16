using ClevoControl.Core.Platform;
using K = ClevoControl.Core.Platform.KeyIds;

namespace ClevoControl.Core.Hardware.PerKey;

/// <summary>The two families of per-key keyboard controllers in Clevo (and TongFang) laptops.</summary>
public enum PerKeyController
{
    /// <summary>ITE 8291 (USB 048d:ce00 / 6004 / 6006 / 600a / 600b): 6 × 21 LED matrix, row 0 = bottom row.</summary>
    Ite8291,
    /// <summary>ITE 829x (USB 048d:8910): 6 × 20 LED matrix, row 0 = top row.</summary>
    Ite829x,
}

/// <summary>
/// Which LED(s) of the controller's matrix sit under which key. LEDs are numbered linearly, row by row
/// (index = row × columns + column), which is also the order in which tuxedo-drivers registers its LED class devices.
/// <para>Sources (checked 2026-11):</para>
/// <list type="bullet">
/// <item>ITE 8291: OpenRGB <c>Controllers/ClevoKeyboardController/ClevoKeyboardDevices.cpp</c> (commit d09a493f): the default
/// map (<c>clevo_tkl_values</c> + numpad edit keys), the "alternate" map (Left Shift = 22, ISO backslash = 23, 77 = upper half of
/// the ISO Enter) and the measured TUXEDO Stellaris 16 Gen7 map (Home 121, End 122, PgUp 123, PgDn 124). Where they differ, this
/// table follows the physical order (columns increase from left to right), and keys whose LED differs between boards get both LEDs.</item>
/// <item>ITE 829x: OpenRGB <c>clevo_829x_full_values</c> (LED id = row &lt;&lt; 5 | column, row 0 = top), consistent with the
/// "TUXEDO" demo pattern in tuxedo-drivers <c>src/ite_829x/ite_829x.c</c> (T = row 2 col 6, E = 2/4, D = 3/4, X = 4/4).</item>
/// </list>
/// Marked "inferred": not in any source, chosen from the matrix gaps; a wrong guess only leaves that key dark or lights an unused LED.
/// </summary>
public static class IteKeyMap
{
    public static int Rows(PerKeyController c) => 6;

    public static int Columns(PerKeyController c) => c == PerKeyController.Ite8291 ? 21 : 20;

    public static int LedCount(PerKeyController c) => Rows(c) * Columns(c);

    /// <summary>Linear LED index of a matrix position.</summary>
    public static int Index(PerKeyController c, int row, int col) => row * Columns(c) + col;

    /// <summary>ITE 829x LED id as used in its "set key" report: ((row &amp; 7) &lt;&lt; 5) | (col &amp; 0x1f).</summary>
    public static byte Ite829xLedId(int row, int col) => (byte)(((row & 0x07) << 5) | (col & 0x1F));

    // ---- ITE 8291 (row 0 = bottom) -----------------------------------------------------------

    private static readonly Dictionary<int, (int Row, int Col)[]> Ite8291Common = BuildIte8291();

    private static Dictionary<int, (int, int)[]> BuildIte8291()
    {
        var m = new Dictionary<int, (int, int)[]>();
        void A(int key, int row, params int[] cols) => m[key] = cols.Select(c => (row, c)).ToArray();

        // Row 5: Esc, F1..F12, PrtSc, Insert, Delete, Home, End, PgUp, PgDn (LED 105..124)
        A(K.Esc, 5, 0);
        for (var f = 1; f <= 12; f++) A(K.Function(f), 5, f);
        A(K.PrintScreen, 5, 13); A(K.Insert, 5, 14); A(K.Delete, 5, 15);
        A(K.Home, 5, 16); A(K.End, 5, 17); A(K.PageUp, 5, 18); A(K.PageDown, 5, 19); // Stellaris16A07 (physical order)

        // Row 4: ` 1..0 - = Backspace, numpad NumLock / * - (LED 84..102)
        A(0x29, 4, 0);
        for (var i = 0; i < 10; i++) A(0x02 + i, 4, 1 + i);
        A(0x0C, 4, 11); A(0x0D, 4, 12); A(K.Backspace, 4, 14);
        A(K.NumLock, 4, 15); A(K.NumDivide, 4, 16); A(K.NumMultiply, 4, 17); A(K.NumMinus, 4, 18);

        // Row 3: Tab, Q..P, [ ], (col 14: ANSI backslash or upper ISO Enter), numpad 7 8 9 + (LED 63..81)
        A(K.Tab, 3, 0);
        for (var i = 0; i < 10; i++) A(0x10 + i, 3, 2 + i);
        A(0x1A, 3, 12); A(0x1B, 3, 13);
        A(K.Num7, 3, 15); A(K.Num8, 3, 16); A(K.Num9, 3, 17); A(K.NumPlus, 3, 18);

        // Row 2: Caps, A..L ; ' (col 13: ISO #), Enter, numpad 4 5 6 (LED 42..59)
        A(K.CapsLock, 2, 0);
        for (var i = 0; i < 9; i++) A(0x1E + i, 2, 2 + i);
        A(0x27, 2, 11); A(0x28, 2, 12);
        A(K.Num4, 2, 15); A(K.Num5, 2, 16); A(K.Num6, 2, 17);

        // Row 1: Left Shift (col 0 on the default board, col 1 on the alternate/Stellaris boards), ISO < (col 2),
        // Z..M , . /, Right Shift, numpad 1 2 3 Enter (LED 21..39)
        A(K.LeftShift, 1, 0, 1);
        A(K.IsoBackslash, 1, 2);
        for (var i = 0; i < 7; i++) A(0x2C + i, 1, 3 + i);
        A(0x33, 1, 10); A(0x34, 1, 11); A(0x35, 1, 12); A(K.RightShift, 1, 14);
        A(K.Num1, 1, 15); A(K.Num2, 1, 16); A(K.Num3, 1, 17); A(K.NumEnter, 1, 18);

        // Row 0: Ctrl, Fn, Win, Alt, Space, AltGr, Ctrl, ← ↑ →, numpad 0 , and ↓ (LED 0..18)
        A(K.LeftCtrl, 0, 0); A(K.Fn, 0, 2); A(K.Win, 0, 3); A(K.LeftAlt, 0, 4); A(K.Space, 0, 7);
        A(K.RightAlt, 0, 10); A(K.RightCtrl, 0, 12);
        A(K.Left, 0, 13); A(K.Up, 0, 14); A(K.Right, 0, 15); A(K.Num0, 0, 16); A(K.NumDecimal, 0, 17); A(K.Down, 0, 18);
        return m;
    }

    // ---- ITE 829x (row 0 = top) --------------------------------------------------------------

    private static readonly Dictionary<int, (int Row, int Col)[]> Ite829xCommon = BuildIte829x();

    private static Dictionary<int, (int, int)[]> BuildIte829x()
    {
        var m = new Dictionary<int, (int, int)[]>();
        void A(int key, int row, params int[] cols) => m[key] = cols.Select(c => (row, c)).ToArray();

        A(K.Esc, 0, 0);
        for (var f = 1; f <= 12; f++) A(K.Function(f), 0, f);
        A(K.PrintScreen, 0, 13); A(K.Insert, 0, 14); A(K.Delete, 0, 15);
        A(K.Home, 0, 16); A(K.End, 0, 17); A(K.PageUp, 0, 18); A(K.PageDown, 0, 19);

        A(0x29, 1, 0);
        for (var i = 0; i < 10; i++) A(0x02 + i, 1, 1 + i);
        A(0x0C, 1, 11); A(0x0D, 1, 13); A(K.Backspace, 1, 14);
        A(K.NumLock, 1, 16); A(K.NumDivide, 1, 17); A(K.NumMultiply, 1, 18); A(K.NumMinus, 1, 19);

        A(K.Tab, 2, 0);
        for (var i = 0; i < 10; i++) A(0x10 + i, 2, 2 + i);
        A(0x1A, 2, 12); A(0x1B, 2, 13);
        A(K.Num7, 2, 16); A(K.Num8, 2, 17); A(K.Num9, 2, 18); A(K.NumPlus, 2, 19);

        A(K.CapsLock, 3, 0);
        for (var i = 0; i < 9; i++) A(0x1E + i, 3, 2 + i);
        A(0x27, 3, 11); A(0x28, 3, 12);
        A(K.Num4, 3, 16); A(K.Num5, 3, 17); A(K.Num6, 3, 18);

        A(K.LeftShift, 4, 0);
        for (var i = 0; i < 7; i++) A(0x2C + i, 4, 3 + i);
        A(0x33, 4, 10); A(0x34, 4, 11); A(0x35, 4, 12); A(K.RightShift, 4, 13); A(K.Up, 4, 15);
        A(K.Num1, 4, 16); A(K.Num2, 4, 17); A(K.Num3, 4, 18); A(K.NumEnter, 4, 19);

        A(K.LeftCtrl, 5, 0); A(K.Fn, 5, 2); A(K.Win, 5, 3); A(K.LeftAlt, 5, 4); A(K.Space, 5, 5);
        A(K.RightAlt, 5, 10); A(K.Menu, 5, 11); A(K.RightCtrl, 5, 12);
        A(K.Left, 5, 14); A(K.Down, 5, 15); A(K.Right, 5, 16); A(K.Num0, 5, 17); A(K.NumDecimal, 5, 18);
        return m;
    }

    /// <summary>
    /// Matrix positions of a key. <paramref name="iso"/> selects the ISO variant (tall Enter, "#" next to it, "&lt;" left of Z)
    /// for the keys whose position depends on the keyboard shape.
    /// </summary>
    public static IReadOnlyList<(int Row, int Col)> Positions(PerKeyController c, int key, bool iso)
    {
        if (c == PerKeyController.Ite8291)
        {
            switch (key)
            {
                // ANSI: Enter = LED 56, backslash above it = LED 77.
                // ISO:  "#" = LED 55, Enter = LED 77 (upper half, alternate/Stellaris maps) and 56 (default map).
                case K.Enter: return iso ? [(3, 14), (2, 14)] : [(2, 14)];
                case K.Hash: return iso ? [(2, 13)] : [(3, 14)];
                case K.IsoBackslash: return iso ? [(1, 2)] : [];
            }
            return Ite8291Common.TryGetValue(key, out var p) ? p : [];
        }
        switch (key)
        {
            case K.Enter: return iso ? [(3, 14), (2, 14)] : [(3, 14)];
            case K.Hash: return iso ? [(3, 13)] : [(2, 14)];   // ISO position inferred (OpenRGB: none)
            case K.IsoBackslash: return iso ? [(4, 1)] : [];   // inferred (OpenRGB: none)
        }
        return Ite829xCommon.TryGetValue(key, out var q) ? q : [];
    }

    /// <summary>True when the key has at least one LED on this controller.</summary>
    public static bool HasLed(PerKeyController c, int key, bool iso) => Positions(c, key, iso).Count > 0;

    /// <summary>
    /// Builds the full LED frame (linear index → color) from colors per key. LEDs without a key in
    /// <paramref name="colors"/> are black. Unknown key ids are ignored.
    /// </summary>
    public static RgbColor[] BuildFrame(PerKeyController c, IReadOnlyDictionary<int, RgbColor> colors, bool iso)
    {
        var frame = new RgbColor[LedCount(c)];
        foreach (var (key, color) in colors)
        {
            foreach (var (row, col) in Positions(c, key, iso))
                frame[Index(c, row, col)] = color;
        }
        return frame;
    }

    /// <summary>All key ids that have a mapping (both shapes).</summary>
    public static IEnumerable<int> MappedKeys(PerKeyController c) =>
        (c == PerKeyController.Ite8291 ? Ite8291Common.Keys : Ite829xCommon.Keys).Concat([K.Enter, K.Hash, K.IsoBackslash]);
}
