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
          dump                           raw EC buffers (for bug reports)

        Options: --simulate   use simulated hardware
        """);
    return 0;
}

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
