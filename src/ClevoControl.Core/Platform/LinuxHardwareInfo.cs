using System.Globalization;

namespace ClevoControl.Core.Platform;

/// <summary>CPU and GPU details from /proc and /sys for the sensor pages on Linux.</summary>
public static class LinuxHardwareInfo
{
    // ---- CPU ---------------------------------------------------------------------------

    /// <summary>Per-CPU (idle, total) jiffies from /proc/stat, in CPU order ("cpu0", "cpu1", …).</summary>
    public static List<(string Name, ulong Idle, ulong Total)> ParsePerCpu(string? stat)
    {
        var list = new List<(string, ulong, ulong)>();
        if (stat is null) return list;
        foreach (var line in stat.Split('\n'))
        {
            if (!line.StartsWith("cpu", StringComparison.Ordinal) || line.StartsWith("cpu ", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var f = parts.Skip(1).Select(x => ulong.TryParse(x, out var v) ? v : 0).ToArray();
            if (f.Length < 4) continue;
            var idle = f[3] + (f.Length > 4 ? f[4] : 0);
            var total = f.Take(Math.Min(f.Length, 8)).Aggregate(0UL, (a, b) => a + b);
            list.Add((parts[0], idle, total));
        }
        return list;
    }

    public sealed record CpuInfo(string Name, int Physical, int Logical, double BaseMhz);

    public static CpuInfo ParseCpuInfo(string? cpuinfo)
    {
        var name = "CPU";
        var logical = 0;
        var cores = new HashSet<string>();
        string phys = "0";
        foreach (var line in (cpuinfo ?? "").Split('\n'))
        {
            var kv = line.Split(':', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var value = kv[1].Trim();
            switch (key)
            {
                case "processor": logical++; break;
                case "model name": name = value; break;
                case "physical id": phys = value; break;
                case "core id": cores.Add(phys + ":" + value); break;
            }
        }
        return new CpuInfo(name, Math.Max(cores.Count, logical > 0 ? 1 : 0), logical, 0);
    }

    public static CpuInfo Cpu()
    {
        var info = ParseCpuInfo(LinuxSys.ReadText("proc/cpuinfo"));
        var khz = LinuxSys.ReadLong("sys/devices/system/cpu/cpu0/cpufreq/base_frequency")
                  ?? LinuxSys.ReadLong("sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");
        return info with { BaseMhz = khz is > 0 ? khz.Value / 1000.0 : 0 };
    }

    /// <summary>Average current clock of all CPUs in MHz (cpufreq), 0 if unknown.</summary>
    public static double CurrentMhz()
    {
        var dir = LinuxSys.P("sys/devices/system/cpu");
        if (!Directory.Exists(dir)) return 0;
        var values = new List<long>();
        foreach (var cpu in Directory.GetDirectories(dir, "cpu*"))
        {
            var f = Path.Combine(cpu, "cpufreq", "scaling_cur_freq");
            try { if (long.TryParse(File.ReadAllText(f).Trim(), out var khz)) values.Add(khz); } catch { }
        }
        return values.Count == 0 ? 0 : values.Average() / 1000.0;
    }

    // ---- GPUs --------------------------------------------------------------------------

    public sealed record Gpu(string Card, string Name, string Vendor, double? BusyPercent, double? VramUsedMb, double? VramTotalMb);

    private static string VendorName(string? id) => id switch
    {
        "0x10de" => "NVIDIA",
        "0x8086" => "Intel",
        "0x1002" => "AMD",
        _ => "GPU",
    };

    /// <summary>All DRM cards. Load is reported by amdgpu; NVIDIA details come from NVML instead.</summary>
    public static List<Gpu> Gpus()
    {
        var list = new List<Gpu>();
        var dir = LinuxSys.P("sys/class/drm");
        if (!Directory.Exists(dir)) return list;
        foreach (var card in Directory.GetDirectories(dir).Select(Path.GetFileName).OfType<string>()
                     .Where(n => n.StartsWith("card", StringComparison.Ordinal) && !n.Contains('-')).Order())
        {
            var dev = $"sys/class/drm/{card}/device/";
            var vendor = VendorName(LinuxSys.ReadText(dev + "vendor"));
            var slot = LinuxSys.ReadText(dev + "uevent")?.Split('\n').FirstOrDefault(l => l.StartsWith("PCI_SLOT_NAME=", StringComparison.Ordinal))?[14..];
            var name = slot is null ? null : ParseLspciName(Shell.Run("lspci", "-s", slot));
            double? Mb(string file) => LinuxSys.ReadLong(dev + file) is { } b ? b / 1024.0 / 1024 : null;
            list.Add(new Gpu(card, name ?? $"{vendor} ({card})", vendor,
                LinuxSys.ReadLong(dev + "gpu_busy_percent"), Mb("mem_info_vram_used"), Mb("mem_info_vram_total")));
        }
        return list;
    }

    /// <summary>"0000:01:00.0 VGA compatible controller: NVIDIA Corporation TU106M [GeForce RTX 2070 Mobile]" → part after the class.</summary>
    public static string? ParseLspciName(string? line) =>
        line?.Split(':', 3) is { Length: 3 } p ? p[2].Trim() : null;
}
