using System.Diagnostics;
using System.Management;
using System.Text;
using ClevoControl.Core.Hardware;
using Microsoft.Win32;

namespace ClevoControl.Core.Services;

public sealed record InfoItem(string Key, string Value);

public sealed record ClevoComponent(string Name, string Description, string? ProcessName, string? ServiceName, bool Running, string? Path);

/// <summary>Collects everything people ask for in support threads (model, BIOS, EC, drivers …).</summary>
public static class SystemInfoService
{
    public static List<InfoItem> Collect(IClevoHardware hw)
    {
        var list = OperatingSystem.IsWindows() ? CollectWindows() : CollectLinux();
        list.Add(new("Keyboard type", hw.KeyboardType.ToString()));
        list.Add(new("Driver", hw.Description));
        var support = Platform.SupportCheck.Run(hw);
        list.Add(new("Support check", $"{support.Verdict}: {support.Summary}"));
        list.Add(new("ClevoControl", typeof(SystemInfoService).Assembly.GetName().Version?.ToString(3) ?? "?"));
        try
        {
            list.Add(new("EC spec 0x0D", Convert.ToHexString(hw.ReadSpecBuffer().AsSpan(0, 48))));
            list.Add(new("EC fan 0x0C", Convert.ToHexString(hw.ReadFanBuffer().AsSpan(0, 32))));
        }
        catch { }
        return list;
    }

    private static List<InfoItem> CollectLinux()
    {
        var list = new List<InfoItem>();
        static string D(string name) => Platform.LinuxSys.Dmi(name) ?? "";
        list.Add(new("Model", D("product_name")));
        list.Add(new("System manufacturer", D("sys_vendor")));
        list.Add(new("Mainboard", $"{D("board_vendor")} {D("board_name")}".Trim()));
        list.Add(new("BIOS", $"{D("bios_version")} ({D("bios_vendor")})"));
        if (D("bios_date") is { Length: > 0 } date) list.Add(new("BIOS date", date));
        if (D("ec_firmware_release") is { Length: > 0 } ec) list.Add(new("EC firmware", ec));
        if (Platform.LinuxSys.ReadText("proc/cpuinfo") is { } cpu &&
            cpu.Split('\n').FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal)) is { } cpuLine)
            list.Add(new("CPU", cpuLine.Split(':', 2)[1].Trim()));
        var (_, ramGb) = Platform.SystemStatus.Memory();
        if (ramGb > 0) list.Add(new("RAM", $"{ramGb:0.0} GB"));
        var os = Platform.LinuxSys.ReadText("etc/os-release")?.Split('\n')
            .FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))?[12..].Trim('"');
        list.Add(new("Linux", $"{os ?? "?"}, kernel {Platform.LinuxSys.ReadText("proc/sys/kernel/osrelease") ?? "?"}"));
        list.Add(new("tuxedo-drivers", Platform.LinuxSys.ReadText("sys/module/tuxedo_io/version")
            ?? (Directory.Exists(Platform.LinuxSys.P("sys/module/tuxedo_io")) ? "loaded" : "not loaded")));
        foreach (var m in new[] { "clevo_acpi", "clevo_wmi", "tuxedo_keyboard" })
            if (Directory.Exists(Platform.LinuxSys.P("sys/module/" + m))) list.Add(new("Module", m));
        return list;
    }

    private static List<InfoItem> CollectWindows()
    {
        var list = new List<InfoItem>();
        void Wmi(string wql, Action<ManagementBaseObject> f)
        {
            try
            {
                using var s = new ManagementObjectSearcher(wql);
                foreach (var o in s.Get()) f(o);
            }
            catch { }
        }
        static string S(object? o) => o?.ToString()?.Trim() ?? "";

        Wmi("SELECT Manufacturer, Model FROM Win32_ComputerSystem", o =>
        {
            list.Add(new("Model", S(o["Model"])));
            list.Add(new("System manufacturer", S(o["Manufacturer"])));
        });
        Wmi("SELECT Product, Manufacturer FROM Win32_BaseBoard", o => list.Add(new("Mainboard", $"{S(o["Manufacturer"])} {S(o["Product"])}".Trim())));
        Wmi("SELECT * FROM Win32_BIOS", o =>
        {
            list.Add(new("BIOS", $"{S(o["SMBIOSBIOSVersion"])} ({S(o["Manufacturer"])})"));
            var date = S(o["ReleaseDate"]);
            if (date.Length >= 8) list.Add(new("BIOS date", $"{date[..4]}-{date[4..6]}-{date[6..8]}"));
            var ecMajor = o["EmbeddedControllerMajorVersion"];
            var ecMinor = o["EmbeddedControllerMinorVersion"];
            if (ecMajor is not null && Convert.ToInt32(ecMajor) != 255)
                list.Add(new("EC firmware", $"{ecMajor}.{Convert.ToInt32(ecMinor):D2}"));
        });
        Wmi("SELECT Name FROM Win32_Processor", o => list.Add(new("CPU", S(o["Name"]))));
        Wmi("SELECT Name, DriverVersion FROM Win32_VideoController", o => list.Add(new("GPU", $"{S(o["Name"])} (driver {S(o["DriverVersion"])})")));
        Wmi("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", o => list.Add(new("RAM", $"{Convert.ToDouble(o["TotalPhysicalMemory"]) / 1024 / 1024 / 1024:0.0} GB")));
        Wmi("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem", o => list.Add(new("Windows", $"{S(o["Caption"])} {S(o["Version"])}")));
        return list;
    }

    public static string AsText(IEnumerable<InfoItem> items)
    {
        var sb = new StringBuilder();
        foreach (var i in items) sb.Append(i.Key).Append(": ").AppendLine(i.Value);
        return sb.ToString();
    }

    private static readonly (string Name, string Description, string? Process, string? Service)[] Known =
    [
        ("Control Center Hotkey Service", "Background service, starts the Fn-key app and handles power events.", null, "CCDCHUService"),
        ("Fn hotkeys and OSD", "Fn-key functions and the on-screen popups (caps lock, volume …).", "FnKey", null),
        ("Control Center 3.0", "Clevo's own control app.", "ControlCenter30", null),
        ("LED keyboard settings", "Clevo's keyboard light app.", "LedKeyboardSetting", null),
        ("Flexikey", "Clevo's macro / key remapping tool.", "Flexikey", null),
        ("HotKey Clipboard Service", "Clevo clipboard helper service.", null, "HKClipSvc"),
    ];

    private static readonly (string Name, string Description, string Process)[] KnownLinux =
    [
        ("TUXEDO Control Center service", "tccd also controls fans, profiles and the keyboard and will fight with ClevoControl. Disable it: sudo systemctl disable --now tccd tccd-sleep", "tccd"),
        ("TUXEDO Control Center", "TUXEDO's own app (needs tccd).", "tuxedo-control-center"),
        ("ClevoControl service", "clevoctl daemon (systemd user service). The desktop app stops it while it runs.", "clevoctl"),
    ];

    public static List<ClevoComponent> ClevoComponents()
    {
        if (OperatingSystem.IsLinux())
        {
            var running = Platform.LinuxProcesses.AllNames();
            return KnownLinux.Select(k => new ClevoComponent(k.Name, k.Description, k.Process, null, running.Contains(k.Process), null)).ToList();
        }
        var list = new List<ClevoComponent>();
        foreach (var (name, desc, process, service) in Known)
        {
            bool running = false;
            string? path = null;
            if (process is not null)
            {
                try
                {
                    var p = Process.GetProcessesByName(process).FirstOrDefault();
                    running = p is not null;
                    try { path = p?.MainModule?.FileName; } catch { }
                }
                catch { }
            }
            if (service is not null)
            {
                try
                {
                    using var sc = new ManagementObjectSearcher($"SELECT State, PathName FROM Win32_Service WHERE Name='{service}'");
                    foreach (var o in sc.Get())
                    {
                        running = (o["State"] as string) == "Running";
                        path = o["PathName"] as string;
                    }
                }
                catch { }
            }
            list.Add(new ClevoComponent(name, desc, process, service, running, path));
        }
        return list;
    }

    /// <summary>Ends a Clevo process for the current session (it comes back after the next sign-in).</summary>
    public static bool StopProcess(string processName)
    {
        var ok = false;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try { p.Kill(); ok = true; }
            catch { }
        }
        return ok;
    }

    public static bool IsInstalled(string packagePrefix)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            return k?.GetSubKeyNames().Any(n => n.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase)) ?? false;
        }
        catch
        {
            return false;
        }
    }
}
