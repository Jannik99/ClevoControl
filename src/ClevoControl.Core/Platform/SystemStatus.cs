using ClevoControl.Core.Native;

namespace ClevoControl.Core.Platform;

/// <summary>Cross-platform power source, CPU time, memory and idle time.</summary>
public static class SystemStatus
{
    /// <summary>AC connected, battery percent (0..100), battery present.</summary>
    public static (bool OnAc, int Percent, bool HasBattery) Power()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Win32.GetSystemPowerStatus(out var s)) return (true, 100, false);
            var present = s.BatteryFlag != 128 && s.BatteryFlag != 255;
            return (s.ACLineStatus == 1, s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : 100, present);
        }
        if (OperatingSystem.IsLinux())
        {
            var pct = LinuxSys.BatteryPercent();
            return (LinuxSys.OnAc(), pct ?? 100, pct is not null);
        }
        return (true, 100, false);
    }

    /// <summary>Cumulative (idle, total) CPU time in arbitrary but consistent units.</summary>
    public static (ulong Idle, ulong Total)? CpuTimes()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!Win32.GetSystemTimes(out var idle, out var kernel, out var user)) return null;
            return (idle.Value, kernel.Value + user.Value); // kernel includes idle
        }
        if (OperatingSystem.IsLinux() && LinuxSys.ReadText("proc/stat") is { } stat) return LinuxSys.ParseProcStat(stat);
        return null;
    }

    public static (double Percent, double TotalGb) Memory()
    {
        if (OperatingSystem.IsWindows())
        {
            var m = new Win32.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
            return Win32.GlobalMemoryStatusEx(ref m) ? (m.dwMemoryLoad, m.ullTotalPhys / 1024d / 1024 / 1024) : (0, 0);
        }
        if (OperatingSystem.IsLinux() && LinuxSys.ReadText("proc/meminfo") is { } mem) return LinuxSys.ParseMemInfo(mem);
        return (0, 0);
    }

    /// <summary>Time since the last keyboard/mouse input. Zero where unknown.</summary>
    public static TimeSpan IdleTime()
    {
        if (OperatingSystem.IsLinux()) return LinuxPower.IdleTime();
        if (!OperatingSystem.IsWindows()) return TimeSpan.Zero;
        var info = new Win32.LASTINPUTINFO { cbSize = 8 };
        if (!Win32.GetLastInputInfo(ref info)) return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }
}
