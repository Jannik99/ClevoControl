using ClevoControl.Core;
using ClevoControl.Core.Hardware;

// clevoctl – scriptable access to the Clevo EC. Note: while the ClevoControl app is running,
// its lighting engine may immediately override keyboard changes made here.

Console.OutputEncoding = System.Text.Encoding.UTF8;
var hw = HardwareFactory.Create(args.Contains("--simulate"));
args = args.Where(a => a != "--simulate").ToArray();

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        clevoctl – control Clevo laptops from the command line

          status                         sensors, keyboard type, profile
          color <#RRGGBB> [zone]         set keyboard color (zone 0-2 for 3-zone keyboards)
          brightness <0-100>             keyboard brightness
          kb on|off                      keyboard backlight power
          effect <name>                  hardware effect: breathe, cycle, wave, dance, flash, randomcolor, tempo
          fan auto|max                   fan mode
          fan duty <cpu%> [gpu%]         fixed fan duty (use with care; 'fan auto' to undo)
          profile quiet|powersaving|performance|entertainment
          cooling status|normal|cool|verycool   turbo / CPU limit (Windows power plan, Linux intel_pstate)
          charge status                  charge limit state (if the model supports it)
          charge <start> <stop> | charge off
          gpu [seconds]                  GPU load (NVIDIA via NVML; Windows also shows integrated GPUs)
          sysinfo                        model, BIOS, EC, drivers (for bug reports)
          dump                           raw EC buffers (for bug reports)
          detect                         does this laptop have the Clevo interface? (Linux: also driver status)
          daemon                         run lighting effects, fan curve, profiles and the local API
                                         from settings.json until stopped (used by the Linux service)

        Options: --simulate   use simulated hardware
        """);
    return 0;
}

if (args[0].Equals("daemon", StringComparison.OrdinalIgnoreCase)) return Daemon.Run(hw);

if (!hw.IsReal) Console.Error.WriteLine($"warning: {hw.Description}");

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "status":
        {
            var (cpu, gpu, gpu2) = hw.ReadFans();
            Console.WriteLine($"Keyboard : {hw.KeyboardType}");
            Console.WriteLine($"Profile  : {hw.ReadStoredPerformanceProfile()?.ToString() ?? "unknown"}");
            Console.WriteLine($"CPU      : {cpu.TemperatureC} °C, fan {cpu.Rpm} rpm ({cpu.DutyPercent} %)");
            Console.WriteLine($"GPU      : {gpu.TemperatureC} °C, fan {gpu.Rpm} rpm ({gpu.DutyPercent} %)");
            if (gpu2 is not null) Console.WriteLine($"GPU 2    : {gpu2.TemperatureC} °C, fan {gpu2.Rpm} rpm ({gpu2.DutyPercent} %)");
            break;
        }
        case "color":
        {
            var c = RgbColor.Parse(Arg(1));
            if (args.Length > 2) hw.SetKeyboardZoneColor(int.Parse(args[2]), c);
            else hw.SetKeyboardColor(c);
            break;
        }
        case "brightness":
            hw.SetKeyboardBrightness((byte)Math.Round(Math.Clamp(double.Parse(Arg(1)), 0, 100) * 2.55));
            break;
        case "kb":
            hw.SetKeyboardPower(Arg(1).Equals("on", StringComparison.OrdinalIgnoreCase));
            break;
        case "effect":
            hw.SetKeyboardHardwareEffect(Enum.Parse<HardwareEffect>(Arg(1), true));
            break;
        case "fan":
            if (Arg(1).Equals("duty", StringComparison.OrdinalIgnoreCase))
            {
                var cpuPct = Math.Clamp(int.Parse(Arg(2)), 30, 100); // never below 30 % from the CLI
                var gpuPct = args.Length > 3 ? Math.Clamp(int.Parse(args[3]), 30, 100) : cpuPct;
                byte B(int p) => (byte)Math.Round(p * 2.55);
                hw.SetFanDuty(B(cpuPct), B(gpuPct), B(gpuPct));
                Console.WriteLine("Fixed duty set. The fans stay there until 'clevoctl fan auto' or a reboot.");
            }
            else hw.SetFanMode(Enum.Parse<FanMode>(Arg(1), true));
            break;
        case "profile":
            hw.SetPerformanceProfile(Enum.Parse<PerformanceProfile>(Arg(1), true));
            break;
        case "cooling":
        {
            if (!ClevoControl.Core.Services.CoolingService.IsSupported) { Console.Error.WriteLine("Cooling modes are not available: the CPU driver offers no turbo switch (intel_pstate / cpufreq boost)."); return 1; }
            var arg = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
            if (arg != "status")
            {
                var mode = Enum.Parse<ClevoControl.Core.Services.CoolingMode>(arg, true);
                if (!ClevoControl.Core.Services.CoolingService.Apply(mode))
                {
                    Console.Error.WriteLine(OperatingSystem.IsWindows() ? "Windows refused the change." : "Not permitted – install the ClevoControl permissions (contrib/linux/install.sh, see docs/LINUX.md) or run with sudo.");
                    return 1;
                }
            }
            var (boost, max) = ClevoControl.Core.Services.CoolingService.Read();
            Console.WriteLine(OperatingSystem.IsWindows()
                ? $"Mode: {ClevoControl.Core.Services.CoolingService.Detect()}  (turbo boost mode {boost}, max processor state {max} %)"
                : $"Mode: {ClevoControl.Core.Services.CoolingService.Detect()}  (turbo {(boost != 0 ? "on" : "off")}, max performance {max} %)");
            break;
        }
        case "charge":
        {
            var info = hw.ReadChargeLimit();
            if (info is null) { Console.WriteLine("FlexiCharger is not supported via software on this model."); break; }
            if (args.Length > 1 && args[1].Equals("off", StringComparison.OrdinalIgnoreCase)) hw.SetChargeLimit(false, info.Start, info.Stop);
            else if (args.Length > 2) hw.SetChargeLimit(true, int.Parse(args[1]), int.Parse(args[2]));
            info = hw.ReadChargeLimit()!;
            Console.WriteLine($"Enabled: {info.Enabled}, start {info.Start} %, stop {info.Stop} %");
            Console.WriteLine($"Allowed start: {string.Join(", ", info.StartOptions)} / stop: {string.Join(", ", info.StopOptions)}");
            break;
        }
        case "gpu":
        {
            using var nv = new ClevoControl.Core.Monitoring.GpuMonitor();
            var eng = OperatingSystem.IsWindows() ? new ClevoControl.Core.Monitoring.GpuEngineMonitor() : null;
            var n = args.Length > 1 ? int.Parse(args[1]) : 1;
            for (var i = 0; i < n; i++)
            {
                Thread.Sleep(1000);
                if (nv.Sample() is { } s) Console.WriteLine($"{nv.Name}: load {s.LoadPercent:0} %, {s.PowerW:0} W, {s.CoreClockMhz:0} MHz, VRAM {s.VramUsedMb:0}/{s.VramTotalMb:0} MB, P{s.PState}");
                else if (i == 0) Console.WriteLine("No NVIDIA GPU found (NVML not available).");
                if (eng is null) continue;
                foreach (var a in eng.Sample()) Console.WriteLine($"  {a.Name}: engines max {a.LoadPercent:0} %  ({string.Join(", ", a.EngineLoad.Where(e => e.Value >= 1).Select(e => $"{e.Key} {e.Value:0} %"))})");
            }
            break;
        }
        case "sysinfo":
            Console.Write(ClevoControl.Core.Services.SystemInfoService.AsText(ClevoControl.Core.Services.SystemInfoService.Collect(hw)));
            break;
        case "detect":
        {
            var r = ClevoControl.Core.Platform.SupportCheck.Run(hw);
            foreach (var f in r.Findings) Console.WriteLine($"  {(f.Ok ? "ok " : " - ")} {f.Text}");
            Console.WriteLine();
            Console.WriteLine($"{r.Verdict}: {r.Summary}");
            return r.Verdict == ClevoControl.Core.Platform.SupportVerdict.NotSupported ? 3 : 0;
        }
        case "dump":
            Console.WriteLine("spec (0x0D): " + Convert.ToHexString(hw.ReadSpecBuffer().AsSpan(0, 64)));
            Console.WriteLine("fan  (0x0C): " + Convert.ToHexString(hw.ReadFanBuffer().AsSpan(0, 64)));
            break;
        default:
            Console.Error.WriteLine($"unknown command '{args[0]}' – see 'clevoctl help'");
            return 2;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("error: " + ex.Message);
    return 1;
}

string Arg(int i) => args.Length > i ? args[i] : throw new ArgumentException("missing argument – see 'clevoctl help'");
