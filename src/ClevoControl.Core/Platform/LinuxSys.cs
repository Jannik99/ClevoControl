using System.Globalization;

namespace ClevoControl.Core.Platform;

/// <summary>
/// Readers for Linux /proc and /sys files. The parsing functions take file contents (not paths)
/// so they can be unit-tested on any OS; <see cref="Root"/> can be pointed at a fake tree.
/// </summary>
public static class LinuxSys
{
    /// <summary>File system root, "/" in production. Tests point it at a temporary folder.</summary>
    public static string Root { get; set; } = "/";

    public static string P(string path) => Path.Combine(Root, path.TrimStart('/'));

    public static string? ReadText(string path)
    {
        try { return File.ReadAllText(P(path)).Trim(); }
        catch { return null; }
    }

    public static long? ReadLong(string path) =>
        long.TryParse(ReadText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    // ---- CPU / memory -------------------------------------------------------------------

    /// <summary>Parses the aggregate "cpu" line of /proc/stat into (idle, total) jiffies.</summary>
    public static (ulong Idle, ulong Total)? ParseProcStat(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            if (!line.StartsWith("cpu ", StringComparison.Ordinal)) continue;
            var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                .Select(x => ulong.TryParse(x, out var v) ? v : 0).ToArray();
            if (f.Length < 4) return null;
            // user nice system idle iowait irq softirq steal (guest values are already in user/nice)
            var idle = f[3] + (f.Length > 4 ? f[4] : 0);
            var total = f.Take(Math.Min(f.Length, 8)).Aggregate(0UL, (a, b) => a + b);
            return (idle, total);
        }
        return null;
    }

    /// <summary>Parses /proc/meminfo into (used percent, total GB).</summary>
    public static (double Percent, double TotalGb) ParseMemInfo(string content)
    {
        long total = 0, available = -1;
        foreach (var line in content.Split('\n'))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2) continue;
            var kb = long.TryParse(parts[1].Trim().Split(' ')[0], out var v) ? v : 0;
            if (parts[0] == "MemTotal") total = kb;
            else if (parts[0] == "MemAvailable") available = kb;
        }
        if (total <= 0 || available < 0) return (0, 0);
        return (Math.Round(100.0 * (total - available) / total), total / 1024d / 1024);
    }

    // ---- Power supply -------------------------------------------------------------------

    private static IEnumerable<string> PowerSupplies()
    {
        var dir = P("sys/class/power_supply");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetDirectories(dir).Order();
    }

    private static string? Attr(string supplyDir, string name)
    {
        try { return File.ReadAllText(Path.Combine(supplyDir, name)).Trim(); }
        catch { return null; }
    }

    private static int? AttrInt(string supplyDir, string name) =>
        long.TryParse(Attr(supplyDir, name), out var v) ? (int)Math.Clamp(v, int.MinValue, int.MaxValue) : null;

    /// <summary>First system battery (type "Battery", scope not "Device"), or null.</summary>
    public static string? BatteryDir() => PowerSupplies().FirstOrDefault(d =>
        Attr(d, "type") == "Battery" && Attr(d, "scope") is null or "System");

    /// <summary>True if any mains adapter reports online; true as well when no adapter is listed (desktop).</summary>
    public static bool OnAc()
    {
        var mains = PowerSupplies().Where(d => Attr(d, "type") == "Mains").ToList();
        if (mains.Count == 0) return BatteryDir() is null || Attr(BatteryDir()!, "status") is not "Discharging";
        return mains.Any(d => Attr(d, "online") == "1");
    }

    /// <summary>Charge in percent, or null when there is no battery.</summary>
    public static int? BatteryPercent() =>
        BatteryDir() is { } b ? Math.Clamp(AttrInt(b, "capacity") ?? 0, 0, 100) : null;

    public static Services.BatteryInfo ReadBattery()
    {
        var onAc = OnAc();
        if (BatteryDir() is not { } b)
            return new Services.BatteryInfo(false, 0, onAc, false, null, null, null, null, null, null, null, null, null);

        var status = Attr(b, "status");
        var percent = Math.Clamp(AttrInt(b, "capacity") ?? 0, 0, 100);
        var voltageUv = AttrInt(b, "voltage_now");
        var voltage = voltageUv is > 0 ? voltageUv / 1_000_000.0 : (double?)null;

        // Batteries report either energy (µWh / µW) or charge (µAh / µA); charge is converted with the design voltage.
        int? design, full, now, powerMw;
        if (AttrInt(b, "energy_full_design") is { } efd)
        {
            design = efd / 1000;
            full = AttrInt(b, "energy_full") / 1000;
            now = AttrInt(b, "energy_now") / 1000;
            powerMw = AttrInt(b, "power_now") / 1000;
        }
        else
        {
            var vDesign = (AttrInt(b, "voltage_min_design") ?? voltageUv ?? 0) / 1_000_000.0;
            int? ToMwh(int? uah) => uah is null || vDesign <= 0 ? null : (int)Math.Round(uah.Value / 1000.0 * vDesign);
            design = ToMwh(AttrInt(b, "charge_full_design"));
            full = ToMwh(AttrInt(b, "charge_full"));
            now = ToMwh(AttrInt(b, "charge_now"));
            powerMw = ToMwh(AttrInt(b, "current_now"));
        }
        var charging = status == "Charging";
        int? rate = powerMw is null ? null : charging ? powerMw : -powerMw;
        TimeSpan? remaining = !charging && powerMw is > 0 && now is > 0 ? TimeSpan.FromHours(now.Value / (double)powerMw.Value) : null;
        var cycles = AttrInt(b, "cycle_count");

        return new Services.BatteryInfo(true, percent, onAc, charging, remaining, design, full, now,
            cycles is > 0 ? cycles : null, voltage, rate, Attr(b, "manufacturer"), Attr(b, "technology"));
    }

    // ---- DMI ------------------------------------------------------------------------------

    public static string? Dmi(string name) => ReadText("sys/class/dmi/id/" + name) is { Length: > 0 } s ? s : null;
}
