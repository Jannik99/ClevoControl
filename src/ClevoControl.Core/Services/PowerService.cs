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

    public static bool IsOnAc => Platform.SystemStatus.Power().OnAc;

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

    /// <summary>True if the OS power mode can be switched (Windows; Linux with power-profiles-daemon).</summary>
    public static bool HasSystemPowerMode => OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && Platform.LinuxPower.HasPowerProfiles);

    /// <summary>Windows power mode overlay, on Linux the power-profiles-daemon profile.</summary>
    public static void SetWindowsPowerMode(WindowsPowerMode mode)
    {
        if (OperatingSystem.IsLinux())
        {
            Platform.LinuxPower.SetPowerProfile(mode switch
            {
                WindowsPowerMode.BestEfficiency => "power-saver",
                WindowsPowerMode.BestPerformance => "performance",
                _ => "balanced",
            });
            return;
        }
        if (!OperatingSystem.IsWindows()) return;
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
        if (OperatingSystem.IsLinux())
        {
            return Platform.LinuxPower.GetPowerProfile() switch
            {
                "power-saver" => WindowsPowerMode.BestEfficiency,
                "performance" => WindowsPowerMode.BestPerformance,
                "balanced" => WindowsPowerMode.Balanced,
                _ => null,
            };
        }
        if (!OperatingSystem.IsWindows()) return null;
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

    /// <summary>Where <see cref="CheckPowerSource"/> reads the power source from (replaced by tests).</summary>
    internal Func<bool> OnAcSource { get; set; } = () => IsOnAc;

    /// <summary>
    /// Called periodically and on power events; applies AC/battery rules when the source changes
    /// (<paramref name="force"/>: at start and when the rules themselves changed). A profile set by the
    /// auto-switch is written to <see cref="PowerSettings.Profile"/> so the UI and fans per profile agree with the EC.
    /// </summary>
    public void CheckPowerSource(PowerSettings settings, bool force = false)
    {
        lock (_gate)
        {
            var onAc = OnAcSource();
            if (!force && _lastOnAc == onAc) return;
            _lastOnAc = onAc;
            if (force) _rules = RulesOf(settings);

            if (settings.AutoSwitch)
            {
                var profile = onAc ? settings.ProfileOnAc : settings.ProfileOnBattery;
                settings.Profile = profile;
                SetProfile(profile, settings.SyncWindowsPowerMode);
            }

            if (settings.LowRefreshOnBattery)
            {
                if (onAc) DisplayService.SetRefreshRate(DisplayService.GetMaxRefreshRate());
                else DisplayService.SetRefreshRate(settings.BatteryRefreshRate);
            }
        }
    }

    private readonly object _gate = new();
    private (bool, PerformanceProfile, PerformanceProfile, bool, int)? _rules;

    private static (bool, PerformanceProfile, PerformanceProfile, bool, int) RulesOf(PowerSettings s) =>
        (s.AutoSwitch, s.ProfileOnAc, s.ProfileOnBattery, s.LowRefreshOnBattery, s.BatteryRefreshRate);

    /// <summary>
    /// Applies <paramref name="settings"/> after they were changed (page, tray, scene, automation). An explicit
    /// profile choice is kept until the power source actually changes; the AC/battery rules are applied right away
    /// only when the rules themselves changed (auto-switch switched on, other profile for AC/battery …).
    /// </summary>
    public void Apply(PowerSettings settings)
    {
        lock (_gate)
        {
            var rulesChanged = _rules != RulesOf(settings);
            if (rulesChanged && settings.AutoSwitch)
            {
                CheckPowerSource(settings, force: true);
                return;
            }
            SetProfile(settings.Profile, settings.SyncWindowsPowerMode);
            if (rulesChanged) CheckPowerSource(settings, force: true);
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
        if (OperatingSystem.IsLinux()) return (int)Math.Round(Platform.LinuxPower.Panel()?.Current ?? 0);
        if (!OperatingSystem.IsWindows()) return 0;
        var dm = New();
        return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 0;
    }

    /// <summary>Refresh rates available at the current resolution.</summary>
    public static List<int> GetRefreshRates()
    {
        if (OperatingSystem.IsLinux()) return Platform.LinuxPower.Panel()?.Rates.Select(r => (int)Math.Round(r)).Distinct().Order().ToList() ?? [];
        if (!OperatingSystem.IsWindows()) return [];
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
        if (OperatingSystem.IsLinux()) return Platform.LinuxPower.SetRefreshRate(hz);
        if (!OperatingSystem.IsWindows()) return false;
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
        if (OperatingSystem.IsLinux()) return Platform.LinuxPower.GetBrightness();
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
        if (OperatingSystem.IsLinux()) { Platform.LinuxPower.SetBrightness(percent); return; }
        try
        {
            using var s = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (System.Management.ManagementObject o in s.Get())
                o.InvokeMethod("WmiSetBrightness", [1u, (byte)Math.Clamp(percent, 0, 100)]);
        }
        catch { }
    }
}
