namespace ClevoControl.Core.Native;

/// <summary>Small public wrappers for the UI.</summary>
public static class Win32Public
{
    public static void PowerStatus(out bool onAc, out int percent)
    {
        Win32.GetSystemPowerStatus(out var s);
        onAc = s.ACLineStatus == 1;
        percent = s.BatteryLifePercent <= 100 ? s.BatteryLifePercent : 0;
    }
}
