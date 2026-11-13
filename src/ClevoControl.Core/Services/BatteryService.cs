using System.Management;
using ClevoControl.Core.Native;

namespace ClevoControl.Core.Services;

public sealed record BatteryInfo(
    bool Present,
    int Percent,
    bool OnAc,
    bool Charging,
    TimeSpan? Remaining,
    int? DesignCapacityMWh,
    int? FullChargeCapacityMWh,
    int? RemainingCapacityMWh,
    int? CycleCount,
    double? Voltage,
    int? RateMw,
    string? Manufacturer,
    string? Chemistry)
{
    /// <summary>Health in percent (full charge capacity vs. design capacity).</summary>
    public double? HealthPercent => DesignCapacityMWh is > 0 && FullChargeCapacityMWh is > 0
        ? Math.Min(100, 100.0 * FullChargeCapacityMWh.Value / DesignCapacityMWh.Value)
        : null;
}

public static class BatteryService
{
    public static BatteryInfo Read()
    {
        Win32.GetSystemPowerStatus(out var ps);
        var present = ps.BatteryFlag != 128 && ps.BatteryFlag != 255;
        var percent = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : 0;
        var onAc = ps.ACLineStatus == 1;
        TimeSpan? remaining = ps.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(ps.BatteryLifeTime) : null;

        int? design = null, full = null, rem = null, cycles = null, rate = null;
        double? voltage = null;
        string? manufacturer = null, chemistry = null;
        bool charging = (ps.BatteryFlag & 8) != 0;

        Query("SELECT * FROM BatteryStaticData", o =>
        {
            design = ToInt(o["DesignedCapacity"]);
            manufacturer = (o["ManufactureName"] as string)?.Trim();
            chemistry = ToInt(o["Chemistry"]) switch
            {
                0x6E6F694C => "Li-Ion", // 'Lion'
                0x4C695049 or 0x4C695066 => "Li-Polymer",
                _ => null,
            };
        });
        Query("SELECT * FROM BatteryFullChargedCapacity", o => full = ToInt(o["FullChargedCapacity"]));
        Query("SELECT * FROM BatteryCycleCount", o => cycles = ToInt(o["CycleCount"]));
        Query("SELECT * FROM BatteryStatus", o =>
        {
            rem = ToInt(o["RemainingCapacity"]);
            var v = ToInt(o["Voltage"]);
            voltage = v is > 0 ? v / 1000.0 : null;
            var chargeRate = ToInt(o["ChargeRate"]) ?? 0;
            var dischargeRate = ToInt(o["DischargeRate"]) ?? 0;
            rate = chargeRate > 0 ? chargeRate : dischargeRate > 0 ? -dischargeRate : 0;
            if (o["Charging"] is bool c) charging = c;
        });

        return new BatteryInfo(present, percent, onAc, charging, remaining, design, full, rem, cycles is > 0 ? cycles : null, voltage, rate, manufacturer, chemistry);
    }

    private static void Query(string wql, Action<ManagementBaseObject> f)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", wql);
            foreach (var o in s.Get()) { f(o); break; }
        }
        catch { /* class not available */ }
    }

    private static int? ToInt(object? o)
    {
        try { return o is null ? null : Convert.ToInt32(o); }
        catch { return null; }
    }
}
