using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ClevoControl.Core.Services;

public enum VendorStartupKind
{
    /// <summary>Value in a Run key (HKCU, HKLM or HKLM WOW6432Node).</summary>
    RunEntry,
    /// <summary>Shortcut in the user's or the common Startup folder.</summary>
    StartupFolder,
    /// <summary>Startup task of an MSIX/Store package.</summary>
    PackageTask,
    /// <summary>Task Scheduler task whose action runs vendor software.</summary>
    ScheduledTask,
    /// <summary>Windows service (start type Automatic vs. Manual).</summary>
    Service,
    /// <summary>systemd unit (Linux).</summary>
    SystemdUnit,
    /// <summary>XDG autostart .desktop entry (Linux).</summary>
    XdgAutostart,
}

/// <summary>One autostart entry of other vendor software (Clevo Control Center, TUXEDO Control Center …).</summary>
/// <param name="Id">Kind-specific locator, e.g. <c>run|HKLM|Run|Name</c>. Only meant for <see cref="VendorAutostart.SetEnabled"/>.</param>
/// <param name="Name">Entry name as the system shows it (Run value name, service display name, task name …).</param>
/// <param name="Description">What the program is (localised through <see cref="VendorAutostart.Text"/>).</param>
/// <param name="Enabled">True when it starts automatically.</param>
/// <param name="NeedsAdmin">Changing it asks for admin rights (UAC / pkexec).</param>
/// <param name="Warning">Side effect of turning it off, or null.</param>
public sealed record VendorStartupItem(string Id, string Name, string Description, VendorStartupKind Kind, bool Enabled, bool NeedsAdmin, string? Warning);

/// <summary>
/// Finds and switches the autostart of other vendor control software, so it does not fight with ClevoControl
/// over fans and keyboard light. Everything is reversible:
/// <list type="bullet">
/// <item>Run entries and Startup-folder shortcuts: the <c>Explorer\StartupApproved</c> flag, exactly like
/// Task Manager's Startup tab (the entry itself is never deleted). HKCU entries per user without admin;
/// HKLM entries use the HKLM flag (that is where Task Manager keeps it) and need admin.</item>
/// <item>MSIX startup tasks: the per-user <c>State</c> value (2 = enabled, 1 = disabled by the user).</item>
/// <item>Scheduled tasks: <c>schtasks /change /disable|/enable</c>, elevated if needed.</item>
/// <item>Services: start type Automatic ↔ Manual through one elevated <c>sc.exe config</c>. A running
/// service is not stopped; it just no longer starts with Windows.</item>
/// <item>Linux: <c>pkexec systemctl enable|disable</c> for tccd; XDG autostart entries are hidden per user
/// with a <c>Hidden=true</c> override in ~/.config/autostart.</item>
/// </list>
/// Kernel drivers (e.g. the ACPI bridge that ClevoControl itself uses through InsydeDCHU.dll) are never listed.
/// </summary>
public static class VendorAutostart
{
    /// <summary>Translator (German, English) for descriptions and warnings. The UI sets it to its Loc.T.</summary>
    public static Func<string, string, string> Text { get; set; } = (_, en) => en;

    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    private const string SystemAppData = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
    private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

    // ------------------------------------------------------------------ matching (pure)

    /// <summary>Patterns that identify vendor control software in names, paths and command lines.</summary>
    private static readonly Regex VendorPattern = new(
        @"\bCC30\b|LedKeyboardSetting|Flexikey|\bFnKey|FnhotkeysandOSD|FanSpeedSetting|Fan[ _]Speed[ _]Setting|" +
        @"HKClipSvc|DCHUService|CCDCHU|CLEVOCO\.|\bClevo|504814C03D814|\btccd\b|tuxedo[-_ ]control[-_ ]center",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// "Control Center" alone: Clevo's app and its rebrands (Obsidian, XMG, Eurocom …) – but many unrelated
    /// products use the name too, so <see cref="OtherControlCenters"/> is excluded.
    /// </summary>
    private static readonly Regex GenericPattern = new(@"ControlCenter|Control[ _]Center", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OtherControlCenters = new(
        @"Killer|Rivet|\bIntel|NVIDIA|\bAMD\b|Radeon|Realtek|Dolby|\bLogi|Corsair|Razer|\bApple|Microsoft|Thunderbolt|SteelSeries|Alienware|\bDell\b|Lenovo|\bHP\b|\bASUS|Armoury|\bMSI\b|Gigabyte|\bAcer\b|Samsung|Huawei|Synaptics",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Never ours: ClevoControl's own entries contain "ClevoCo…".</summary>
    private static readonly Regex OwnPattern = new(@"ClevoControl|clevoctl", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when any of the texts points to vendor control software (and none to ClevoControl itself).</summary>
    internal static bool IsVendor(params string?[] texts)
    {
        bool strong = false, generic = false, other = false;
        foreach (var t in texts)
        {
            if (string.IsNullOrEmpty(t)) continue;
            if (OwnPattern.IsMatch(t)) return false;
            strong |= VendorPattern.IsMatch(t);
            generic |= GenericPattern.IsMatch(t);
            other |= OtherControlCenters.IsMatch(t);
        }
        return strong || (generic && !other);
    }

    /// <summary>Description and warning for a matched entry, by what it runs.</summary>
    internal static (string Description, string? Warning) Describe(params string?[] texts)
    {
        var all = string.Join(" ", texts.Where(t => !string.IsNullOrEmpty(t)));
        bool Has(string pattern) => Regex.IsMatch(all, pattern, RegexOptions.IgnoreCase);
        if (Has(@"DCHUService|CCDCHU|Hotkey Service"))
            return (Text("Clevo-Hintergrunddienst: startet die Fn-Tasten-App und meldet Energieereignisse.",
                    "Clevo background service: starts the Fn-key app and handles power events."),
                Text("Ohne diesen Dienst funktionieren Fn-Tasten und Bildschirmanzeigen (OSD) evtl. nicht mehr. ClevoControl selbst braucht ihn nicht.",
                    "Without this service, Fn keys and on-screen popups (OSD) may stop working. ClevoControl itself does not need it."));
        if (Has(@"\bFnKey|FnhotkeysandOSD"))
            return (Text("Fn-Tasten und Bildschirmanzeigen (Feststelltaste, Lautstärke …).", "Fn keys and on-screen popups (caps lock, volume …)."),
                Text("Ohne diese App funktionieren manche Fn-Tasten und das OSD evtl. nicht mehr.", "Without it, some Fn keys and the OSD may stop working."));
        if (Has("HKClipSvc"))
            return (Text("Clevo-Hilfsdienst für Hotkeys.", "Clevo helper service for hotkeys."),
                Text("Kann einzelne Clevo-Hotkeys betreffen.", "May affect some Clevo hotkeys."));
        if (Has("LedKeyboardSetting"))
            return (Text("Clevos Tastaturlicht-App, überschreibt das Licht von ClevoControl.", "Clevo's keyboard light app; overrides ClevoControl's lighting."), null);
        if (Has("Flexikey"))
            return (Text("Clevos Makro- und Tastenbelegungs-Tool.", "Clevo's macro and key remapping tool."), null);
        if (Has(@"FanSpeedSetting|Fan[ _]Speed|504814C03D814"))
            return (Text("Clevos Lüfter-App, überschreibt die Lüftersteuerung.", "Clevo's fan app; overrides the fan control."), null);
        if (Has(@"tccd"))
            return (Text("TUXEDO-Control-Center-Dienst: steuert Lüfter, Profile und Tastatur und kämpft mit ClevoControl.",
                    "TUXEDO Control Center service: controls fans, profiles and keyboard and fights with ClevoControl."),
                Text("Das TUXEDO Control Center funktioniert ohne diesen Dienst nicht.", "TUXEDO Control Center does not work without this service."));
        if (Has(@"tuxedo"))
            return (Text("TUXEDOs eigene Steuer-App (Infobereich).", "TUXEDO's own control app (tray)."), null);
        if (Has(@"ControlCenter|Control[ _]Center|\bCC30\b"))
            return (Text("Control Center des Herstellers: setzt Lüfter, Licht und Profile.", "The vendor's Control Center: sets fans, light and profiles."), null);
        return (Text("Software des Laptop-Herstellers.", "Software from the laptop vendor."), null);
    }

    // ------------------------------------------------------------------ StartupApproved values (pure)

    /// <summary>
    /// The 12-byte StartupApproved value: 0x02 = enabled (rest zero), 0x03 = disabled followed by the
    /// time of disabling (FILETIME, UTC) at offset 4 – the format Task Manager writes.
    /// </summary>
    internal static byte[] ApprovedValue(bool enabled, DateTime utcNow)
    {
        var v = new byte[12];
        v[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled) BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(v, 4);
        return v;
    }

    /// <summary>
    /// Missing or empty values mean enabled. Even first bytes (0x02, 0x06) are enabled, odd ones (0x03, 0x07 …) disabled.
    /// </summary>
    internal static bool IsApprovedEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    // ------------------------------------------------------------------ Task Scheduler XML (pure)

    internal sealed record TaskInfo(string Path, string Command, string? Arguments, bool Enabled, string Xml);

    /// <summary>Splits the output of <c>schtasks /query /xml</c> (one XML document per task, each after a &lt;!-- \path --&gt; comment).</summary>
    internal static List<TaskInfo> ParseTaskList(string output)
    {
        var list = new List<TaskInfo>();
        var parts = Regex.Split(output, @"<!--\s*(\\[^>]*?)\s*-->");
        // parts: [prefix, path1, xml1, path2, xml2, …]
        for (var i = 1; i + 1 < parts.Length; i += 2)
        {
            var path = parts[i].Trim();
            var xml = parts[i + 1];
            var command = XmlValue(xml, "Command");
            if (command is null) continue;
            var settings = Regex.Match(xml, @"<Settings>(.*?)</Settings>", RegexOptions.Singleline);
            var enabled = !settings.Success || !Regex.IsMatch(settings.Groups[1].Value, @"<Enabled>\s*false\s*</Enabled>", RegexOptions.IgnoreCase);
            list.Add(new TaskInfo(path, command, XmlValue(xml, "Arguments"), enabled, xml));
        }
        return list;
    }

    private static string? XmlValue(string xml, string tag)
    {
        var m = Regex.Match(xml, $@"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline);
        return m.Success
            ? m.Groups[1].Value.Trim().Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&")
            : null;
    }

    // ------------------------------------------------------------------ services (pure)

    /// <summary>"@oem156.inf,%AcpiBridge1.SVCDESC%;Control Center Hotkey Service" → "Control Center Hotkey Service".</summary>
    internal static string ServiceDisplayName(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (raw.StartsWith('@'))
        {
            var semi = raw.LastIndexOf(';');
            return semi >= 0 && semi < raw.Length - 1 ? raw[(semi + 1)..].Trim() : fallback;
        }
        return raw.Trim();
    }

    // ------------------------------------------------------------------ Linux helpers (pure)

    /// <summary>First word of <c>systemctl is-enabled</c>: null when the unit does not exist.</summary>
    internal static bool? ParseIsEnabled(string? output)
    {
        var word = output?.Trim().Split('\n')[0].Trim();
        return word switch
        {
            "enabled" or "enabled-runtime" or "alias" or "static" or "indirect" or "generated" or "transient" => true,
            "disabled" or "masked" or "masked-runtime" or "linked" or "linked-runtime" => false,
            _ => null,
        };
    }

    /// <summary>A desktop entry does not autostart with Hidden=true or X-GNOME-Autostart-enabled=false.</summary>
    internal static bool DesktopAutostarts(string content)
    {
        foreach (var (key, value) in DesktopEntryKeys(content))
        {
            if (key == "Hidden" && value.Equals("true", StringComparison.OrdinalIgnoreCase)) return false;
            if (key == "X-GNOME-Autostart-enabled" && value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    internal static string? DesktopName(string content) =>
        DesktopEntryKeys(content).Where(k => k.Key == "Name").Select(k => k.Value).FirstOrDefault();

    private static IEnumerable<(string Key, string Value)> DesktopEntryKeys(string content)
    {
        var inMain = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inMain = line == "[Desktop Entry]"; continue; }
            if (!inMain || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq > 0) yield return (line[..eq].Trim(), line[(eq + 1)..].Trim());
        }
    }

    /// <summary>Marker line in overrides we write, so re-enabling can delete the whole file.</summary>
    internal const string OverrideMarker = "# Written by ClevoControl to turn off this autostart entry. Delete this file to turn it on again.";

    /// <summary>Per-user override for a system-wide autostart entry (same file name in ~/.config/autostart).</summary>
    internal static string DesktopOverride(string name) => $"[Desktop Entry]\n{OverrideMarker}\nType=Application\nName={name}\nHidden=true\n";

    /// <summary>True when the file is one of our pure overrides (safe to delete).</summary>
    internal static bool IsOurOverride(string content) => content.Contains(OverrideMarker, StringComparison.Ordinal);

    /// <summary>
    /// Edits a user-owned entry in place: off adds Hidden=true, on removes Hidden=true and
    /// X-GNOME-Autostart-enabled=false from the [Desktop Entry] group. Other lines are kept.
    /// </summary>
    internal static string DesktopSetAutostart(string content, bool enabled)
    {
        var nl = content.Contains("\r\n") ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var inMain = false;
        var mainEnd = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith('['))
            {
                if (inMain && mainEnd < 0) mainEnd = i;
                inMain = line == "[Desktop Entry]";
                continue;
            }
            if (!inMain) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (key == "Hidden" || (key == "X-GNOME-Autostart-enabled" && enabled))
            {
                lines.RemoveAt(i);
                i--;
            }
        }
        if (!enabled)
        {
            if (inMain && mainEnd < 0) mainEnd = lines.Count;
            if (mainEnd < 0) { lines.Insert(0, "[Desktop Entry]"); mainEnd = 1; }
            // Insert before trailing blank lines of the group.
            while (mainEnd > 1 && lines[mainEnd - 1].Trim().Length == 0) mainEnd--;
            lines.Insert(mainEnd, "Hidden=true");
        }
        return string.Join(nl, lines);
    }

    /// <summary>"Service · needs admin rights" – the kind line the settings page shows under the description.</summary>
    public static string KindLabel(VendorStartupItem item)
    {
        var kind = item.Kind switch
        {
            VendorStartupKind.RunEntry => Text("Autostart-Eintrag", "Startup entry"),
            VendorStartupKind.StartupFolder => Text("Autostart-Ordner", "Startup folder"),
            VendorStartupKind.PackageTask => Text("App-Starttask", "App startup task"),
            VendorStartupKind.ScheduledTask => Text("Geplante Aufgabe", "Scheduled task"),
            VendorStartupKind.Service => Text("Dienst", "Service"),
            VendorStartupKind.SystemdUnit => Text("systemd-Dienst", "systemd service"),
            VendorStartupKind.XdgAutostart => Text("Autostart-Eintrag", "Autostart entry"),
            _ => "",
        };
        return item.NeedsAdmin ? kind + " · " + Text("braucht Adminrechte", "needs admin rights") : kind;
    }

    // ------------------------------------------------------------------ discovery

    /// <summary>Finds the autostart entries of known vendor software. Slow (a few hundred ms); call it off the UI thread.</summary>
    public static IReadOnlyList<VendorStartupItem> Discover()
    {
        var list = new List<VendorStartupItem>();
        void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception ex) { Log.Warn($"VendorAutostart: {what}: {ex.Message}"); }
        }
        if (OperatingSystem.IsWindows())
        {
            Try("Run keys", () => DiscoverRun(list));
            Try("Startup folders", () => DiscoverStartupFolders(list));
            Try("package tasks", () => DiscoverPackageTasks(list));
            Try("scheduled tasks", () => DiscoverScheduledTasks(list));
            Try("services", () => DiscoverServices(list));
        }
        else if (OperatingSystem.IsLinux())
        {
            Try("systemd", () => DiscoverSystemd(list));
            Try("XDG autostart", () => DiscoverXdg(list));
        }
        return list;
    }

    private static readonly (RegistryHive Hive, string Key, string ApprovedName, string Label)[] RunKeys =
    [
        (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "HKCU"),
        (RegistryHive.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "HKLM"),
        (RegistryHive.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", "HKLM"),
    ];

    private static RegistryKey Base(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    private static void DiscoverRun(List<VendorStartupItem> list)
    {
        foreach (var (hive, key, approvedName, label) in RunKeys)
        {
            using var b = Base(hive);
            using var k = b.OpenSubKey(key);
            if (k is null) continue;
            foreach (var name in k.GetValueNames())
            {
                var command = k.GetValue(name)?.ToString();
                if (!IsVendor(name, command)) continue;
                var enabled = ReadApproved(hive, approvedName, name);
                var (desc, warn) = Describe(name, command);
                list.Add(new VendorStartupItem($"run|{label}|{approvedName}|{name}", name, desc, VendorStartupKind.RunEntry, enabled,
                    hive == RegistryHive.LocalMachine, warn));
            }
        }
    }

    private static bool ReadApproved(RegistryHive hive, string approvedName, string valueName)
    {
        using var b = Base(hive);
        using var k = b.OpenSubKey(Approved + approvedName);
        return IsApprovedEnabled(k?.GetValue(valueName) as byte[]);
    }

    private static void DiscoverStartupFolders(List<VendorStartupItem> list)
    {
        foreach (var (folder, hive, label) in new[]
                 {
                     (Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "HKCU"),
                     (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "HKLM"),
                 })
        {
            if (folder.Length == 0 || !Directory.Exists(folder)) continue;
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsVendor(name, ShortcutText(file))) continue;
                var (desc, warn) = Describe(name, ShortcutText(file));
                list.Add(new VendorStartupItem($"folder|{label}|StartupFolder|{name}", Path.GetFileNameWithoutExtension(name), desc,
                    VendorStartupKind.StartupFolder, ReadApproved(hive, "StartupFolder", name), hive == RegistryHive.LocalMachine, warn));
            }
        }
    }

    /// <summary>The printable strings of a shortcut file (target path etc.), good enough for pattern matching.</summary>
    private static string ShortcutText(string file)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            if (bytes.Length > 1 << 20) return "";
            return Encoding.Latin1.GetString(bytes) + " " + Encoding.Unicode.GetString(bytes);
        }
        catch { return ""; }
    }

    private static void DiscoverPackageTasks(List<VendorStartupItem> list)
    {
        using var root = Registry.CurrentUser.OpenSubKey(SystemAppData);
        if (root is null) return;
        foreach (var family in root.GetSubKeyNames())
        {
            if (!IsVendor(family)) continue;
            using var fk = root.OpenSubKey(family);
            if (fk is null) continue;
            foreach (var taskId in fk.GetSubKeyNames())
            {
                using var tk = fk.OpenSubKey(taskId);
                if (tk?.GetValue("State") is not int state) continue;
                // StartupTaskState: 0 disabled, 1 disabled by user, 2 enabled, 3 disabled by policy, 4 enabled by policy.
                if (state is 3 or 4) continue; // policy, not ours to change
                var (desc, warn) = Describe(family, taskId);
                list.Add(new VendorStartupItem($"pkg|{family}|{taskId}", $"{PackageName(family)} ({taskId})", desc, VendorStartupKind.PackageTask,
                    state == 2, false, warn));
            }
        }
    }

    private static string PackageName(string family)
    {
        var n = family.Split('_')[0];
        return n.StartsWith("CLEVOCO.", StringComparison.OrdinalIgnoreCase) ? n[8..] : n;
    }

    private static void DiscoverScheduledTasks(List<VendorStartupItem> list)
    {
        var (code, output) = RunCapture("schtasks.exe", ["/query", "/xml"], 20000);
        if (code != 0) return;
        var me = Environment.UserName;
        foreach (var t in ParseTaskList(output))
        {
            if (!IsVendor(t.Path, t.Command, t.Arguments)) continue;
            var (desc, warn) = Describe(t.Path, t.Command);
            // Tasks registered for the current user can usually be changed without admin rights.
            var mine = t.Xml.Contains(me, StringComparison.OrdinalIgnoreCase) && !t.Xml.Contains("HighestAvailable", StringComparison.Ordinal);
            list.Add(new VendorStartupItem($"task|{t.Path}", t.Path.TrimStart('\\'), desc, VendorStartupKind.ScheduledTask, t.Enabled, !mine, warn));
        }
    }

    private static void DiscoverServices(List<VendorStartupItem> list)
    {
        using var root = Base(RegistryHive.LocalMachine).OpenSubKey(ServicesKey);
        if (root is null) return;
        foreach (var name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            if (k is null) continue;
            // Only Win32 services (0x10 own process, 0x20 shared). Kernel/file system drivers – such as the
            // ACPI bridge ClevoControl needs to reach the EC – are never listed.
            if (k.GetValue("Type") is not int type || (type & 0x30) == 0) continue;
            var image = k.GetValue("ImagePath")?.ToString();
            var display = ServiceDisplayName(k.GetValue("DisplayName")?.ToString(), name);
            if (!IsVendor(name, image, display)) continue;
            if (k.GetValue("Start") is not int start) continue;
            var (desc, warn) = Describe(name, image, display);
            list.Add(new VendorStartupItem($"svc|{name}", display, desc, VendorStartupKind.Service, start == 2, true, warn));
        }
    }

    // ---- Linux

    private static readonly string[] SystemdUnits = ["tccd.service", "tccd-sleep.service"];

    private static void DiscoverSystemd(List<VendorStartupItem> list)
    {
        foreach (var unit in SystemdUnits)
        {
            var (_, output) = RunCapture("systemctl", ["is-enabled", unit], 3000);
            if (ParseIsEnabled(output) is not { } enabled) continue;
            var (desc, warn) = Describe(unit);
            if (unit.StartsWith("tccd-sleep", StringComparison.Ordinal))
                (desc, warn) = (Text("Hilfsdienst des TUXEDO Control Centers für Standby und Aufwachen.", "TUXEDO Control Center helper for suspend and resume."), null);
            list.Add(new VendorStartupItem($"systemd|{unit}", unit, desc, VendorStartupKind.SystemdUnit, enabled, true, warn));
        }
    }

    private static string UserAutostartDir =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart");

    private static IEnumerable<string> SystemAutostartDirs()
    {
        var dirs = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS") is { Length: > 0 } d ? d.Split(':') : ["/etc/xdg"];
        return dirs.Where(x => x.Length > 0).Select(x => Path.Combine(x, "autostart"));
    }

    private static void DiscoverXdg(List<VendorStartupItem> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var user = UserAutostartDir;
        foreach (var dir in new[] { user }.Concat(SystemAutostartDirs()))
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.desktop"))
            {
                var fileName = Path.GetFileName(file);
                if (!seen.Add(fileName)) continue; // a user file overrides system files with the same name
                var content = File.ReadAllText(file);
                var systemFile = dir == user ? SystemAutostartDirs().Select(d => Path.Combine(d, fileName)).FirstOrDefault(File.Exists) : file;
                var reference = IsOurOverride(content) && systemFile is not null ? File.ReadAllText(systemFile) : content;
                if (!IsVendor(fileName, DesktopName(reference), reference)) continue;
                var (desc, warn) = Describe(fileName, reference);
                list.Add(new VendorStartupItem($"xdg|{fileName}", DesktopName(reference) ?? fileName, desc, VendorStartupKind.XdgAutostart,
                    DesktopAutostarts(content), false, warn));
            }
        }
    }

    // ------------------------------------------------------------------ switching

    /// <summary>Turns the autostart of an entry on or off. May show a UAC / pkexec prompt; call it off the UI thread.</summary>
    public static bool SetEnabled(VendorStartupItem item, bool enabled, out string? error)
    {
        error = null;
        try
        {
            var parts = item.Id.Split('|');
            switch (parts[0])
            {
                case "run" or "folder" when OperatingSystem.IsWindows():
                    return SetApproved(parts[1], parts[2], string.Join('|', parts[3..]), enabled, out error);
                case "pkg" when OperatingSystem.IsWindows():
                    {
                        using var k = Registry.CurrentUser.OpenSubKey($@"{SystemAppData}\{parts[1]}\{parts[2]}", writable: true)
                            ?? throw new InvalidOperationException("Startup task not found.");
                        k.SetValue("State", enabled ? 2 : 1, RegistryValueKind.DWord);
                        if (enabled) k.SetValue("UserEnabledStartupOnce", 1, RegistryValueKind.DWord);
                        return true;
                    }
                case "task" when OperatingSystem.IsWindows():
                    {
                        string[] args = ["/change", "/tn", parts[1], enabled ? "/enable" : "/disable"];
                        var (code, output) = RunCapture("schtasks.exe", args, 15000);
                        if (code == 0) return true;
                        // Usually "access denied" for tasks of other users / SYSTEM: try again elevated.
                        Log.Info($"VendorAutostart: schtasks without admin failed ({output.Trim()}), asking for admin rights");
                        return RunElevated("schtasks.exe", $"/change /tn \"{parts[1]}\" {(enabled ? "/enable" : "/disable")}", out error);
                    }
                case "svc" when OperatingSystem.IsWindows():
                    {
                        var start = "demand";
                        if (enabled)
                        {
                            using var k = Base(RegistryHive.LocalMachine).OpenSubKey($@"{ServicesKey}\{parts[1]}");
                            start = k?.GetValue("DelayedAutostart") is 1 ? "delayed-auto" : "auto";
                        }
                        return RunElevated("sc.exe", $"config \"{parts[1]}\" start= {start}", out error);
                    }
                case "systemd" when OperatingSystem.IsLinux():
                    {
                        var (code, output) = RunCapture("pkexec", ["systemctl", enabled ? "enable" : "disable", parts[1]], 120000);
                        if (code == 0) return true;
                        error = code is 126 or 127 ? "Cancelled." : output.Trim();
                        return false;
                    }
                case "xdg" when OperatingSystem.IsLinux():
                    return SetXdg(parts[1], enabled, out error);
                default:
                    error = "Not supported on this system.";
                    return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Warn($"VendorAutostart: {item.Id} → {enabled}: {ex.Message}");
            return false;
        }
    }

    private static bool SetApproved(string hiveLabel, string approvedName, string valueName, bool enabled, out string? error)
    {
        error = null;
        var value = ApprovedValue(enabled, DateTime.UtcNow);
        if (hiveLabel == "HKCU")
        {
            using var k = Registry.CurrentUser.CreateSubKey(Approved + approvedName);
            k.SetValue(valueName, value, RegistryValueKind.Binary);
            return true;
        }
        try
        {
            using var b = Base(RegistryHive.LocalMachine);
            using var k = b.CreateSubKey(Approved + approvedName, writable: true);
            k.SetValue(valueName, value, RegistryValueKind.Binary);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            var hex = Convert.ToHexString(value);
            return RunElevated("reg.exe", $"add \"HKLM\\{Approved}{approvedName}\" /v \"{valueName}\" /t REG_BINARY /d {hex} /f /reg:64", out error);
        }
    }

    private static bool SetXdg(string fileName, bool enabled, out string? error)
    {
        error = null;
        var userFile = Path.Combine(UserAutostartDir, fileName);
        var systemFile = SystemAutostartDirs().Select(d => Path.Combine(d, fileName)).FirstOrDefault(File.Exists);
        if (File.Exists(userFile))
        {
            var content = File.ReadAllText(userFile);
            if (IsOurOverride(content))
            {
                if (enabled) File.Delete(userFile);
                return true;
            }
            File.WriteAllText(userFile, DesktopSetAutostart(content, enabled));
            return true;
        }
        if (enabled) return true; // nothing overrides the system entry
        if (systemFile is null) { error = "Autostart entry not found."; return false; }
        Directory.CreateDirectory(UserAutostartDir);
        File.WriteAllText(userFile, DesktopOverride(DesktopName(File.ReadAllText(systemFile)) ?? fileName));
        return true;
    }

    // ------------------------------------------------------------------ running apps

    /// <summary>Processes of vendor control apps that override fans or light while they run.</summary>
    private static readonly string[] ConflictingProcesses = ["ControlCenter30", "LedKeyboardSetting"];

    /// <summary>Names of running vendor control apps (Windows: Control Center 3.0, LED keyboard settings).</summary>
    public static IReadOnlyList<string> RunningControlApps()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var running = new List<string>();
        foreach (var name in ConflictingProcesses)
        {
            try
            {
                var ps = Process.GetProcessesByName(name);
                if (ps.Length > 0) running.Add(name);
                foreach (var p in ps) p.Dispose();
            }
            catch { }
        }
        return running;
    }

    /// <summary>Ends the running vendor control apps of this session. True when at least one was ended.</summary>
    public static bool QuitControlApps()
    {
        var ok = false;
        foreach (var name in ConflictingProcesses) ok |= SystemInfoService.StopProcess(name);
        return ok;
    }

    // ------------------------------------------------------------------ process helpers

    private static (int Code, string Output) RunCapture(string program, string[] args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(program)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            if (!OperatingSystem.IsWindows()) psi.Environment["LC_ALL"] = "C";
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (-1, $"{program} timed out");
            }
            return (p.ExitCode, stdout.Result + stderr.Result);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>Runs a Windows tool elevated (UAC prompt). False with an error when it fails or the prompt is cancelled.</summary>
    private static bool RunElevated(string program, string arguments, out string? error)
    {
        error = null;
        try
        {
            var psi = new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p is null) { error = "Could not start " + program; return false; }
            if (!p.WaitForExit(60000)) { error = program + " timed out"; return false; }
            if (p.ExitCode == 0) return true;
            error = $"{program} exit code {p.ExitCode}";
            return false;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = Text("Abgebrochen (Administratorrechte nicht erteilt).", "Cancelled (admin rights not granted).");
            return false;
        }
    }
}
