namespace ClevoControl.Core.Platform;

public enum SupportVerdict
{
    /// <summary>Clevo interface found – ClevoControl should work.</summary>
    Supported,
    /// <summary>Clevo interface found, but something limits it (e.g. per-key keyboard, driver missing).</summary>
    Partial,
    /// <summary>Not a Clevo EC interface (TongFang/Uniwill, System76 open firmware …).</summary>
    NotSupported,
    Unknown,
}

public sealed record SupportFinding(string Text, bool Ok);

public sealed record SupportReport(SupportVerdict Verdict, string Summary, IReadOnlyList<SupportFinding> Findings);

/// <summary>
/// "Will ClevoControl work on this laptop?" – decided from firmware interfaces, not from model
/// names (Clevo boards are sold under many brands). Sources of the identifiers: tuxedo-drivers
/// (clevo_interfaces.h, uniwill_interfaces.h, ite_8291) and System76's open firmware docs.
/// </summary>
public static class SupportCheck
{
    /// <summary>Clevo ACPI device used by clevo_acpi (and by Clevo's Windows driver).</summary>
    public const string ClevoAcpiHid = "CLV0001";

    /// <summary>WMI GUID shared by Clevo and Uniwill; only the extra Uniwill GUIDs tell them apart.</summary>
    public const string SharedWmiGuid = "ABBC0F6D-8EA1-11D1-00A0-C90629100000";

    public static readonly string[] UniwillOnlyWmiGuids =
        ["ABBC0F6E-8EA1-11D1-00A0-C90629100000", "ABBC0F6F-8EA1-11D1-00A0-C90629100000", "ABBC0F70-8EA1-11D1-00A0-C90629100000"];

    /// <summary>ITE USB controllers of per-key RGB keyboards (vendor 048d).</summary>
    public static readonly string[] PerKeyProducts = ["ce00", "6004", "600a", "600b", "8910"];

    public static SupportReport Run(Hardware.IClevoHardware? hw = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            var real = hw?.IsReal == true;
            return new SupportReport(real ? SupportVerdict.Supported : SupportVerdict.Unknown,
                real ? "Clevo driver (InsydeDCHU) found." : "Clevo driver (InsydeDCHU.dll) not found – install Clevo Control Center / your vendor's driver.",
                [new SupportFinding(hw?.Description ?? "no hardware", real)]);
        }
        return Evaluate(
            acpiDevices: List("sys/bus/acpi/devices"),
            wmiDevices: List("sys/bus/wmi/devices"),
            modules: List("sys/module"),
            usbIds: UsbIds(),
            sysVendor: LinuxSys.Dmi("sys_vendor"),
            boardName: LinuxSys.Dmi("board_name"),
            tuxedoIo: File.Exists(LinuxSys.P("dev/tuxedo_io")));
    }

    private static List<string> List(string dir)
    {
        try { return Directory.GetFileSystemEntries(LinuxSys.P(dir)).Select(Path.GetFileName).OfType<string>().ToList(); }
        catch { return []; }
    }

    private static List<string> UsbIds()
    {
        var list = new List<string>();
        try
        {
            foreach (var d in Directory.GetDirectories(LinuxSys.P("sys/bus/usb/devices")))
            {
                var v = Read(Path.Combine(d, "idVendor"));
                var p = Read(Path.Combine(d, "idProduct"));
                if (v is not null && p is not null) list.Add($"{v}:{p}");
            }
        }
        catch { }
        return list;

        static string? Read(string f)
        {
            try { return File.ReadAllText(f).Trim().ToLowerInvariant(); }
            catch { return null; }
        }
    }

    /// <summary>Pure decision logic (unit-tested).</summary>
    public static SupportReport Evaluate(IReadOnlyCollection<string> acpiDevices, IReadOnlyCollection<string> wmiDevices,
        IReadOnlyCollection<string> modules, IReadOnlyCollection<string> usbIds, string? sysVendor, string? boardName, bool tuxedoIo)
    {
        var f = new List<SupportFinding>();
        bool Has(IEnumerable<string> list, string prefix) => list.Any(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var clevoAcpi = Has(acpiDevices, ClevoAcpiHid);
        var sharedWmi = Has(wmiDevices, SharedWmiGuid);
        var uniwill = UniwillOnlyWmiGuids.Any(g => Has(wmiDevices, g));
        var system76 = modules.Contains("system76_acpi");
        var perKey = usbIds.Any(id => id.StartsWith("048d:", StringComparison.Ordinal) && PerKeyProducts.Contains(id[5..]));
        var driver = modules.Contains("clevo_acpi") || modules.Contains("clevo_wmi");

        f.Add(new($"System: {sysVendor ?? "?"} / board {boardName ?? "?"}", true));
        f.Add(new(clevoAcpi ? "Clevo ACPI device CLV0001 found" : "No Clevo ACPI device (CLV0001)", clevoAcpi));
        if (sharedWmi) f.Add(new("Clevo/Uniwill WMI interface found", !uniwill));
        if (uniwill) f.Add(new("Uniwill (TongFang) WMI interface found – this is not a Clevo laptop", false));
        if (system76) f.Add(new("System76 open firmware (system76_acpi) – no Clevo EC interface", false));
        if (perKey) f.Add(new("Per-key RGB keyboard controller (ITE) – keyboard lighting is not supported yet", false));
        f.Add(new(driver ? "Driver loaded: " + string.Join(", ", new[] { "clevo_acpi", "clevo_wmi" }.Where(modules.Contains))
                         : "clevo_acpi / clevo_wmi not loaded (tuxedo-drivers)", driver));
        f.Add(new(tuxedoIo ? "/dev/tuxedo_io present" : "/dev/tuxedo_io missing", tuxedoIo));

        if (uniwill && !clevoAcpi)
            return new(SupportVerdict.NotSupported, "TongFang/Uniwill laptop – not supported by ClevoControl (use TUXEDO Control Center or your vendor's tool).", f);
        if (system76 && !clevoAcpi)
            return new(SupportVerdict.NotSupported, "System76 laptop with open firmware – use System76's own tools.", f);
        if (!clevoAcpi && !sharedWmi)
            return new(SupportVerdict.NotSupported, "No Clevo interface found – this does not look like a Clevo-based laptop.", f);
        if (!driver || !tuxedoIo)
            return new(SupportVerdict.Partial, "Clevo laptop, but the Linux driver is not active – install tuxedo-drivers (see docs/LINUX.md; newer CPUs on non-TUXEDO laptops need a build without the compatibility check).", f);
        if (perKey)
            return new(SupportVerdict.Partial, "Clevo laptop – fans and profiles supported; per-key RGB keyboard lighting is not supported yet.", f);
        return new(SupportVerdict.Supported, "Clevo laptop with active driver – ClevoControl should work.", f);
    }
}
