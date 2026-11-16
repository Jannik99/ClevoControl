using ClevoControl.Core;
using ClevoControl.Core.Games;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Hardware.Linux;
using ClevoControl.Core.Hardware.PerKey;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Platform;
using ClevoControl.Core.Settings;
using Xunit;

namespace ClevoControl.Tests;

/// <summary>Per-key RGB: detection, key → LED mapping and the HID reports (all pure functions).</summary>
public sealed class PerKeyTests
{
    // ---- HID reports ------------------------------------------------------------------------------

    [Fact]
    public void Ite8291_control_reports_match_tuxedo_drivers()
    {
        Assert.Equal(new byte[] { 0x08, 0x02, 0x33, 0x00, 0x32, 0x00, 0x00, 0x00 }, Ite8291Protocol.UserMode(0x40)); // clamped to 0x32
        Assert.Equal(new byte[] { 0x08, 0x02, 0x33, 0x00, 0x10, 0x00, 0x00, 0x00 }, Ite8291Protocol.UserMode(0x10));
        Assert.Equal(new byte[] { 0x08, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, Ite8291Protocol.Off());
        Assert.Equal(new byte[] { 0x16, 0x00, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00 }, Ite8291Protocol.AnnounceRow(5));
        Assert.Equal(new byte[] { 0x14, 0x00, 0x01, 1, 2, 3, 0x00, 0x00 }, Ite8291Protocol.Palette(1, new RgbColor(1, 2, 3)));
        Assert.Equal(new byte[] { 0x08, 0x02, 0x03, 0x05, 0x32, 0x08, 0x01, 0x00 }, Ite8291Protocol.Effect(0x03, 5, 0x32, 8, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Ite8291Protocol.AnnounceRow(6));
        Assert.Equal(0, Ite8291Protocol.ScaleBrightness(0));
        Assert.Equal(0x32, Ite8291Protocol.ScaleBrightness(255));
        Assert.Equal(0x19, Ite8291Protocol.ScaleBrightness(128));
    }

    [Fact]
    public void Ite8291_row_report_is_report_id_padding_then_blue_green_red()
    {
        var frame = new RgbColor[126];
        frame[2 * 21 + 0] = new RgbColor(0x11, 0x22, 0x33);   // row 2, column 0
        frame[2 * 21 + 20] = new RgbColor(0xA1, 0xA2, 0xA3);  // row 2, column 20
        frame[3 * 21 + 0] = new RgbColor(0xFF, 0xFF, 0xFF);   // other row: not in the report
        var r = Ite8291Protocol.RowReport(frame, 2);
        Assert.Equal(65, r.Length);
        Assert.Equal(0, r[0]);
        Assert.Equal(0, r[1]);
        Assert.Equal(0x33, r[2]);        // blue, column 0
        Assert.Equal(0x22, r[2 + 21]);   // green, column 0
        Assert.Equal(0x11, r[2 + 42]);   // red, column 0
        Assert.Equal(0xA3, r[2 + 20]);
        Assert.Equal(0xA2, r[2 + 41]);
        Assert.Equal(0xA1, r[2 + 62]);
        Assert.Equal(6, r.Count(b => b != 0));
    }

    [Fact]
    public void Ite829x_reports_match_tuxedo_drivers()
    {
        // The "TUXEDO" demo of ite_829x lights T at row 2, column 6.
        Assert.Equal(0x46, IteKeyMap.Ite829xLedId(2, 6));
        Assert.Equal(new byte[] { 0xCC, 0x01, 0x46, 0xFF, 0x00, 0x00 }, Ite829xProtocol.SetKey(0x46, new RgbColor(255, 0, 0)));
        Assert.Equal(new byte[] { 0xCC, 0x09, 0x0A, 0x02, 0x00, 0x00 }, Ite829xProtocol.Brightness(0x30));
        Assert.Equal(new byte[] { 0xCC, 0x00, 0x09, 0x00, 0x00, 0x00 }, Ite829xProtocol.RandomAnimation());
        Assert.Equal(10, Ite829xProtocol.ScaleBrightness(255));
    }

    // ---- detection --------------------------------------------------------------------------------

    private static HidInfo Hid(ushort product, ushort version = 3, ushort page = 0xFF03, ushort usage = 0x01, int feature = 9, int output = 65) =>
        new(0x048D, product, version, page, usage, feature, output);

    [Theory]
    [InlineData(0x600B)]
    [InlineData(0x6004)]
    [InlineData(0x6006)]
    [InlineData(0x600A)]
    [InlineData(0xCE00)]
    public void Windows_detects_ite8291_per_key_keyboards(int product) =>
        Assert.Equal(PerKeyController.Ite8291, WindowsHidPerKey.Classify(Hid((ushort)product)));

    [Fact]
    public void Windows_detects_ite829x() =>
        Assert.Equal(PerKeyController.Ite829x, WindowsHidPerKey.Classify(Hid(0x8910, page: 0xFF89, usage: 0xCC, feature: 6, output: 0)));

    [Fact]
    public void Windows_rejects_everything_else()
    {
        Assert.Null(WindowsHidPerKey.Classify(Hid(0xCE00, version: 0x0002)));            // 4-zone keyboard (tuxedo-drivers)
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x600B, page: 0x0001, usage: 0x06)));  // a keyboard collection, not the LED one
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x600B, feature: 0)));                 // no feature report
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x600B, output: 9)));                  // no 64-byte row report
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x7001)));                             // ite_8291_lb light bar
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x6010)));                             // light bar
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x8297)));                             // ite_8297
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x8910)));                             // 829x id, wrong usage page
        Assert.Null(WindowsHidPerKey.Classify(Hid(0x600B) with { Vendor = 0x1234 }));
    }

    private static IEnumerable<LinuxSysfsPerKey.LedEntry> Leds(int count, string? hidId, int firstSuffix = 0) =>
        Enumerable.Range(firstSuffix, count).Select(n => new LinuxSysfsPerKey.LedEntry(n == 0 ? "rgb:kbd_backlight" : $"rgb:kbd_backlight_{n}", hidId));

    [Fact]
    public void Linux_detects_ite8291_led_class_devices_in_matrix_order()
    {
        // Directory order is alphabetical (_1, _10, _100 …); the matrix order is the numeric suffix.
        var leds = Leds(126, "0003:0000048D:0000600B").OrderBy(l => l.Name, StringComparer.Ordinal);
        var d = LinuxSysfsPerKey.Detect(leds);
        Assert.NotNull(d);
        Assert.Equal(PerKeyController.Ite8291, d.Controller);
        Assert.Equal("rgb:kbd_backlight", d.OrderedNames[0]);
        Assert.Equal("rgb:kbd_backlight_105", d.OrderedNames[105]); // Esc: row 5, column 0
        Assert.Equal("rgb:kbd_backlight_125", d.OrderedNames[125]);
    }

    [Fact]
    public void Linux_detection_handles_shifted_names_and_other_leds()
    {
        // An EC keyboard LED (no HID parent) registered first takes the plain name; the ITE LEDs become _1 … _126.
        var leds = new[] { new LinuxSysfsPerKey.LedEntry("rgb:kbd_backlight", null) }.Concat(Leds(126, "0003:0000048D:0000600B", 1));
        var d = LinuxSysfsPerKey.Detect(leds);
        Assert.NotNull(d);
        Assert.Equal("rgb:kbd_backlight_1", d.OrderedNames[0]);
        Assert.Equal("rgb:kbd_backlight_106", d.OrderedNames[105]);
    }

    [Fact]
    public void Linux_detects_ite829x() =>
        Assert.Equal(PerKeyController.Ite829x, LinuxSysfsPerKey.Detect(Leds(120, "0003:0000048D:00008910"))?.Controller);

    [Fact]
    public void Linux_rejects_zone_keyboards_and_unknown_devices()
    {
        Assert.Null(LinuxSysfsPerKey.Detect(Leds(3, null)));                                   // clevo_leds 3 zones
        Assert.Null(LinuxSysfsPerKey.Detect(Leds(4, "0003:0000048D:0000CE00")));               // ite_8291 4-zone variant
        Assert.Null(LinuxSysfsPerKey.Detect(Leds(126, null)));                                 // not a HID device
        Assert.Null(LinuxSysfsPerKey.Detect(Leds(126, "0003:00001234:0000600B")));             // other vendor
        Assert.Null(LinuxSysfsPerKey.Detect(Leds(126, "0003:0000048D:00007001")));             // light bar id
        Assert.Null(LinuxSysfsPerKey.Detect(Leds(125, "0003:0000048D:0000600B")));             // incomplete matrix
        Assert.Null(LinuxSysfsPerKey.Detect(Leds(120, "0003:0000048D:0000600B")));             // 8291 needs 126
        var gap = Leds(127, "0003:0000048D:0000600B").Where(l => l.Name != "rgb:kbd_backlight_50");
        Assert.Null(LinuxSysfsPerKey.Detect(gap));                                             // not one contiguous block
        Assert.Equal(((ushort)0x048D, (ushort)0x600B), LinuxSysfsPerKey.ParseHidId("0003:0000048D:0000600B"));
        Assert.Null(LinuxSysfsPerKey.ParseHidId("garbage"));
    }

    [Fact]
    public void Linux_keyboard_type_from_led_count()
    {
        Assert.Equal(KeyboardType.ThreeZoneRgb, LinuxClevoHardware.ClassifyKeyboard(3, false, false));
        Assert.Equal(KeyboardType.SingleZoneRgb, LinuxClevoHardware.ClassifyKeyboard(1, false, false));
        Assert.Equal(KeyboardType.SingleZoneRgb, LinuxClevoHardware.ClassifyKeyboard(4, false, false)); // ITE 4 zones, one color
        Assert.Equal(KeyboardType.SingleZoneRgb, LinuxClevoHardware.ClassifyKeyboard(126, false, false)); // not detected = no per-key
        Assert.Equal(KeyboardType.PerKeyRgb, LinuxClevoHardware.ClassifyKeyboard(0, false, true));
        Assert.Equal(KeyboardType.WhiteOnly, LinuxClevoHardware.ClassifyKeyboard(0, true, false));
        Assert.Equal(KeyboardType.None, LinuxClevoHardware.ClassifyKeyboard(0, false, false));
    }

    // ---- key → LED mapping --------------------------------------------------------------------------

    private static int Led(PerKeyController c, int key, bool iso = false)
    {
        var p = IteKeyMap.Positions(c, key, iso);
        Assert.NotEmpty(p);
        return IteKeyMap.Index(c, p[0].Row, p[0].Col);
    }

    [Fact]
    public void Ite8291_mapping_matches_openrgb()
    {
        var c = PerKeyController.Ite8291;
        Assert.Equal(105, Led(c, KeyIds.Esc));
        Assert.Equal(117, Led(c, KeyIds.F12));
        Assert.Equal(84, Led(c, 0x29));   // `
        Assert.Equal(98, Led(c, KeyIds.Backspace));
        Assert.Equal(65, Led(c, 0x10));   // Q
        Assert.Equal(44, Led(c, 0x1E));   // A
        Assert.Equal(24, Led(c, 0x2C));   // Z
        Assert.Equal(7, Led(c, KeyIds.Space));
        Assert.Equal(14, Led(c, KeyIds.Up));
        Assert.Equal(18, Led(c, KeyIds.Down));
        Assert.Equal(39, Led(c, KeyIds.NumEnter));
        Assert.Equal(99, Led(c, KeyIds.NumLock));
        Assert.Equal(56, Led(c, KeyIds.Enter));           // ANSI Enter
        Assert.Equal(77, Led(c, KeyIds.Hash));            // ANSI backslash
        Assert.Equal(55, Led(c, KeyIds.Hash, iso: true)); // ISO #
        Assert.Equal(23, Led(c, KeyIds.IsoBackslash, iso: true));
        Assert.Equal([77, 56], IteKeyMap.Positions(c, KeyIds.Enter, true).Select(p => IteKeyMap.Index(c, p.Row, p.Col)));
        Assert.Empty(IteKeyMap.Positions(c, KeyIds.IsoBackslash, false));
    }

    [Fact]
    public void Ite829x_mapping_matches_openrgb()
    {
        var c = PerKeyController.Ite829x;
        int Id(int key) { var p = IteKeyMap.Positions(c, key, false)[0]; return IteKeyMap.Ite829xLedId(p.Row, p.Col); }
        Assert.Equal(0x00, Id(KeyIds.Esc));
        Assert.Equal(0x2D, Id(0x0D));     // =
        Assert.Equal(0x46, Id(0x14));     // T
        Assert.Equal(0x64, Id(0x20));     // D
        Assert.Equal(0x84, Id(0x2D));     // X
        Assert.Equal(0x8F, Id(KeyIds.Up));
        Assert.Equal(0xA5, Id(KeyIds.Space));
        Assert.Equal(0xB2, Id(KeyIds.NumDecimal));
        Assert.Equal(0x6E, Id(KeyIds.Enter));
    }

    [Theory]
    [InlineData(PerKeyController.Ite8291, "de")]
    [InlineData(PerKeyController.Ite8291, "us")]
    [InlineData(PerKeyController.Ite829x, "de")]
    [InlineData(PerKeyController.Ite829x, "us")]
    public void Every_key_of_the_picture_has_its_own_led(PerKeyController c, string layoutId)
    {
        var layout = KeyboardLayout.ForTests(layoutId);
        var keys = KeyboardGeometry.Build(layout, numpad: true);
        var owner = new Dictionary<int, int>();
        foreach (var k in keys)
        {
            var positions = IteKeyMap.Positions(c, k.Id, layout.Iso);
            // OpenRGB has no Menu key on the ITE 8291 boards.
            if (positions.Count == 0) { Assert.True(c == PerKeyController.Ite8291 && k.Id == KeyIds.Menu, $"no LED for {k.Label} ({k.Id:X})"); continue; }
            foreach (var (row, col) in positions)
            {
                Assert.InRange(row, 0, IteKeyMap.Rows(c) - 1);
                Assert.InRange(col, 0, IteKeyMap.Columns(c) - 1);
                var index = IteKeyMap.Index(c, row, col);
                Assert.False(owner.TryGetValue(index, out var other) && other != k.Id, $"LED {index} used by {other:X} and {k.Id:X}");
                owner[index] = k.Id;
            }
        }
    }

    [Fact]
    public void Frame_contains_only_the_painted_keys()
    {
        var frame = IteKeyMap.BuildFrame(PerKeyController.Ite8291, new Dictionary<int, RgbColor> { [KeyIds.Esc] = new(1, 2, 3), [0x7777] = new(9, 9, 9) }, iso: false);
        Assert.Equal(126, frame.Length);
        Assert.Equal(new RgbColor(1, 2, 3), frame[105]);
        Assert.Equal(1, frame.Count(c => c != RgbColor.Black));
    }

    // ---- key ids of the keyboard picture --------------------------------------------------------------

    [Fact]
    public void Picture_keys_are_identified_by_scan_code()
    {
        var de = KeyboardGeometry.Build(KeyboardLayout.ForTests("de"), numpad: true);
        var us = KeyboardGeometry.Build(KeyboardLayout.ForTests("us"), numpad: true);
        Assert.Equal(de.Count, de.Select(k => k.Id).Distinct().Count());
        Assert.Equal(0x15, de.Single(k => k.Label == "Z").Id); // QWERTZ: Z sits where US has Y
        Assert.Equal(0x2C, us.Single(k => k.Label == "Z").Id);
        Assert.True(de.Single(k => k.Id == KeyIds.Enter).LowerWidth > 0);  // ISO Enter is L-shaped
        Assert.Equal(0, us.Single(k => k.Id == KeyIds.Enter).LowerWidth);
        foreach (var id in new[] { KeyIds.Fn, KeyIds.LeftShift, KeyIds.RightShift, KeyIds.LeftCtrl, KeyIds.RightCtrl, KeyIds.Win, KeyIds.LeftAlt, KeyIds.RightAlt, KeyIds.NumEnter, KeyIds.IsoBackslash })
            Assert.Contains(de, k => k.Id == id);
        Assert.DoesNotContain(us, k => k.Id == KeyIds.IsoBackslash);
    }

    // ---- engine and simulation ------------------------------------------------------------------------

    [Fact]
    public void Simulated_per_key_keyboard_only_with_env_switch()
    {
        var before = Environment.GetEnvironmentVariable("CC_SIM_KEYBOARD");
        try
        {
            Environment.SetEnvironmentVariable("CC_SIM_KEYBOARD", null);
            var plain = new SimulatedHardware("test");
            Assert.Null(((IClevoHardware)plain).PerKey);
            Assert.Equal(KeyboardType.SingleZoneRgb, plain.KeyboardType);

            Environment.SetEnvironmentVariable("CC_SIM_KEYBOARD", "perkey");
            var perKey = new SimulatedHardware("test");
            Assert.NotNull(((IClevoHardware)perKey).PerKey);
            Assert.Equal(KeyboardType.PerKeyRgb, perKey.KeyboardType);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CC_SIM_KEYBOARD", before);
        }
    }

    [Fact]
    public void Per_key_base_only_writes_changes()
    {
        var kb = new SimulatedPerKey();
        kb.SetKeyColors(new Dictionary<int, RgbColor> { [KeyIds.Esc] = new(255, 0, 0) });
        Assert.Equal(1, kb.Writes);
        kb.SetKeyColors(new Dictionary<int, RgbColor> { [KeyIds.Esc] = new(255, 0, 0) });
        Assert.Equal(1, kb.Writes);
        kb.SetPower(false);
        kb.SetAll(RgbColor.White); // remembered, not written while off
        Assert.Equal(1, kb.Writes);
        kb.SetPower(true);
        Assert.Equal(2, kb.Writes);
        Assert.All(kb.Frame, c => Assert.Equal(RgbColor.White, c));
    }

    [Fact]
    public void Failed_brightness_write_is_retried()
    {
        var kb = new SimulatedPerKey();
        kb.SetAll(RgbColor.White); // clears the initial "send everything" state
        kb.FailNextWrite = true;
        Assert.Throws<IOException>(() => kb.SetBrightness(100));
        Assert.Empty(kb.BrightnessWrites);
        kb.SetBrightness(100); // same level again: must still be sent
        Assert.Equal([(byte)100], kb.BrightnessWrites);
        kb.SetBrightness(100); // now it is on the controller
        Assert.Single(kb.BrightnessWrites);
    }

    [Fact]
    public void Brightness_change_keeps_a_running_hardware_effect()
    {
        var kb = new SimulatedPerKey();
        kb.SetAll(RgbColor.White);
        kb.SetHardwareEffect(HardwareEffect.Wave, new RgbColor(255, 0, 0));
        Assert.True(kb.HardwareEffectRunning);
        var frames = kb.Writes;
        kb.SetBrightness(50);
        Assert.True(kb.HardwareEffectRunning);
        Assert.Equal(2, kb.EffectWrites); // the effect was restarted with the new brightness ...
        Assert.Equal(frames, kb.Writes);  // ... instead of sending the rows (user mode)
        kb.SetAll(RgbColor.White);
        Assert.False(kb.HardwareEffectRunning); // a frame ends the effect
    }

    [Fact]
    public void Null_per_key_colors_in_settings_become_empty()
    {
        var kb = System.Text.Json.JsonSerializer.Deserialize<KeyboardSettings>("""{"PerKeyColors": null}""", SettingsStore.JsonOptions)!;
        Assert.NotNull(kb.PerKeyColors);
        Assert.Empty(kb.PerKeyColors);

        var dir = Directory.CreateTempSubdirectory("clevo-settings-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "settings.json"), """{"Keyboard": {"PerKeyColors": null}}""");
            var store = new SettingsStore(dir);
            Assert.Empty(store.Current.Keyboard.PerKeyColors);
            store.Dispose();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Engine_renders_painted_keys_and_waves()
    {
        using var engine = new LightingEngine(new SimulatedHardware("test"), new KeyboardSettings { Enabled = false }, () => SensorSnapshot.Empty, new Cs2Integration());
        var s = new KeyboardSettings { Effect = LightingEffectKind.PerKey, PerKeyColors = { [KeyIds.Esc] = new(1, 2, 3) } };
        var painted = engine.RenderKeys(LightingEffectKind.PerKey, s, 0);
        Assert.NotNull(painted);
        Assert.Equal(new RgbColor(1, 2, 3), Assert.Single(painted.Value.Colors).Value);

        var rainbow = engine.RenderKeys(LightingEffectKind.Spectrum, s, 0)!.Value.Colors;
        Assert.NotEqual(rainbow[KeyIds.Esc], rainbow[KeyIds.Num0]); // hue changes from left to right
        Assert.Null(engine.RenderKeys(LightingEffectKind.Breathing, s, 0)); // uniform effects stay one color
    }
}

/// <summary>Per-key detection on a fake /sys tree (Linux only: LED names contain ':').</summary>
[Collection("LinuxSysRoot")]
public sealed class LinuxPerKeyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("clevo-perkey-").FullName;

    public LinuxPerKeyTests() => LinuxSys.Root = _root;

    public void Dispose()
    {
        LinuxSys.Root = "/";
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Put(string path, string content)
    {
        var full = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Get(string path) => File.ReadAllText(Path.Combine(_root, path));

    private sealed class FakeIo : ITuxedoIo
    {
        public bool IsClevo() => true;
        public string InterfaceName() => "clevo_wmi";
        public string ModuleVersion() => "4.0.0";
        public uint ReadFanInfo(int fan) => 0x00_30_00_00;
        public void SetFanSpeed(uint packed) { }
        public void SetFanAuto(uint mask) { }
        public void SetPerformanceProfile(uint value) { }
        public void Dispose() { }
    }

    [LinuxFact]
    public void Ite8291_sysfs_keyboard_is_per_key_and_writes_the_right_led()
    {
        for (var n = 0; n < 126; n++)
        {
            var name = n == 0 ? "rgb:kbd_backlight" : $"rgb:kbd_backlight_{n}";
            Put($"sys/class/leds/{name}/multi_index", "red green blue\n");
            Put($"sys/class/leds/{name}/multi_intensity", "255 255 255");
            Put($"sys/class/leds/{name}/brightness", "0");
            Put($"sys/class/leds/{name}/max_brightness", "50");
            Put($"sys/class/leds/{name}/device/uevent", "DRIVER=tuxedo-ite8291\nHID_ID=0003:0000048D:0000600B\nHID_NAME=ITE Tech. Inc. ITE Device(8291)\n");
            Put($"sys/class/leds/{name}/device/controls/buffer_input", "0");
        }
        using var hw = new LinuxClevoHardware(new FakeIo());
        Assert.Equal(KeyboardType.PerKeyRgb, hw.KeyboardType);
        var pk = ((IClevoHardware)hw).PerKey;
        Assert.NotNull(pk);
        Assert.Equal(PerKeyController.Ite8291, pk.Controller);

        hw.SetKeyboardBrightness(255);
        Assert.Equal("50", Get("sys/class/leds/rgb:kbd_backlight/brightness"));
        pk.SetKeyColors(new Dictionary<int, RgbColor> { [KeyIds.Esc] = new(10, 20, 30) });
        Assert.Equal("10 20 30", Get("sys/class/leds/rgb:kbd_backlight_105/multi_intensity"));
        Assert.Equal("0 0 0", Get("sys/class/leds/rgb:kbd_backlight_104/multi_intensity"));
        Assert.Equal("0", Get("sys/class/leds/rgb:kbd_backlight/device/controls/buffer_input")); // flushed
    }

    [LinuxFact]
    public void Four_ite_zones_are_not_per_key()
    {
        for (var n = 0; n < 4; n++)
        {
            var name = n == 0 ? "rgb:kbd_backlight" : $"rgb:kbd_backlight_{n}";
            Put($"sys/class/leds/{name}/multi_index", "red green blue\n");
            Put($"sys/class/leds/{name}/device/uevent", "HID_ID=0003:0000048D:0000CE00\n");
        }
        using var hw = new LinuxClevoHardware(new FakeIo());
        Assert.Null(((IClevoHardware)hw).PerKey);
        Assert.Equal(KeyboardType.SingleZoneRgb, hw.KeyboardType);
    }
}
