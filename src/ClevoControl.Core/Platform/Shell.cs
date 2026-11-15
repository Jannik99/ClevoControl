using System.Diagnostics;

namespace ClevoControl.Core.Platform;

/// <summary>Runs small command-line tools (pactl, xprop, powerprofilesctl …) with a timeout.</summary>
public static class Shell
{
    private static readonly Dictionary<string, string?> Found = new();

    /// <summary>Full path of a program on PATH, or null. Cached.</summary>
    public static string? Which(string program)
    {
        lock (Found)
        {
            if (Found.TryGetValue(program, out var cached)) return cached;
            string? result = null;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = Path.Combine(dir, program);
                if (File.Exists(p)) { result = p; break; }
            }
            Found[program] = result;
            return result;
        }
    }

    public static bool Has(string program) => Which(program) is not null;

    /// <summary>Runs the program and returns stdout, or null when it is missing, fails or times out.</summary>
    public static string? Run(string program, params string[] args) => Run(program, args, 2000);

    public static string? Run(string program, string[] args, int timeoutMs)
    {
        if (Which(program) is not { } path) return null;
        try
        {
            var psi = new ProcessStartInfo(path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.Environment["LC_ALL"] = "C"; // stable, parseable output
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            return p.ExitCode == 0 ? stdout.Result : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Starts a long-running process whose stdout is read as a binary stream (e.g. parec).</summary>
    public static Process? StartStream(string program, params string[] args)
    {
        if (Which(program) is not { } path) return null;
        try
        {
            var psi = new ProcessStartInfo(path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi);
            if (p is not null) _ = p.StandardError.ReadToEndAsync();
            return p;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Opens a file, folder or URL with the desktop's default application.</summary>
    public static void Open(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", [target]) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {target}: {ex.Message}");
        }
    }

    public static bool IsWayland =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is { Length: > 0 };

    public static bool HasX11 => Environment.GetEnvironmentVariable("DISPLAY") is { Length: > 0 };

    /// <summary>"KDE", "GNOME", "sway", … (XDG_CURRENT_DESKTOP), empty if unknown.</summary>
    public static string Desktop => Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
}
