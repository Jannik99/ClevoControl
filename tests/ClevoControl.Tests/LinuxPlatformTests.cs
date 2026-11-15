using ClevoControl.Core.Platform;
using ClevoControl.Core.Settings;
using Xunit;

namespace ClevoControl.Tests;

/// <summary>Parsers and pure logic of the Linux platform layer (run on every OS).</summary>
public class LinuxPlatformTests
{
    [Fact]
    public void Parses_pactl_volume_and_mute()
    {
        Assert.Equal(45, LinuxAudio.ParseVolumePercent("Volume: front-left: 29491 /  45% / -20.81 dB,   front-right: 29491 /  45% / -20.81 dB"));
        Assert.Null(LinuxAudio.ParseVolumePercent(null));
        Assert.True(LinuxAudio.ParseMute("Mute: yes"));
        Assert.False(LinuxAudio.ParseMute("Mute: no\n"));
    }

    [Fact]
    public void Parses_pactl_devices()
    {
        var text = """
            Sink #47
            	State: RUNNING
            	Name: alsa_output.pci-0000_00_1f.3.analog-stereo
            	Description: Built-in Audio Analog Stereo
            Sink #48
            	Name: bluez_output.AA_BB.1
            	Description: Headphones
            """;
        var d = LinuxAudio.ParseDevices(text);
        Assert.Equal(2, d.Count);
        Assert.Equal("Headphones", d[1].Description);
        Assert.Equal("alsa_output.pci-0000_00_1f.3.analog-stereo", d[0].Name);
    }

    [Fact]
    public void Counts_only_real_microphone_recorders()
    {
        var monitors = LinuxAudio.ParseMonitorSourceIds("51\talsa_output.pci.analog-stereo.monitor\tPipeWire\ts32le 2ch 48000Hz\tRUNNING\n52\talsa_input.pci.analog-stereo\tPipeWire\ts32le 2ch 48000Hz\tRUNNING\n");
        Assert.Equal(["51"], monitors);
        var outputs = """
            Source Output #100
            	Source: 51
            	Properties:
            		application.process.binary = "parec"
            Source Output #101
            	Source: 52
            	Properties:
            		application.process.binary = "firefox"
            """;
        Assert.Equal(1, LinuxAudio.CountMicrophoneRecorders(outputs, monitors));
        Assert.Equal(0, LinuxAudio.CountMicrophoneRecorders(outputs.Replace("Source: 52", "Source: 51"), monitors));
    }

    [Fact]
    public void Parses_xrandr_panel_rates()
    {
        var text = """
            Screen 0: minimum 320 x 200, current 1920 x 1080, maximum 16384 x 16384
            eDP-1 connected primary 1920x1080+0+0 (normal left inverted right x axis y axis) 344mm x 193mm
               1920x1080    144.00*+  60.01    59.97
               1680x1050     60.00
            HDMI-1 disconnected (normal left inverted right x axis y axis)
            """;
        var outputs = LinuxPower.ParseXrandr(text);
        var panel = Assert.Single(outputs);
        Assert.Equal("eDP-1", panel.Name);
        Assert.True(panel.Primary);
        Assert.Equal(144.0, panel.Current);
        Assert.Equal([144.0, 60.01, 59.97], panel.Rates);
    }

    [Fact]
    public void Parses_gdbus_idle_time()
    {
        Assert.Equal(12345, LinuxPower.ParseGdbusNumber("(uint64 12345,)"));
        Assert.Null(LinuxPower.ParseGdbusNumber("Error: no such interface"));
    }

    [Fact]
    public void Identifies_wine_and_native_processes()
    {
        Assert.Equal(("Cyberpunk2077", @"Z:\home\u\.local\share\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe"),
            LinuxProcesses.Identify([@"Z:\home\u\.local\share\Steam\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe"], "/usr/bin/wine64-preloader", "Cyberpunk2077.e"));
        Assert.Equal(("mpv", "/usr/bin/mpv"), LinuxProcesses.Identify(["mpv", "film.mkv"], "/usr/bin/mpv", "mpv"));
        Assert.Equal(("kworker", null), LinuxProcesses.Identify([], null, "kworker"));
    }

    [Fact]
    public void Parses_xprop_window()
    {
        Assert.Equal("0x3c00007", LinuxProcesses.ParseWindowId("_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3c00007"));
        var w = LinuxProcesses.ParseXpropWindow("_NET_WM_PID(CARDINAL) = 4242\n_NET_WM_NAME(UTF8_STRING) = \"Netflix – Mozilla Firefox\"");
        Assert.Equal((4242, "Netflix – Mozilla Firefox"), w);
    }

    [Fact]
    public void Parses_xrandr_monitors()
    {
        var m = ScreenGrabber.ParseXrandrMonitors("Monitors: 2\n 0: +*eDP-1 1920/344x1080/193+0+0  eDP-1\n 1: +HDMI-1 2560/597x1440/336+1920+0  HDMI-1\n");
        Assert.Equal(2, m.Count);
        Assert.True(m[0].Primary);
        Assert.Equal((1920, 0, 2560, 1440), (m[1].X, m[1].Y, m[1].Width, m[1].Height));
    }

    [Fact]
    public void Decodes_ppm_and_downscales()
    {
        // 2×2 image: red, green / blue, white
        var header = "P6\n# comment\n2 2\n255\n"u8.ToArray();
        byte[] pixels = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255];
        Assert.True(Ppm.TryDecode([.. header, .. pixels], out var px, out var w, out var h));
        Assert.Equal((2, 2), (w, h));
        Assert.Equal(0xFF0000u, px[0]);
        var dest = new uint[1];
        ScreenGrabber.Downscale(px, 2, 2, 2, dest, 1, 1);
        Assert.Equal((uint)(127 << 16 | 127 << 8 | 127), dest[0]); // average of the four
    }

    [Fact]
    public void Finds_real_keyboards_in_proc_bus_input()
    {
        var text = """
            I: Bus=0019 Vendor=0000 Product=0001 Version=0000
            N: Name="Power Button"
            H: Handlers=kbd event2
            B: EV=3

            I: Bus=0011 Vendor=0001 Product=0001 Version=ab41
            N: Name="AT Translated Set 2 keyboard"
            H: Handlers=sysrq kbd leds event3
            B: EV=120013

            I: Bus=0003 Vendor=046d Product=c52b Version=0111
            N: Name="Logitech USB Receiver Mouse"
            H: Handlers=mouse0 event5
            B: EV=17
            """;
        Assert.Equal(["event3"], LinuxKeyboard.Keyboards(text));
    }

    [Fact]
    public void Recognises_scene_hotkeys_from_evdev_codes()
    {
        using var kb = new LinuxKeyboard();
        var hits = new List<int>();
        var presses = 0;
        kb.Hotkey += hits.Add;
        kb.KeyPressed += () => presses++;
        // Ctrl (29) + Alt (56) + Shift (42) + 3 (code 4)
        kb.OnKey(29, 1); kb.OnKey(56, 1); kb.OnKey(42, 1); kb.OnKey(4, 1); kb.OnKey(4, 2); kb.OnKey(4, 0);
        kb.OnKey(33, 1); // F → fans
        kb.OnKey(29, 0); kb.OnKey(11, 1); // Ctrl released: 0 is not a hotkey
        Assert.Equal([3, 20], hits);
        Assert.Equal(6, presses); // auto-repeat does not count
    }

    [Fact]
    public void Generates_keyd_config()
    {
        var settings = new KeyRemapSettings
        {
            Mappings =
            [
                new KeyMapping { Key = 0x14, Action = KeyActionType.Disable },                                  // CapsLock off
                new KeyMapping { Key = 'Z', Ctrl = true, Action = KeyActionType.Shortcut, Value = "Ctrl+Shift+Esc" },
                new KeyMapping { Key = 0x7B, Action = KeyActionType.Text, Value = "Hello World" },              // F12
                new KeyMapping { Key = 0x7A, Action = KeyActionType.Launch, Value = "firefox" },                // F11
            ],
        };
        var conf = KeydConfig.Generate(settings, lockWindowsKey: true);
        Assert.Contains("[ids]\n*", conf.Replace("\r", ""));
        Assert.Contains("capslock = noop", conf);
        Assert.Contains("leftmeta = noop", conf);
        Assert.Contains("[control]", conf);
        Assert.Contains("z = C-S-esc", conf);           // trigger keys are physical positions (US names)
        Assert.Contains("f12 = macro(S-h e l l o space S-w o r l d)", conf);
        Assert.Contains("# not converted: F11", conf);
        Assert.Null(KeydConfig.Text("50%"));
    }

    [Fact]
    public void Support_check_tells_clevo_uniwill_and_system76_apart()
    {
        string[] none = [];
        var clevo = SupportCheck.Evaluate(["CLV0001:00", "PNP0C0A:00"], [SupportCheck.SharedWmiGuid], ["clevo_acpi", "tuxedo_io"], ["8087:0029"], "Notebook", "N960TF", true);
        Assert.Equal(SupportVerdict.Supported, clevo.Verdict);

        var noDriver = SupportCheck.Evaluate(["CLV0001:00"], none, none, none, "Notebook", "PD5x_7xSNC_SND_SNE", false);
        Assert.Equal(SupportVerdict.Partial, noDriver.Verdict);

        var perKey = SupportCheck.Evaluate(["CLV0001:00"], none, ["clevo_acpi"], ["048d:ce00"], "Notebook", "X170KM-G", true);
        Assert.Equal(SupportVerdict.Partial, perKey.Verdict);
        Assert.Contains("per-key", perKey.Summary);

        var uniwill = SupportCheck.Evaluate(none, [SupportCheck.SharedWmiGuid, SupportCheck.UniwillOnlyWmiGuids[0]], ["uniwill_wmi"], none, "SchenkerTechnologiesGmbH", "GM6PX7X", true);
        Assert.Equal(SupportVerdict.NotSupported, uniwill.Verdict);

        var s76 = SupportCheck.Evaluate(none, none, ["system76_acpi"], none, "System76", "Oryx Pro", false);
        Assert.Equal(SupportVerdict.NotSupported, s76.Verdict);
    }

    [Fact]
    public void Parses_per_cpu_stat_and_cpuinfo()
    {
        var per = LinuxHardwareInfo.ParsePerCpu("cpu  10 0 10 80 0 0 0 0\ncpu0 5 0 5 40 0 0 0 0\ncpu1 5 0 5 40 0 0 0 0\nintr 1 2 3\n");
        Assert.Equal(2, per.Count);
        Assert.Equal(("cpu1", 40UL, 50UL), per[1]);
        var info = LinuxHardwareInfo.ParseCpuInfo("processor\t: 0\nmodel name\t: Intel(R) Core(TM) i9-9900K\nphysical id\t: 0\ncore id\t: 0\n\nprocessor\t: 1\nmodel name\t: Intel(R) Core(TM) i9-9900K\nphysical id\t: 0\ncore id\t: 0\n");
        Assert.Equal("Intel(R) Core(TM) i9-9900K", info.Name);
        Assert.Equal((1, 2), (info.Physical, info.Logical));
        Assert.Equal("NVIDIA Corporation TU106M [GeForce RTX 2070 Mobile]",
            LinuxHardwareInfo.ParseLspciName("01:00.0 VGA compatible controller: NVIDIA Corporation TU106M [GeForce RTX 2070 Mobile]"));
    }
}
