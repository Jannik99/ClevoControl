using ClevoControl.Core;
using ClevoControl.Core.Hardware.Linux;
using ClevoControl.Core.Platform;
using Xunit;

namespace ClevoControl.Tests;

/// <summary>Only runs on Linux (e.g. sysfs LED names contain ":", which Windows paths do not allow).</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Linux only";
    }
}

/// <summary>Linux backend tests. /sys is faked in a temp folder and tuxedo_io is mocked.</summary>
[Collection("LinuxSysRoot")] // LinuxSys.Root is global state
public sealed class LinuxTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("clevo-sys-").FullName;

    public LinuxTests() => LinuxSys.Root = _root;

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
        public uint[] FanInfo = [0, 0, 0];
        public readonly List<string> Calls = [];
        public bool IsClevo() => true;
        public string InterfaceName() => "clevo_acpi";
        public string ModuleVersion() => "4.0.0";
        public uint ReadFanInfo(int fan) => FanInfo[fan];
        public void SetFanSpeed(uint packed) => Calls.Add($"speed {packed:X6}");
        public void SetFanAuto(uint mask) => Calls.Add($"auto {mask:X}");
        public void SetPerformanceProfile(uint value) => Calls.Add($"profile {value}");
        public void Dispose() { }
    }

    [Fact]
    public void Ioctl_numbers_match_the_kernel_encoding()
    {
        // _IOR(0xEC, 0x05, int32_t*) etc. on 64-bit Linux.
        Assert.Equal(0x8008EC05u, TuxedoIo.ReadHwCheckClevo);
        Assert.Equal(0x8008ED00u, TuxedoIo.ReadClevoInterface);
        Assert.Equal(0x8008ED10u, TuxedoIo.ReadClevoFanInfo1);
        Assert.Equal(0x4008EE10u, TuxedoIo.WriteClevoFanSpeed);
        Assert.Equal(0x4008EE11u, TuxedoIo.WriteClevoFanAuto);
        Assert.Equal(0x4008EE15u, TuxedoIo.WriteClevoPerfProfile);
    }

    [Fact]
    public void Parses_proc_stat()
    {
        var r = LinuxSys.ParseProcStat("cpu  100 5 50 800 20 1 2 3 0 0\ncpu0 1 2 3 4\n");
        Assert.Equal((820UL, 981UL), r);
    }

    [Fact]
    public void Parses_meminfo()
    {
        var (pct, gb) = LinuxSys.ParseMemInfo("MemTotal:       16777216 kB\nMemFree:  1 kB\nMemAvailable:    4194304 kB\n");
        Assert.Equal(75, pct);
        Assert.Equal(16, gb, 3);
    }

    [Fact]
    public void Decodes_fan_info()
    {
        var f = LinuxClevoHardware.DecodeFanInfo(0x00_3A_00_80); // 58 °C, duty 0x80
        Assert.Equal(58, f.TemperatureC);
        Assert.Equal(50, f.DutyPercent);
        Assert.Equal(0, f.Rpm);
    }

    [Fact]
    public void Undoes_the_driver_white_balance()
    {
        Assert.Equal(new RgbColor(255, 255, 255), LinuxClevoHardware.CompensateDriverGain(new RgbColor(181, 255, 200)));
        Assert.Equal(new RgbColor(0, 10, 0), LinuxClevoHardware.CompensateDriverGain(new RgbColor(0, 10, 0)));
    }

    [Fact]
    public void Formats_intensity_in_multi_index_order()
    {
        Assert.Equal("1 2 3", LinuxClevoHardware.FormatIntensity("red green blue", new RgbColor(1, 2, 3)));
        Assert.Equal("3 1 2", LinuxClevoHardware.FormatIntensity("blue red green", new RgbColor(1, 2, 3)));
    }

    [LinuxFact]
    public void Three_zone_keyboard_fans_and_profile()
    {
        foreach (var n in new[] { "rgb:kbd_backlight", "rgb:kbd_backlight_1", "rgb:kbd_backlight_2" })
        {
            Put($"sys/class/leds/{n}/multi_index", "red green blue\n");
            Put($"sys/class/leds/{n}/multi_intensity", "0 0 0");
            Put($"sys/class/leds/{n}/brightness", "0");
        }
        Put("sys/class/leds/input3::capslock/brightness", "0");
        var io = new FakeIo { FanInfo = [0x00_2D_00_40, 0x00_30_00_00, 0x00_00_00_00] };
        var hw = new LinuxClevoHardware(io);

        Assert.Equal(KeyboardType.ThreeZoneRgb, hw.KeyboardType);
        hw.SetKeyboardZoneColor(2, new RgbColor(10, 20, 30));
        Assert.Equal("10 20 30", Get("sys/class/leds/rgb:kbd_backlight_2/multi_intensity"));
        Assert.Equal("0 0 0", Get("sys/class/leds/rgb:kbd_backlight/multi_intensity"));

        hw.SetKeyboardBrightness(128);
        Assert.Equal("128", Get("sys/class/leds/rgb:kbd_backlight_1/brightness"));
        hw.SetKeyboardPower(false);
        Assert.Equal("0", Get("sys/class/leds/rgb:kbd_backlight/brightness"));
        hw.SetKeyboardPower(true);
        Assert.Equal("128", Get("sys/class/leds/rgb:kbd_backlight/brightness"));

        var (cpu, gpu, gpu2) = hw.ReadFans();
        Assert.Equal(45, cpu.TemperatureC);
        Assert.Equal(48, gpu.TemperatureC);
        Assert.Null(gpu2);

        hw.SetFanDuty(0x80, 0x90, 0x90);
        hw.SetFanMode(FanMode.Max);
        hw.SetFanMode(FanMode.Auto);
        hw.SetPerformanceProfile(PerformanceProfile.Performance);
        Assert.Equal(["speed 909080", "speed FFFFFF", "auto F", "profile 2"], io.Calls);
    }

    [LinuxFact]
    public void Single_zone_keyboard_is_compensated_and_white_keyboard_is_stepped()
    {
        Put("sys/class/leds/rgb:kbd_backlight/multi_index", "red green blue");
        Put("sys/class/leds/rgb:kbd_backlight/multi_intensity", "0 0 0");
        var hw = new LinuxClevoHardware(new FakeIo());
        Assert.Equal(KeyboardType.SingleZoneRgb, hw.KeyboardType);
        hw.SetKeyboardColor(new RgbColor(90, 100, 100));
        Assert.Equal("128 100 128", Get("sys/class/leds/rgb:kbd_backlight/multi_intensity"));

        Directory.Delete(Path.Combine(_root, "sys/class/leds/rgb:kbd_backlight"), true);
        Put("sys/class/leds/white:kbd_backlight/max_brightness", "5");
        Put("sys/class/leds/white:kbd_backlight/brightness", "0");
        var white = new LinuxClevoHardware(new FakeIo());
        Assert.Equal(KeyboardType.WhiteOnly, white.KeyboardType);
        white.SetKeyboardBrightness(255);
        Assert.Equal("5", Get("sys/class/leds/white:kbd_backlight/brightness"));
        white.SetKeyboardBrightness(10);
        Assert.Equal("1", Get("sys/class/leds/white:kbd_backlight/brightness"));
    }

    [Fact]
    public void Reads_battery_and_charge_thresholds()
    {
        Put("sys/class/power_supply/AC/type", "Mains");
        Put("sys/class/power_supply/AC/online", "0");
        var b = "sys/class/power_supply/BAT0/";
        Put(b + "type", "Battery");
        Put(b + "status", "Discharging");
        Put(b + "capacity", "64");
        Put(b + "energy_full_design", "80000000");
        Put(b + "energy_full", "72000000");
        Put(b + "energy_now", "46000000");
        Put(b + "power_now", "23000000");
        Put(b + "cycle_count", "37");
        Put(b + "charge_control_start_threshold", "40");
        Put(b + "charge_control_end_threshold", "100");

        var info = LinuxSys.ReadBattery();
        Assert.True(info.Present);
        Assert.False(info.OnAc);
        Assert.Equal(64, info.Percent);
        Assert.Equal(90, info.HealthPercent!.Value, 1);
        Assert.Equal(TimeSpan.FromHours(2), info.Remaining);
        Assert.Equal(-23000, info.RateMw);
        Assert.Equal(37, info.CycleCount);

        var hw = new LinuxClevoHardware(new FakeIo());
        var limit = hw.ReadChargeLimit()!;
        Assert.False(limit.Enabled);
        hw.SetChargeLimit(true, 60, 80);
        Assert.Equal("60", Get(b + "charge_control_start_threshold"));
        Assert.Equal("80", Get(b + "charge_control_end_threshold"));
        Assert.True(hw.ReadChargeLimit()!.Enabled);
    }
}
