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

        list.Add(new("Keyboard type", hw.KeyboardType.ToString()));
        list.Add(new("DCHU", hw.Description));
        list.Add(new("ClevoControl", typeof(SystemInfoService).Assembly.GetName().Version?.ToString(3) ?? "?"));
        try
        {
            list.Add(new("EC spec 0x0D", Convert.ToHexString(hw.ReadSpecBuffer().AsSpan(0, 48))));
            list.Add(new("EC fan 0x0C", Convert.ToHexString(hw.ReadFanBuffer().AsSpan(0, 32))));
        }
        catch { }
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

    public static List<ClevoComponent> ClevoComponents()
    {
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
