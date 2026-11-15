using Microsoft.Win32;

namespace ClevoControl.Core.Services;

/// <summary>
/// Per-user autostart without admin rights.
/// Windows: the HKCU Run key. Linux: an XDG autostart entry in ~/.config/autostart.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ClevoControl";

    private static string LinuxEntry =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", "clevocontrol.desktop");

    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return File.Exists(LinuxEntry);
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool enabled, string exePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (!enabled) { File.Delete(LinuxEntry); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxEntry)!);
            File.WriteAllText(LinuxEntry, LinuxDesktopEntry(exePath, autostart: true));
            return;
        }
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) k.SetValue(Name, $"\"{exePath}\" --minimized");
        else k.DeleteValue(Name, false);
    }

    /// <summary>freedesktop .desktop entry for the app (menu entry and autostart).</summary>
    public static string LinuxDesktopEntry(string exePath, bool autostart) => $"""
        [Desktop Entry]
        Type=Application
        Name=ClevoControl
        GenericName=Laptop control center
        Comment=Keyboard lighting, fans and performance profiles for Clevo laptops
        Exec="{exePath}"{(autostart ? " --minimized" : "")}
        Icon=clevocontrol
        Terminal=false
        Categories=System;Settings;HardwareSettings;
        Keywords=Clevo;keyboard;RGB;fan;TUXEDO;XMG;
        StartupWMClass=ClevoControl
        {(autostart ? "X-GNOME-Autostart-enabled=true\n" : "")}
        """;
}
