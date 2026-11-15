using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ClevoControl.Core.Settings;

namespace ClevoControl.Core.Automation;

public sealed record ForegroundApp(string ProcessName, string? Path, string Title);

public sealed record Detection(AppCategory Category, string AppName, string ProcessName, string? SceneOverride = null)
{
    public static readonly Detection Nothing = new(AppCategory.None, "", "");
}

/// <summary>Looks at the foreground window and decides what kind of app it is.</summary>
public static class AppDetector
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ClevoControl", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "LockApp", "Taskmgr", "dwm",
        // Linux desktops and ClevoControl itself
        "clevocontrol", "clevoctl", "gnome-shell", "plasmashell", "kwin_wayland", "kwin_x11", "Xorg", "Xwayland", "xfdesktop", "nautilus", "dolphin",
    };

    public static ForegroundApp? Foreground(AutoProfileSettings? settings = null)
    {
        if (OperatingSystem.IsLinux()) return LinuxForeground(settings);
        if (!OperatingSystem.IsWindows()) return null;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        string? path = null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                var size = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size)) path = sb.ToString();
            }
            finally { CloseHandle(h); }
        }
        var name = path is not null ? System.IO.Path.GetFileNameWithoutExtension(path) : SafeProcessName((int)pid);
        return name is null ? null : new ForegroundApp(name, path, title.ToString());
    }


    /// <summary>
    /// Linux: the focused window where the desktop tells us (X11, KDE with kdotool); otherwise
    /// the most relevant known app that is running (games first), since GNOME/Wayland hide the focus.
    /// </summary>
    private static ForegroundApp? LinuxForeground(AutoProfileSettings? settings)
    {
        if (Platform.LinuxProcesses.ActiveWindow() is { } w && Platform.LinuxProcesses.Describe(w.Pid) is { } id)
            return new ForegroundApp(id.Name, id.Path, w.Title);

        settings ??= new AutoProfileSettings { DetectFullscreenGames = false };
        ForegroundApp? best = null;
        var bestRank = int.MaxValue;
        foreach (var p in Platform.LinuxProcesses.All())
        {
            var app = new ForegroundApp(p.Name, p.Path, "");
            var d = Classify(app, settings);
            if (d.Category == AppCategory.None) continue;
            var rank = d.Category == AppCategory.Game ? 0 : 1 + (int)d.Category;
            if (rank < bestRank) { best = app; bestRank = rank; }
        }
        return best;
    }
    private static string? SafeProcessName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return null; }
    }

    public static bool IsExclusiveFullscreen()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { return SHQueryUserNotificationState(out var s) == 0 && s == QUNS_RUNNING_D3D_FULL_SCREEN; }
        catch { return false; }
    }

    /// <summary>Classifies a foreground app. Order: own rules → catalog → store folders → engine → fullscreen.</summary>
    public static Detection Classify(ForegroundApp app, AutoProfileSettings settings)
    {
        if (Ignore.Contains(app.ProcessName)) return Detection.Nothing;

        foreach (var rule in settings.CustomRules)
        {
            if (string.Equals(rule.ProcessName, app.ProcessName, StringComparison.OrdinalIgnoreCase))
                return new Detection(rule.Category, rule.DisplayName ?? app.ProcessName, app.ProcessName, rule.SceneName);
        }

        var isTitleHost = AppCatalog.TitleHosts.Contains(app.ProcessName);
        foreach (var known in AppCatalog.Apps)
        {
            var processMatch = known.Processes.Any(p => string.Equals(p, app.ProcessName, StringComparison.OrdinalIgnoreCase));
            var titleMatch = known.TitleKeywords?.Any(k => app.Title.Contains(k, StringComparison.OrdinalIgnoreCase)) == true;
            if (known.Processes.Length > 0 && known.TitleKeywords is not null)
            {
                if (processMatch && titleMatch) return new Detection(known.Category, known.Name, app.ProcessName);
            }
            else if (processMatch || (isTitleHost && known.Processes.Length == 0 && titleMatch))
            {
                return new Detection(known.Category, known.Name, app.ProcessName);
            }
        }

        if (app.Path is { } path)
        {
            var winPath = path.Replace('/', '\\'); // Linux paths match the same folder names
            if (AppCatalog.GameFolders.Any(f => winPath.Contains(f, StringComparison.OrdinalIgnoreCase)))
                return new Detection(AppCategory.Game, Pretty(app), app.ProcessName);
            // Unreal Engine games always ship as "<Name>-Win64-Shipping.exe" (or -WinGDK-/-EGS-).
            if (app.ProcessName.EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase))
                return new Detection(AppCategory.Game, Pretty(app), app.ProcessName);
        }

        if (settings.DetectFullscreenGames && !isTitleHost && IsExclusiveFullscreen())
            return new Detection(AppCategory.Game, Pretty(app), app.ProcessName);

        return Detection.Nothing;
    }

    private static string Pretty(ForegroundApp app) =>
        app.Title.Length is > 0 and < 60 ? app.Title : app.ProcessName.Replace("-Win64-Shipping", "", StringComparison.OrdinalIgnoreCase);
}
