using Microsoft.Win32;

namespace ClevoControl.Core.Services;

/// <summary>Per-user autostart via the HKCU Run key (no admin rights needed).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ClevoControl";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool enabled, string exePath)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(Name, $"\"{exePath}\" --minimized");
        else k.DeleteValue(Name, false);
    }
}
