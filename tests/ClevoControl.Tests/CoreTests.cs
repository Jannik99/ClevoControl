using System.Net.Sockets;
using System.Text;
using ClevoControl.Core;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;
using Xunit;

namespace ClevoControl.Tests;

public class RgbColorTests
{
    [Theory]
    [InlineData("#FF8800", 255, 136, 0)]
    [InlineData("ff8800", 255, 136, 0)]
    [InlineData("#f80", 255, 136, 0)]
    [InlineData("0x0000FF", 0, 0, 255)]
    public void Parses_hex(string text, byte r, byte g, byte b) => Assert.Equal(new RgbColor(r, g, b), RgbColor.Parse(text));

    [Fact]
    public void Rejects_garbage() => Assert.False(RgbColor.TryParse("nope", out _));

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(12, 200, 90)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    public void Hsv_roundtrip(byte r, byte g, byte b)
    {
        var c = new RgbColor(r, g, b);
        var (h, s, v) = c.ToHsv();
        var back = RgbColor.FromHsv(h, s, v);
        Assert.InRange(Math.Abs(back.R - r), 0, 1);
        Assert.InRange(Math.Abs(back.G - g), 0, 1);
        Assert.InRange(Math.Abs(back.B - b), 0, 1);
    }

    [Fact]
    public void Kelvin_is_warm_at_night_and_neutral_at_day()
    {
        var warm = RgbColor.FromKelvin(2200);
        var day = RgbColor.FromKelvin(6500);
        Assert.True(warm.R > warm.B);
        Assert.InRange(day.R - day.B, -15, 15);
    }
}

public class FanCurveTests
{
    private static readonly List<FanCurvePoint> Curve = [new(40, 20), new(60, 40), new(80, 80)];

    [Theory]
    [InlineData(30, 20)]
    [InlineData(40, 20)]
    [InlineData(50, 30)]
    [InlineData(70, 60)]
    [InlineData(95, 80)]
    public void Interpolates(int temp, double expected) => Assert.Equal(expected, FanController.Evaluate(Curve, temp), 3);

    [Theory]
    [InlineData(0, 69, 0)]
    [InlineData(0, 70, 35)]
    [InlineData(10, 80, 70)]
    [InlineData(90, 82, 90)]
    [InlineData(0, 88, 100)]
    [InlineData(20, 99, 100)]
    public void Safety_floor_always_applies(double duty, int temp, double expected) => Assert.Equal(expected, FanController.Safe(duty, temp));
}

public class ScreenTests
{
    [Fact]
    public void Finds_monitors_and_samples_a_color()
    {
        var monitors = ScreenSampler.GetMonitors();
        Assert.NotEmpty(monitors);
        Assert.All(monitors, m => Assert.True(m.Width > 0 && m.Height > 0));
        using var sampler = new ScreenSampler();
        Assert.NotNull(sampler.Sample(monitors[0]));
    }
}

public class SettingsTests
{
    [Fact]
    public void Roundtrips_through_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clevocontrol-test-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(dir);
            Assert.True(store.IsFirstRun);
            store.Current.Keyboard.Color = new RgbColor(1, 2, 3);
            store.Current.Keyboard.Effect = LightingEffectKind.Music;
            store.Current.Fan.CpuCurve.Add(new FanCurvePoint(99, 99));
            store.SaveNow();
            var again = new SettingsStore(dir);
            Assert.False(again.IsFirstRun);
            Assert.Equal(new RgbColor(1, 2, 3), again.Current.Keyboard.Color);
            Assert.Equal(LightingEffectKind.Music, again.Current.Keyboard.Effect);
            Assert.Contains(again.Current.Fan.CpuCurve, p => p.Temperature == 99);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class OpenRgbServerTests
{
    private static byte[] Packet(uint dev, uint id, byte[] data)
    {
        var p = new byte[16 + data.Length];
        Encoding.ASCII.GetBytes("ORGB").CopyTo(p, 0);
        BitConverter.GetBytes(dev).CopyTo(p, 4);
        BitConverter.GetBytes(id).CopyTo(p, 8);
        BitConverter.GetBytes((uint)data.Length).CopyTo(p, 12);
        data.CopyTo(p, 16);
        return p;
    }

    private static async Task<(uint Id, byte[] Data)> Read(NetworkStream s)
    {
        var h = new byte[16];
        await s.ReadExactlyAsync(h);
        Assert.Equal("ORGB", Encoding.ASCII.GetString(h, 0, 4));
        var data = new byte[BitConverter.ToUInt32(h, 12)];
        await s.ReadExactlyAsync(data);
        return (BitConverter.ToUInt32(h, 8), data);
    }

    [Fact]
    public async Task Speaks_the_openrgb_protocol()
    {
        var received = new TaskCompletionSource<RgbColor>();
        var port = 46000 + Random.Shared.Next(1000);
        using var server = new OpenRgbServer(port, "Test Keyboard", () => new RgbColor(10, 20, 30), c => received.TrySetResult(c));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var s = client.GetStream();

        await s.WriteAsync(Packet(0, 40, BitConverter.GetBytes(4u)));
        var (id, data) = await Read(s);
        Assert.Equal(40u, id);
        Assert.Equal(1u, BitConverter.ToUInt32(data));

        await s.WriteAsync(Packet(0, 0, []));
        (id, data) = await Read(s);
        Assert.Equal(1u, BitConverter.ToUInt32(data));

        await s.WriteAsync(Packet(0, 1, BitConverter.GetBytes(1u)));
        (id, data) = await Read(s);
        Assert.Equal(1u, id);
        Assert.Equal((uint)data.Length, BitConverter.ToUInt32(data, 0)); // size prefix matches
        Assert.Equal(5, BitConverter.ToInt32(data, 4));                  // keyboard
        Assert.Contains("Test Keyboard", Encoding.ASCII.GetString(data));

        // UPDATELEDS: u32 size, u16 count, u32 color (R | G<<8 | B<<16)
        var payload = new byte[10];
        BitConverter.GetBytes(10u).CopyTo(payload, 0);
        BitConverter.GetBytes((ushort)1).CopyTo(payload, 4);
        BitConverter.GetBytes(0x00336699u).CopyTo(payload, 6);
        await s.WriteAsync(Packet(0, 1050, payload));
        var color = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new RgbColor(0x99, 0x66, 0x33), color);
    }
}

public class ProfileMappingTests
{
    // Regression test for the 0.1.0 bug where Performance was sent to the EC as Quiet.
    [Theory]
    [InlineData(PerformanceProfile.Quiet, 0u)]
    [InlineData(PerformanceProfile.PowerSaving, 1u)]
    [InlineData(PerformanceProfile.Performance, 2u)]
    [InlineData(PerformanceProfile.Entertainment, 3u)]
    public void Ec_value_equals_clevo_ui_index(PerformanceProfile p, uint ec) =>
        Assert.Equal(ec, ClevoControl.Core.Hardware.ClevoHardware.ProfileToEc(p));
}

public class KeyRemapTests
{
    [Theory]
    [InlineData("Ctrl+Shift+Esc", new[] { 0x11, 0x10, 0x1B })]
    [InlineData("Win+D", new[] { 0x5B, 0x44 })]
    [InlineData("MediaPlayPause", new[] { 0xB3 })]
    [InlineData("Strg + Alt + Entf", new[] { 0x11, 0x12, 0x2E })]
    [InlineData("F12", new[] { 0x7B })]
    public void Parses_shortcuts(string text, int[] expected) =>
        Assert.Equal(expected, ClevoControl.Core.Services.KeyRemapService.ParseShortcut(text));

    [Fact]
    public void Rejects_unknown_keys() => Assert.Null(ClevoControl.Core.Services.KeyRemapService.ParseShortcut("Ctrl+Banana"));

    [Fact]
    public void Describes_mapping() =>
        Assert.Equal("Ctrl+Shift+F5", ClevoControl.Core.Services.KeyRemapService.Describe(new KeyMapping { Key = 0x74, Ctrl = true, Shift = true }));

    [Fact]
    public void Filter_swallows_only_mapped_keys_and_their_key_up()
    {
        var svc = new ClevoControl.Core.Services.KeyRemapService();
        svc.Apply(new KeyRemapSettings { Enabled = true, Mappings = [new KeyMapping { Key = 0x13, Action = KeyActionType.Disable }] });
        Assert.True(svc.Filter(0x13, true));   // Pause down swallowed
        Assert.True(svc.Filter(0x13, false));  // and its key-up
        Assert.False(svc.Filter(0x41, true));  // 'A' untouched
        Assert.False(svc.Filter(0x13, false)); // stray key-up not swallowed
    }
}

public class MonitorTests
{
    [Fact]
    public void Cpu_monitor_reports_cores()
    {
        using var cpu = new ClevoControl.Core.Monitoring.CpuMonitor();
        Assert.True(cpu.LogicalCores > 0);
        var s = cpu.Sample();
        Assert.All(s.Cores, c => Assert.InRange(c.LoadPercent, 0, 100));
    }
}

public class FanOverrideTests
{
    private sealed class CountingHardware : ClevoControl.Core.Hardware.IClevoHardware
    {
        public List<string> Calls { get; } = [];
        public bool IsReal => false;
        public string Description => "test";
        public KeyboardType KeyboardType => KeyboardType.SingleZoneRgb;
        public (FanReading Cpu, FanReading Gpu, FanReading? Gpu2) ReadFans() => (new(1000, 30, 50), new(1000, 30, 50), null);
        public void SetKeyboardColor(RgbColor color) { }
        public void SetKeyboardZoneColor(int zone, RgbColor color) { }
        public void SetKeyboardBrightness(byte level) { }
        public void SetKeyboardPower(bool on) { }
        public void SetKeyboardHardwareEffect(HardwareEffect effect) { }
        public void SetFanMode(FanMode mode) => Calls.Add("mode:" + mode);
        public void SetFanDuty(byte cpu, byte gpu1, byte gpu2) => Calls.Add("duty");
        public void SetPerformanceProfile(PerformanceProfile profile) { }
        public PerformanceProfile? ReadStoredPerformanceProfile() => null;
        public ChargeLimitInfo? ReadChargeLimit() => null;
        public void SetChargeLimit(bool enabled, int start, int stop) { }
        public byte[] ReadSpecBuffer() => new byte[256];
        public byte[] ReadFanBuffer() => new byte[256];
    }

    [Fact]
    public void Auto_without_override_sends_nothing_on_start_suspend_and_resume()
    {
        var hw = new CountingHardware();
        using var sensors = new ClevoControl.Core.Services.SensorService(hw) { Paused = true };
        var fans = new ClevoControl.Core.Services.FanController(hw, sensors, new FanSettings { Mode = FanMode.Auto });
        fans.Suspend();
        fans.Resume();
        fans.Dispose();
        Assert.Empty(hw.Calls);
    }

    [Fact]
    public void Max_then_auto_hands_control_back_once()
    {
        var hw = new CountingHardware();
        using var sensors = new ClevoControl.Core.Services.SensorService(hw) { Paused = true };
        var settings = new FanSettings { Mode = FanMode.Max };
        var fans = new ClevoControl.Core.Services.FanController(hw, sensors, settings);
        Assert.True(settings.OverrideActive);
        settings.Mode = FanMode.Auto;
        fans.Apply(settings);
        Assert.False(settings.OverrideActive);
        Assert.Equal(["mode:Max", "mode:Auto"], hw.Calls);
        fans.Dispose();
        Assert.Equal(2, hw.Calls.Count); // nothing more on exit
    }

    [Fact]
    public void Leftover_override_from_a_crash_is_restored_on_start()
    {
        var hw = new CountingHardware();
        using var sensors = new ClevoControl.Core.Services.SensorService(hw) { Paused = true };
        var settings = new FanSettings { Mode = FanMode.Auto, OverrideActive = true };
        _ = new ClevoControl.Core.Services.FanController(hw, sensors, settings);
        Assert.Equal(["mode:Auto"], hw.Calls);
        Assert.False(settings.OverrideActive);
    }
}
