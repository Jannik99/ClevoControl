using System.Globalization;
using System.Text.RegularExpressions;

namespace ClevoControl.Core.Platform;

/// <summary>
/// Linux counterparts of the Windows power plan features:
/// CPU turbo / limit (intel_pstate or cpufreq boost), power-profiles-daemon, panel brightness,
/// refresh rate (X11), idle time.
/// </summary>
public static partial class LinuxPower
{
    // ---- CPU turbo and limit ("cooling modes") ------------------------------------------

    private const string Pstate = "sys/devices/system/cpu/intel_pstate";
    private const string CpufreqBoost = "sys/devices/system/cpu/cpufreq/boost";

    /// <summary>True when the CPU driver lets us switch turbo (Intel intel_pstate or the generic cpufreq boost switch).</summary>
    public static bool CanControlTurbo => File.Exists(LinuxSys.P(Pstate + "/no_turbo")) || File.Exists(LinuxSys.P(CpufreqBoost));

    /// <summary>Only intel_pstate offers a simple percentage limit.</summary>
    public static bool CanLimitCpu => File.Exists(LinuxSys.P(Pstate + "/max_perf_pct"));

    /// <summary>(turbo on, max performance percent).</summary>
    public static (bool TurboOn, int MaxPercent) ReadCpuLimits()
    {
        bool turbo = true;
        if (LinuxSys.ReadLong(Pstate + "/no_turbo") is { } noTurbo) turbo = noTurbo == 0;
        else if (LinuxSys.ReadLong(CpufreqBoost) is { } boost) turbo = boost != 0;
        var max = (int)(LinuxSys.ReadLong(Pstate + "/max_perf_pct") ?? 100);
        return (turbo, max);
    }

    /// <summary>
    /// Writes turbo and limit. Returns false if not permitted (install contrib/linux rules) or not supported –
    /// also when a limit below 100 % was asked for but the CPU driver has no max_perf_pct (AMD, acpi-cpufreq):
    /// the turbo is still switched, but the limit itself cannot be applied.
    /// </summary>
    public static bool ApplyCpuLimits(bool turboOn, int maxPercent)
    {
        var ok = true;
        if (File.Exists(LinuxSys.P(Pstate + "/no_turbo"))) ok &= TryWrite(Pstate + "/no_turbo", turboOn ? "0" : "1");
        else if (File.Exists(LinuxSys.P(CpufreqBoost))) ok &= TryWrite(CpufreqBoost, turboOn ? "1" : "0");
        else ok = false;
        if (CanLimitCpu) ok &= TryWrite(Pstate + "/max_perf_pct", Math.Clamp(maxPercent, 10, 100).ToString(CultureInfo.InvariantCulture));
        else if (maxPercent < 100)
        {
            Log.Warn($"CPU limit {maxPercent} % not applied: the CPU driver offers no max_perf_pct (intel_pstate only)");
            ok = false;
        }
        return ok;
    }

    private static bool TryWrite(string path, string value)
    {
        try
        {
            File.WriteAllText(LinuxSys.P(path), value);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Cannot write /{path}: {ex.Message} (see docs/LINUX.md, permissions)");
            return false;
        }
    }

    // ---- power-profiles-daemon (the Linux "power mode") --------------------------------

    public static bool HasPowerProfiles => Shell.Has("powerprofilesctl");

    /// <summary>"power-saver", "balanced" or "performance".</summary>
    public static string? GetPowerProfile() => Shell.Run("powerprofilesctl", "get")?.Trim();

    public static bool SetPowerProfile(string profile) => Shell.Run("powerprofilesctl", "set", profile) is not null;

    // ---- panel brightness ----------------------------------------------------------------

    private static string? Backlight()
    {
        var dir = LinuxSys.P("sys/class/backlight");
        if (!Directory.Exists(dir)) return null;
        // Prefer the GPU's own interface over the generic ACPI one.
        return Directory.GetDirectories(dir).OrderBy(d => Path.GetFileName(d).StartsWith("acpi", StringComparison.Ordinal) ? 1 : 0).FirstOrDefault();
    }

    public static int? GetBrightness()
    {
        if (Backlight() is not { } b) return null;
        var max = ReadLong(Path.Combine(b, "max_brightness"));
        var cur = ReadLong(Path.Combine(b, "brightness"));
        return max is > 0 && cur is not null ? (int)Math.Round(100.0 * cur.Value / max.Value) : null;
    }

    /// <summary>Uses systemd-logind (works for the active session without root), then the sysfs file.</summary>
    public static void SetBrightness(int percent)
    {
        if (Backlight() is not { } b || ReadLong(Path.Combine(b, "max_brightness")) is not { } max || max <= 0) return;
        var value = Math.Max(1, (long)Math.Round(max * Math.Clamp(percent, 1, 100) / 100.0)).ToString(CultureInfo.InvariantCulture);
        var name = Path.GetFileName(b);
        if (Shell.Run("busctl", "call", "org.freedesktop.login1", "/org/freedesktop/login1/session/auto",
                "org.freedesktop.login1.Session", "SetBrightness", "ssu", "backlight", name, value) is not null) return;
        try { File.WriteAllText(Path.Combine(b, "brightness"), value); } catch { }
    }

    private static long? ReadLong(string fullPath)
    {
        try { return long.TryParse(File.ReadAllText(fullPath).Trim(), out var v) ? v : null; }
        catch { return null; }
    }

    // ---- refresh rate (X11 via xrandr) -----------------------------------------------------

    public sealed record XrandrOutput(string Name, bool Primary, int Width, int Height, double Current, List<double> Rates);

    /// <summary>Connected outputs with the refresh rates of their current resolution.</summary>
    public static List<XrandrOutput> ParseXrandr(string? output)
    {
        var list = new List<XrandrOutput>();
        if (output is null) return list;
        XrandrOutput? current = null;
        foreach (var line in output.Split('\n'))
        {
            var head = OutputRegex().Match(line);
            if (head.Success)
            {
                current = new XrandrOutput(head.Groups[1].Value, line.Contains(" primary "), 0, 0, 0, []);
                list.Add(current);
                continue;
            }
            if (current is null || !line.StartsWith("   ", StringComparison.Ordinal)) { if (!line.StartsWith(' ')) current = null; continue; }
            var mode = ModeRegex().Match(line);
            if (!mode.Success || !line.Contains('*')) continue; // only the active resolution
            var rates = RateRegex().Matches(line[mode.Length..]).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            var active = RateRegex().Matches(line[mode.Length..]).FirstOrDefault(m => m.Groups[2].Value.Contains('*'));
            list[^1] = current = current with
            {
                Width = int.Parse(mode.Groups[1].Value, CultureInfo.InvariantCulture),
                Height = int.Parse(mode.Groups[2].Value, CultureInfo.InvariantCulture),
                Rates = rates,
                Current = active is null ? 0 : double.Parse(active.Groups[1].Value, CultureInfo.InvariantCulture),
            };
        }
        return list.Where(o => o.Width > 0).ToList();
    }

    /// <summary>The built-in panel (eDP/LVDS) or the primary output; X11 sessions only.</summary>
    public static XrandrOutput? Panel()
    {
        if (!Shell.HasX11 || Shell.IsWayland) return null;
        var outputs = ParseXrandr(Shell.Run("xrandr", "--query"));
        return outputs.FirstOrDefault(o => o.Name.StartsWith("eDP", StringComparison.Ordinal) || o.Name.StartsWith("LVDS", StringComparison.Ordinal))
               ?? outputs.FirstOrDefault(o => o.Primary) ?? outputs.FirstOrDefault();
    }

    public static bool SetRefreshRate(int hz)
    {
        if (Panel() is not { } p || p.Rates.Count == 0) return false;
        var best = p.Rates.OrderBy(r => Math.Abs(r - hz)).First();
        if (Math.Abs(best - p.Current) < 0.5) return true;
        return Shell.Run("xrandr", "--output", p.Name, "--rate", best.ToString("0.00", CultureInfo.InvariantCulture)) is not null;
    }

    [GeneratedRegex(@"^(\S+) connected")]
    private static partial Regex OutputRegex();

    [GeneratedRegex(@"^\s+(\d+)x(\d+)\S*")]
    private static partial Regex ModeRegex();

    [GeneratedRegex(@"(\d+\.\d+)([*+ ]*)")]
    private static partial Regex RateRegex();

    // ---- idle time -------------------------------------------------------------------------

    private static TimeSpan _idle;
    private static DateTime _idleRead = DateTime.MinValue;

    /// <summary>
    /// Time since the last input. GNOME (Mutter, also on Wayland), KDE (freedesktop ScreenSaver),
    /// X11 (xprintidle). Cached for 2 s because the lighting loop asks often. Zero if unknown.
    /// </summary>
    public static TimeSpan IdleTime()
    {
        if (DateTime.UtcNow - _idleRead < TimeSpan.FromSeconds(2)) return _idle;
        _idleRead = DateTime.UtcNow;
        _idle = ReadIdle() ?? TimeSpan.Zero;
        return _idle;
    }

    private static TimeSpan? ReadIdle()
    {
        var mutter = Shell.Run("gdbus", "call", "--session", "--dest", "org.gnome.Mutter.IdleMonitor",
            "--object-path", "/org/gnome/Mutter/IdleMonitor/Core", "--method", "org.gnome.Mutter.IdleMonitor.GetIdletime");
        if (ParseGdbusNumber(mutter) is { } ms) return TimeSpan.FromMilliseconds(ms);
        if (Shell.HasX11 && Shell.Run("xprintidle") is { } x && long.TryParse(x.Trim(), out var xms)) return TimeSpan.FromMilliseconds(xms);
        return null;
    }

    /// <summary>"(uint64 12345,)" → 12345.</summary>
    public static long? ParseGdbusNumber(string? output)
    {
        if (output is null) return null;
        var m = NumberRegex().Match(output);
        return m.Success ? long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"\(\w*\s*(\d+),?\)")]
    private static partial Regex NumberRegex();
}
