using System.Globalization;
using System.Text.RegularExpressions;

namespace ClevoControl.Core.Platform;

/// <summary>
/// Foreground window and process identity on Linux.
/// <list type="bullet">
/// <item>X11 (and XWayland games): <c>xprop</c> on _NET_ACTIVE_WINDOW.</item>
/// <item>KDE Wayland: <c>kdotool</c> if installed.</item>
/// <item>Otherwise: no foreground window – callers fall back to scanning running processes.</item>
/// </list>
/// Windows games under Wine/Proton are reported by their .exe name, so the app catalog matches them.
/// </summary>
public static partial class LinuxProcesses
{
    public sealed record Identity(int Pid, string Name, string? Path);

    /// <summary>Process name and path. For Wine/Proton processes the Windows .exe is used.</summary>
    public static Identity? Describe(int pid)
    {
        var cmdline = ReadCmdline(pid);
        string? exe = null;
        try { exe = new FileInfo(LinuxSys.P($"proc/{pid}/exe")).LinkTarget; } catch { }
        var comm = LinuxSys.ReadText($"proc/{pid}/comm");
        var (name, path) = Identify(cmdline, exe, comm);
        return name is null ? null : new Identity(pid, name, path);
    }

    /// <summary>Pure part of <see cref="Describe"/> (testable).</summary>
    public static (string? Name, string? Path) Identify(IReadOnlyList<string> cmdline, string? exe, string? comm)
    {
        // Wine / Proton: the first argument that ends in .exe is the Windows program.
        var winExe = cmdline.FirstOrDefault(a => a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (winExe is not null)
        {
            var file = winExe.Replace('\\', '/');
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            return (name, winExe.Replace('/', '\\'));
        }
        if (exe is not null) return (System.IO.Path.GetFileName(exe), exe);
        if (cmdline.Count > 0 && cmdline[0].Length > 0) return (System.IO.Path.GetFileName(cmdline[0]), cmdline[0]);
        return (comm, null);
    }

    private static List<string> ReadCmdline(int pid)
    {
        try
        {
            var raw = File.ReadAllText(LinuxSys.P($"proc/{pid}/cmdline"));
            return raw.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Short names (comm) of all running processes, any user.</summary>
    public static HashSet<string> AllNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var d in Directory.GetDirectories(LinuxSys.P("proc")))
                if (int.TryParse(System.IO.Path.GetFileName(d), out _) && LinuxSys.ReadText(System.IO.Path.GetRelativePath(LinuxSys.Root, d) + "/comm") is { } c) set.Add(c);
        }
        catch { }
        return set;
    }

    /// <summary>All processes of the current user (pid, identity).</summary>
    public static IEnumerable<Identity> All()
    {
        string[] dirs;
        try { dirs = Directory.GetDirectories(LinuxSys.P("proc")); }
        catch { yield break; }
        var uid = LinuxSys.ReadText("proc/self/status")?.Split('\n').FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal));
        foreach (var d in dirs)
        {
            if (!int.TryParse(System.IO.Path.GetFileName(d), out var pid)) continue;
            if (uid is not null && LinuxSys.ReadText($"proc/{pid}/status")?.Split('\n').FirstOrDefault(l => l.StartsWith("Uid:", StringComparison.Ordinal)) != uid) continue;
            if (Describe(pid) is { } id) yield return id;
        }
    }

    // ---- active window -------------------------------------------------------------------

    /// <summary>(pid, title) of the focused window, or null if the desktop does not tell.</summary>
    public static (int Pid, string Title)? ActiveWindow()
    {
        if (Shell.HasX11 && Shell.Has("xprop"))
        {
            var root = Shell.Run("xprop", "-root", "_NET_ACTIVE_WINDOW");
            if (ParseWindowId(root) is { } id && id != "0x0")
            {
                var props = Shell.Run("xprop", "-id", id, "_NET_WM_PID", "_NET_WM_NAME");
                if (ParseXpropWindow(props) is { } w) return w;
            }
        }
        if (Shell.Has("kdotool"))
        {
            var pid = Shell.Run("kdotool", "getactivewindow", "getwindowpid");
            var title = Shell.Run("kdotool", "getactivewindow", "getwindowname");
            if (int.TryParse(pid?.Trim(), out var p)) return (p, title?.Trim() ?? "");
        }
        return null;
    }

    /// <summary>"_NET_ACTIVE_WINDOW(WINDOW): window id # 0x3c00007" → "0x3c00007".</summary>
    public static string? ParseWindowId(string? output)
    {
        if (output is null) return null;
        var m = WindowIdRegex().Match(output);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Parses "_NET_WM_PID(CARDINAL) = 1234" and "_NET_WM_NAME(UTF8_STRING) = "title"".</summary>
    public static (int Pid, string Title)? ParseXpropWindow(string? output)
    {
        if (output is null) return null;
        var pid = PidRegex().Match(output);
        if (!pid.Success) return null;
        var title = TitleRegex().Match(output);
        return (int.Parse(pid.Groups[1].Value, CultureInfo.InvariantCulture), title.Success ? Regex.Unescape(title.Groups[1].Value) : "");
    }

    [GeneratedRegex(@"window id # (0x[0-9a-fA-F]+)")]
    private static partial Regex WindowIdRegex();

    [GeneratedRegex(@"_NET_WM_PID\(CARDINAL\) = (\d+)")]
    private static partial Regex PidRegex();

    [GeneratedRegex("_NET_WM_NAME\\([A-Z0-9_]+\\) = \"(.*)\"")]
    private static partial Regex TitleRegex();
}
