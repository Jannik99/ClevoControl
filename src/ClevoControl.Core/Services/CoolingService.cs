using System.Runtime.InteropServices;

namespace ClevoControl.Core.Services;

public enum CoolingMode
{
    /// <summary>Windows defaults: turbo boost on, 100 % maximum processor state.</summary>
    Normal,
    /// <summary>Turbo boost off – usually 10–20 °C cooler, ~10–25 % less peak performance.</summary>
    Cool,
    /// <summary>Turbo off and maximum processor state 80 %.</summary>
    VeryCool,
}

/// <summary>
/// Lowers CPU temperatures without any kernel driver: it edits two processor settings of the
/// active Windows power plan (the same ones "powercfg" changes). Applied to AC and battery.
/// </summary>
public static class CoolingService
{
    /// <summary>False on Linux systems whose CPU driver offers no turbo switch.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && Platform.LinuxPower.CanControlTurbo);

    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid MaxProcessorState = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);

    private static Guid ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) throw new InvalidOperationException("No active power plan.");
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    /// <summary>Reads the current state of the active plan (AC values). Boost: 0 off, 1 on, 2 aggressive …</summary>
    public static (uint BoostMode, int MaxStatePercent) Read()
    {
        if (OperatingSystem.IsLinux())
        {
            var (turbo, maxPct) = Platform.LinuxPower.ReadCpuLimits();
            return (turbo ? 2u : 0u, maxPct);
        }
        var scheme = ActiveScheme();
        var sub = SubProcessor;
        var boost = PerfBoostMode;
        var max = MaxProcessorState;
        PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref boost, out var b);
        PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref max, out var m);
        return (b, (int)m);
    }

    public static CoolingMode Detect()
    {
        try
        {
            var (boost, max) = Read();
            if (boost != 0 && max >= 100) return CoolingMode.Normal;
            return max < 100 ? CoolingMode.VeryCool : CoolingMode.Cool;
        }
        catch
        {
            return CoolingMode.Normal;
        }
    }

    /// <summary>Applies the mode. Returns false if Windows refused (e.g. plan managed by policy).</summary>
    public static bool Apply(CoolingMode mode, uint normalBoost = 2, uint normalMax = 100)
    {
        if (OperatingSystem.IsLinux())
        {
            // intel_pstate / cpufreq boost; needs the permissions from contrib/linux (docs/LINUX.md).
            return mode switch
            {
                CoolingMode.Cool => Platform.LinuxPower.ApplyCpuLimits(false, 100),
                CoolingMode.VeryCool => Platform.LinuxPower.ApplyCpuLimits(false, 80),
                _ => Platform.LinuxPower.ApplyCpuLimits(true, (int)Math.Max(normalMax, 50u)),
            };
        }
        if (!OperatingSystem.IsWindows()) return false;
        var (boost, max) = mode switch
        {
            CoolingMode.Cool => (0u, 100u),
            CoolingMode.VeryCool => (0u, 80u),
            _ => (normalBoost == 0 ? 2u : normalBoost, Math.Max(normalMax, 50u)), // restore what the user had
        };
        try
        {
            var scheme = ActiveScheme();
            var sub = SubProcessor;
            var b = PerfBoostMode;
            var m = MaxProcessorState;
            var ok = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref b, boost) == 0
                     & PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref b, boost) == 0
                     & PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref m, max) == 0
                     & PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref m, max) == 0;
            PowerSetActiveScheme(IntPtr.Zero, ref scheme); // re-apply so the change takes effect immediately
            return ok;
        }
        catch
        {
            return false;
        }
    }
}
