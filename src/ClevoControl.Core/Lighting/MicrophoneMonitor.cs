using Microsoft.Win32;

namespace ClevoControl.Core.Lighting;

/// <summary>
/// Detects whether any app currently records from the microphone, using the same registry
/// data Windows uses for the microphone privacy indicator in the taskbar.
/// </summary>
public static class MicrophoneMonitor
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    public static bool IsMicrophoneInUse()
    {
        if (OperatingSystem.IsLinux()) return Platform.LinuxAudio.IsMicrophoneInUse();
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root);
            return key is not null && Scan(key, 0);
        }
        catch
        {
            return false;
        }
    }

    private static bool Scan(RegistryKey key, int depth)
    {
        foreach (var name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub is null) continue;
            if (sub.GetValue("LastUsedTimeStop") is long stop && stop == 0 && sub.GetValue("LastUsedTimeStart") is long start && start != 0)
                return true;
            if (depth == 0 && name == "NonPackaged" && Scan(sub, 1)) return true;
        }
        return false;
    }
}
