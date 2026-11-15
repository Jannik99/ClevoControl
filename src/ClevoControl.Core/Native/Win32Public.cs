namespace ClevoControl.Core.Native;

/// <summary>Small public wrappers for the UI.</summary>
public static class Win32Public
{
    public static void PowerStatus(out bool onAc, out int percent)
    {
        var (ac, pct, hasBattery) = Platform.SystemStatus.Power();
        onAc = ac;
        percent = hasBattery ? pct : 0;
    }
}
