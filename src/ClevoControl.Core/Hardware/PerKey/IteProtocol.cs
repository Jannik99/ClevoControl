namespace ClevoControl.Core.Hardware.PerKey;

/// <summary>
/// HID reports of the ITE 8291 per-key controller. Pure functions (unit-tested).
/// <para>Sources: tuxedo-drivers <c>src/ite_8291/ite_8291.c</c> (commit 07a681fc, header comment and
/// <c>ite8291_write_rows</c>), ite8291r3-ctl <c>ite8291r3.py</c> and OpenRGB <c>ClevoKeyboardController.cpp</c> – all three agree.</para>
/// <list type="bullet">
/// <item>Control commands are 8-byte feature reports. The device declares no report ids: on Windows the buffer for
/// <c>HidD_SetFeature</c> is report id 0 followed by the 8 bytes (as ite8291r3-ctl sends with wValue 0x0300).</item>
/// <item>Per-key colors: "user mode" <c>08 02 33 00 [brightness 0..0x32] 00 00 00</c>, then for each row 0..5
/// <c>16 00 [row] 00 00 00 00 00</c> followed by a 64-byte output report (interrupt OUT): <c>00, B×21, G×21, R×21</c>.</item>
/// <item>Row 0 is the bottom row (Ctrl / Space), row 5 the top row (Esc / F-keys).</item>
/// </list>
/// </summary>
public static class Ite8291Protocol
{
    public const int Rows = 6, Columns = 21;
    public const byte MaxBrightness = 0x32;
    public const byte ModeUser = 0x33;

    /// <summary>Feature report payload length (without the report id byte).</summary>
    public const int ControlLength = 8;

    /// <summary>Output report length including the leading report id byte (0).</summary>
    public const int RowReportLength = 2 + Columns * 3; // 65

    /// <summary>App brightness 0..255 → controller brightness 0..0x32.</summary>
    public static byte ScaleBrightness(byte level) => (byte)Math.Round(level * MaxBrightness / 255.0);

    /// <summary>Enables per-key ("user") mode with a brightness of 0..0x32.</summary>
    public static byte[] UserMode(byte brightness) => [0x08, 0x02, ModeUser, 0x00, Math.Min(brightness, MaxBrightness), 0x00, 0x00, 0x00];

    /// <summary>Turns the backlight off.</summary>
    public static byte[] Off() => [0x08, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    /// <summary>Announces the row whose colors follow in the next output report.</summary>
    public static byte[] AnnounceRow(int row) =>
        row is < 0 or >= Rows ? throw new ArgumentOutOfRangeException(nameof(row)) : [0x16, 0x00, (byte)row, 0x00, 0x00, 0x00, 0x00, 0x00];

    /// <summary>Sets palette slot 1..7 (used by the built-in effects).</summary>
    public static byte[] Palette(int slot, RgbColor c) =>
        slot is < 1 or > 7 ? throw new ArgumentOutOfRangeException(nameof(slot)) : [0x14, 0x00, (byte)slot, c.R, c.G, c.B, 0x00, 0x00];

    /// <summary>
    /// Starts a built-in effect: <c>08 02 [mode] [speed 1..10] [brightness] [color 1..7, 8 = random] [direction / key mode] 00</c>.
    /// </summary>
    public static byte[] Effect(byte mode, byte speed, byte brightness, byte color, byte behaviour) =>
        [0x08, 0x02, mode, Math.Clamp(speed, (byte)1, (byte)10), Math.Min(brightness, MaxBrightness), color, behaviour, 0x00];

    /// <summary>
    /// Output report for one row: [0] report id 0, [1] 0, then blue, green and red of the 21 columns.
    /// <paramref name="frame"/> holds all 126 LEDs (index = row × 21 + column).
    /// </summary>
    public static byte[] RowReport(ReadOnlySpan<RgbColor> frame, int row)
    {
        if (frame.Length != Rows * Columns) throw new ArgumentException("A frame has 126 LEDs.", nameof(frame));
        if (row is < 0 or >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
        var buf = new byte[RowReportLength];
        for (var col = 0; col < Columns; col++)
        {
            var c = frame[row * Columns + col];
            buf[2 + col] = c.B;
            buf[2 + Columns + col] = c.G;
            buf[2 + 2 * Columns + col] = c.R;
        }
        return buf;
    }

    /// <summary>
    /// Built-in effect for one of ClevoControl's hardware effects (mode numbers from the tuxedo-drivers header comment):
    /// 02 breathing, 03 wave, 05 rainbow, 06 ripple, 09 marquee, 0a raindrop, 0e aurora, 11 spark.
    /// Returns (mode, color slot, behaviour); color slot 1 = the user's color, 8 = random colors.
    /// </summary>
    public static (byte Mode, byte Color, byte Behaviour) EffectFor(HardwareEffect e) => e switch
    {
        HardwareEffect.Breathe => (0x02, 1, 0x00),
        HardwareEffect.Wave => (0x03, 8, 0x01),        // left → right
        HardwareEffect.Cycle => (0x05, 8, 0x00),       // rainbow
        HardwareEffect.Dance => (0x0E, 8, 0x01),       // aurora, automatic
        HardwareEffect.Flash => (0x11, 8, 0x01),       // spark, automatic
        HardwareEffect.RandomColor => (0x0A, 8, 0x00), // raindrop
        HardwareEffect.Tempo => (0x09, 8, 0x00),       // marquee
        _ => (0x02, 1, 0x00),
    };
}

/// <summary>
/// HID reports of the ITE 829x per-key controller (USB 048d:8910). Pure functions (unit-tested).
/// <para>Source: tuxedo-drivers <c>src/ite_829x/ite_829x.c</c> (<c>keyb_send_data</c>, <c>leds_set_brightness_mc</c>):
/// 6-byte feature reports with report id 0xCC: <c>cc [cmd] d0 d1 d2 d3</c>. Same in OpenRGB.</para>
/// </summary>
public static class Ite829xProtocol
{
    public const int Rows = 6, Columns = 20;
    public const byte MaxBrightness = 0x0A;
    public const byte ReportId = 0xCC;
    public const int ReportLength = 6;

    public static byte ScaleBrightness(byte level) => (byte)Math.Round(level * MaxBrightness / 255.0);

    /// <summary>Sets one key: <c>cc 01 [led id] R G B</c>.</summary>
    public static byte[] SetKey(byte ledId, RgbColor c) => [ReportId, 0x01, ledId, c.R, c.G, c.B];

    /// <summary>Brightness 0..10 (0 = off): <c>cc 09 [brightness] 02 00 00</c>.</summary>
    public static byte[] Brightness(byte level) => [ReportId, 0x09, Math.Min(level, MaxBrightness), 0x02, 0x00, 0x00];

    /// <summary>The controller's random color animation: <c>cc 00 09 00 00 00</c>.</summary>
    public static byte[] RandomAnimation() => [ReportId, 0x00, 0x09, 0x00, 0x00, 0x00];
}
