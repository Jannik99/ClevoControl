using System.Globalization;
using System.Text.RegularExpressions;
using ClevoControl.Core.Hardware.Linux;

namespace ClevoControl.Core.Hardware.PerKey;

/// <summary>
/// Per-key keyboard on Linux through tuxedo-drivers (<c>ite_8291</c> / <c>ite_829x</c>).
/// <para>
/// What the drivers expose (tuxedo-drivers commit 07a681fc): one multicolor LED class device per matrix position, all with the
/// name <c>rgb:kbd_backlight</c>; the LED core makes the names unique as <c>rgb:kbd_backlight</c>, <c>_1</c>, <c>_2</c> … in
/// registration order, which is row by row (ite_8291: 6 × 21 = 126 LEDs, ite_829x: 6 × 20 = 120). The parent device of each LED
/// is the USB HID device (<c>device/uevent</c> has <c>HID_ID=0003:0000048D:0000600B</c>). Brightness is global: writing it on any
/// LED sets all (max 0x32 resp. 10). ite_8291 has <c>device/controls/buffer_input</c>: while 1, color writes only fill the
/// driver's buffer; writing 0 sends everything at once.
/// </para>
/// </summary>
public sealed partial class LinuxSysfsPerKey : PerKeyKeyboardBase
{
    private readonly string[] _leds;
    private readonly string _hidId;
    private readonly int _max;
    private readonly string? _multiIndex;
    private string? _bufferInput;

    private LinuxSysfsPerKey(PerKeyController controller, string[] leds, string hidId) : base(controller)
    {
        _leds = leds;
        _hidId = hidId;
        _max = ReadInt(Path.Combine(leds[0], "max_brightness")) ?? (controller == PerKeyController.Ite8291 ? Ite8291Protocol.MaxBrightness : Ite829xProtocol.MaxBrightness);
        try { _multiIndex = File.ReadAllText(Path.Combine(leds[0], "multi_index")).Trim(); } catch { }
        var buffer = Path.Combine(leds[0], "device", "controls", "buffer_input");
        if (controller == PerKeyController.Ite8291 && File.Exists(buffer)) _bufferInput = buffer;
    }

    /// <summary>The LED directories in matrix order (linear index).</summary>
    public IReadOnlyList<string> Leds => _leds;

    public override string Description =>
        $"{(Controller == PerKeyController.Ite8291 ? "ITE 8291" : "ITE 829x")} ({_hidId}), tuxedo-drivers, {_leds.Length} LEDs";

    /// <summary>One <c>rgb:kbd_backlight*</c> LED: its name and the HID_ID of its parent device (null if it is no HID device).</summary>
    public readonly record struct LedEntry(string Name, string? HidId);

    public sealed record Detection(PerKeyController Controller, IReadOnlyList<string> OrderedNames, string HidId);

    [GeneratedRegex(@"^rgb:kbd_backlight(?:_(\d+))?$")]
    private static partial Regex LedName();

    /// <summary>Parses "0003:0000048D:0000600B" into (vendor, product).</summary>
    internal static (ushort Vendor, ushort Product)? ParseHidId(string? hidId)
    {
        var parts = hidId?.Split(':');
        if (parts is not { Length: 3 }) return null;
        if (!uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ||
            !uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var p)) return null;
        return ((ushort)v, (ushort)p);
    }

    /// <summary>
    /// Strict, pure detection: a per-key keyboard is a group of <c>rgb:kbd_backlight*</c> LEDs whose parent HID device is an ITE
    /// per-key controller (vendor 048d; 8291: ce00/6004/6006/600a/600b, 829x: 8910) with exactly one LED per matrix position
    /// (126 or 120; the 4-zone ce00 variant has 4) and contiguous name suffixes. Returns the names in matrix order.
    /// </summary>
    public static Detection? Detect(IEnumerable<LedEntry> leds)
    {
        foreach (var group in leds.Where(l => l.HidId is not null).GroupBy(l => l.HidId!.ToUpperInvariant()))
        {
            if (ParseHidId(group.Key) is not { } id || id.Vendor != WindowsHidPerKey.IteVendor) continue;
            PerKeyController? controller = WindowsHidPerKey.Ite8291Products.Contains(id.Product) ? PerKeyController.Ite8291
                : id.Product == WindowsHidPerKey.Ite829xProduct ? PerKeyController.Ite829x : null;
            if (controller is not { } c) continue;

            var numbered = new List<(int N, string Name)>();
            foreach (var l in group)
            {
                var m = LedName().Match(l.Name);
                if (!m.Success) continue;
                numbered.Add((m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0, l.Name));
            }
            if (numbered.Count != IteKeyMap.LedCount(c)) continue;
            numbered.Sort((a, b) => a.N.CompareTo(b.N));
            if (numbered[^1].N - numbered[0].N != numbered.Count - 1) continue; // registered in one go, row by row
            return new Detection(c, numbered.Select(n => n.Name).ToList(), group.Key);
        }
        return null;
    }

    /// <summary>Reads the HID_ID of an LED's parent device (null if it has none, e.g. the EC keyboard LEDs of clevo_leds).</summary>
    internal static string? ReadHidId(string ledDir)
    {
        try
        {
            var uevent = Path.Combine(ledDir, "device", "uevent");
            if (!File.Exists(uevent)) return null;
            foreach (var line in File.ReadAllLines(uevent))
                if (line.StartsWith("HID_ID=", StringComparison.Ordinal)) return line[7..].Trim();
        }
        catch { }
        return null;
    }

    /// <summary>Looks for a per-key keyboard among these LED directories (full paths).</summary>
    public static LinuxSysfsPerKey? TryCreate(IEnumerable<string> ledDirs)
    {
        var dirs = ledDirs.ToList();
        var byName = dirs.ToDictionary(d => Path.GetFileName(d), StringComparer.Ordinal);
        var detection = Detect(dirs.Select(d => new LedEntry(Path.GetFileName(d), ReadHidId(d))));
        if (detection is null) return null;
        Log.Info($"Per-key: {detection.Controller} ({detection.HidId}) with {detection.OrderedNames.Count} LEDs via tuxedo-drivers");
        return new LinuxSysfsPerKey(detection.Controller, detection.OrderedNames.Select(n => byName[n]).ToArray(), detection.HidId);
    }

    // ---- transport ----------------------------------------------------------------------------

    protected override void WriteFrame(RgbColor[] frame, bool[] changed, bool all)
    {
        var buffered = _bufferInput is not null && TryWrite(_bufferInput, "1");
        try
        {
            for (var i = 0; i < _leds.Length; i++)
            {
                if (!all && !changed[i]) continue;
                Write(Path.Combine(_leds[i], "multi_intensity"), LinuxClevoHardware.FormatIntensity(_multiIndex, frame[i]));
            }
        }
        finally
        {
            if (buffered) Write(_bufferInput!, "0"); // sends all rows
        }
    }

    protected override void WriteBrightness(byte level) =>
        Write(Path.Combine(_leds[0], "brightness"), ((int)Math.Round(level * _max / 255.0)).ToString(CultureInfo.InvariantCulture));

    protected override void WriteOff() => Write(Path.Combine(_leds[0], "brightness"), "0");

    protected override void WriteHardwareEffect(HardwareEffect effect, RgbColor color)
    {
        // tuxedo-drivers does not expose the controllers' own animations; the static color stays.
        Log.Warn($"Hardware keyboard effect {effect} is not available for per-key keyboards on Linux.");
    }

    private bool TryWrite(string path, string value)
    {
        try
        {
            File.WriteAllText(path, value);
            return true;
        }
        catch (Exception ex)
        {
            // Without write access to buffer_input (udev rule) every key is sent separately – slower, but it works.
            Log.Warn($"Per-key: cannot use {path} ({ex.Message}); writing keys one by one.");
            _bufferInput = null;
            return false;
        }
    }

    private static void Write(string path, string value)
    {
        try
        {
            File.WriteAllText(path, value);
        }
        catch (UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException($"No permission to write {path} – install the udev rule from contrib/linux (see docs/LINUX.md).");
        }
    }

    private static int? ReadInt(string path)
    {
        try { return int.TryParse(File.ReadAllText(path).Trim(), out var v) ? v : null; }
        catch { return null; }
    }
}
