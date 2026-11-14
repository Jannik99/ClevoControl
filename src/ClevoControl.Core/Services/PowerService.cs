using System.Runtime.InteropServices;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Native;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Services;

/// <summary>Clevo performance profiles, Windows power mode, AC/battery automation, refresh rate.</summary>
public sealed class PowerService
{
    private static readonly Guid OverlayEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid OverlayBalanced = Guid.Empty;
    private static readonly Guid OverlayPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    private readonly IClevoHardware _hw;
    private bool? _lastOnAc;

    public PowerService(IClevoHardware hw) => _hw = hw;

    public PerformanceProfile? ActiveProfile { get; private set; }

    public static bool IsOnAc => Win32.GetSystemPowerStatus(out var s) && s.ACLineStatus == 1;

    /// <summary>Raised after a profile was sent to the EC.</summary>
    public event Action<PerformanceProfile>? ProfileApplied;

    public void SetProfile(PerformanceProfile profile, bool syncWindows)
    {
        _hw.SetPerformanceProfile(profile);
        ActiveProfile = profile;
        Log.Info($"Performance profile -> {profile}" + (syncWindows ? " (+ Windows power mode)" : ""));
        ProfileApplied?.Invoke(profile);
        if (syncWindows)
        {
            SetWindowsPowerMode(profile switch
            {
                PerformanceProfile.Performance => WindowsPowerMode.BestPerformance,
                PerformanceProfile.PowerSaving => WindowsPowerMode.BestEfficiency,
                PerformanceProfile.Quiet => WindowsPowerMode.BestEfficiency,
                _ => WindowsPowerMode.Balanced,
            });
        }
    }

    public static void SetWindowsPowerMode(WindowsPowerMode mode)
    {
        var g = mode switch
        {
            WindowsPowerMode.BestEfficiency => OverlayEfficiency,
            WindowsPowerMode.BestPerformance => OverlayPerformance,
            _ => OverlayBalanced,
        };
        try { Win32.PowerSetActiveOverlayScheme(g); } catch { /* not available on this Windows build */ }
    }

    public static WindowsPowerMode? GetWindowsPowerMode()
    {
        try
        {
            if (Win32.PowerGetEffectiveOverlayScheme(out var g) != 0) return null;
            if (g == OverlayEfficiency) return WindowsPowerMode.BestEfficiency;
            if (g == OverlayPerformance) return WindowsPowerMode.BestPerformance;
            return WindowsPowerMode.Balanced;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Called periodically and on power events; applies AC/battery rules when the source changes.</summary>
    public void CheckPowerSource(PowerSettings settings, bool force = false)
    {
        var onAc = IsOnAc;
        if (!force && _lastOnAc == onAc) return;
        _lastOnAc = onAc;

        if (settings.AutoSwitch)
            SetProfile(onAc ? settings.ProfileOnAc : settings.ProfileOnBattery, settings.SyncWindowsPowerMode);

        if (settings.LowRefreshOnBattery)
        {
            if (onAc) DisplayService.SetRefreshRate(DisplayService.GetMaxRefreshRate());
            else DisplayService.SetRefreshRate(settings.BatteryRefreshRate);
        }
    }
}

/// <summary>Primary display refresh rate and brightness.</summary>
public static class DisplayService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    private const int ENUM_CURRENT_SETTINGS = -1, DM_DISPLAYFREQUENCY = 0x400000, CDS_UPDATEREGISTRY = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettings(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettings(ref DEVMODE dm, int flags);

    private static DEVMODE New() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    public static int GetRefreshRate()
    {
        var dm = New();
        return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 0;
    }

    /// <summary>Refresh rates available at the current resolution.</summary>
    public static List<int> GetRefreshRates()
    {
        var cur = New();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref cur)) return [];
        var set = new SortedSet<int>();
        var dm = New();
        for (var i = 0; EnumDisplaySettings(null, i, ref dm); i++)
        {
            if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && dm.dmBitsPerPel == cur.dmBitsPerPel)
                set.Add(dm.dmDisplayFrequency);
        }
        return set.ToList();
    }

    public static int GetMaxRefreshRate() => GetRefreshRates().DefaultIfEmpty(60).Max();

    public static bool SetRefreshRate(int hz)
    {
        var rates = GetRefreshRates();
        if (rates.Count == 0) return false;
        var best = rates.OrderBy(r => Math.Abs(r - hz)).First();
        var dm = New();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) || dm.dmDisplayFrequency == best) return true;
        dm.dmDisplayFrequency = best;
        dm.dmFields = DM_DISPLAYFREQUENCY;
        return ChangeDisplaySettings(ref dm, CDS_UPDATEREGISTRY) == 0;
    }

    /// <summary>Internal panel brightness (0..100) via WMI, null if not supported.</summary>
    public static int? GetBrightness()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (System.Management.ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return null;
    }

    public static void SetBrightness(int percent)
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (System.Management.ManagementObject o in s.Get())
                o.InvokeMethod("WmiSetBrightness", [1u, (byte)Math.Clamp(percent, 0, 100)]);
        }
        catch { }
    }
}
