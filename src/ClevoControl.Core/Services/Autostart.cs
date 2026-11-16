using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ClevoControl.Core.Services;

/// <summary>
/// Per-user autostart without admin rights.
/// <para>
/// Windows: a scheduled task with a logon trigger for the current user. The classic Run key is
/// started by Explorer one app after another, often minutes after logging in (each slow app before
/// it may take 30 s), so ClevoControl seemed not to start at all. A logon task starts right away
/// (after a short delay for the drivers). The Run key stays as fallback if the task cannot be created.
/// </para>
/// <para>Linux: an XDG autostart entry in ~/.config/autostart.</para>
/// <para>
/// The entry stores the path of the exe that created it. <see cref="EnsureCurrent"/> moves it to the
/// running exe, so a new version in another folder does not keep starting an old one.
/// </para>
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ClevoControl";

    /// <summary>Arguments the autostart passes to the app.</summary>
    public const string Arguments = "--minimized";

    private static string TaskName => $"ClevoControl Autostart ({Environment.UserName})";

    private static string LinuxEntry =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", "clevocontrol.desktop");

    public static bool IsEnabled => RegisteredPath() is not null;

    /// <summary>The exe the autostart currently starts, or null if autostart is off.</summary>
    public static string? RegisteredPath()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return File.Exists(LinuxEntry) ? ParseDesktopExec(File.ReadAllText(LinuxEntry)) ?? "" : null;
            if (TaskCommand() is { } task) return task;
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string run ? ParseRunCommand(run) : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Autostart: could not read the current entry: " + ex.Message);
            return null;
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

        if (!enabled)
        {
            DeleteTask();
            DeleteRunValue();
            Log.Info("Autostart off");
            return;
        }

        if (CreateTask(exePath, out var error))
        {
            DeleteRunValue(); // older versions used the Run key
            Log.Info($"Autostart on (logon task) → {exePath}");
            return;
        }

        Log.Warn("Autostart: logon task not possible, using the Run key instead: " + error);
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        k.SetValue(Name, $"\"{exePath}\" {Arguments}");
    }

    /// <summary>
    /// Called at start: if autostart is on but points to another exe (older version, moved folder)
    /// or still uses the slow Run key, it is re-created for the running exe. Development builds
    /// (bin\Debug etc.) never take over the autostart.
    /// </summary>
    public static void EnsureCurrent(string? exePath)
    {
        if (exePath is null || IsDevelopmentBuild(exePath)) return;
        try
        {
            var current = RegisteredPath();
            if (current is null) return;
            var usesRunKey = OperatingSystem.IsWindows() && TaskCommand() is null;
            if (!usesRunKey && string.Equals(Path.GetFullPath(current), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase)) return;
            Log.Info($"Autostart pointed to {current}{(usesRunKey ? " (Run key)" : "")} – updating it to {exePath}");
            Set(true, exePath);
        }
        catch (Exception ex)
        {
            Log.Warn("Autostart: could not update the entry: " + ex.Message);
        }
    }

    internal static bool IsDevelopmentBuild(string exePath)
    {
        var p = exePath.Replace('\\', '/');
        return p.Contains("/bin/Debug/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/bin/Release/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/bin/x64/", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Windows: scheduled logon task -----------------------------------------------------

    /// <summary>Task Scheduler XML for a logon task of the current user (no admin rights needed).</summary>
    internal static string TaskXml(string exePath, string userId) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Starts ClevoControl when you sign in.</Description>
          </RegistrationInfo>
          <Triggers>
            <LogonTrigger>
              <Enabled>true</Enabled>
              <UserId>{SecurityElement.Escape(userId)}</UserId>
              <Delay>PT5S</Delay>
            </LogonTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <UserId>{SecurityElement.Escape(userId)}</UserId>
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>false</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>5</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exePath)}</Command>
              <Arguments>{Arguments}</Arguments>
              <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? "")}</WorkingDirectory>
            </Exec>
          </Actions>
        </Task>
        """;

    /// <summary>The &lt;Command&gt; of a task XML, unescaped.</summary>
    internal static string? ParseTaskCommand(string xml)
    {
        var m = Regex.Match(xml, @"<Command>(.*?)</Command>", RegexOptions.Singleline);
        if (!m.Success) return null;
        return m.Groups[1].Value.Trim().Trim('"')
            .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
    }

    /// <summary>"\"C:\x\ClevoControl.exe\" --minimized" → C:\x\ClevoControl.exe</summary>
    internal static string ParseRunCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? command[..(exe + 4)] : command;
    }

    /// <summary>Exec="/path/clevocontrol" --minimized → /path/clevocontrol</summary>
    internal static string? ParseDesktopExec(string entry)
    {
        var line = entry.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));
        if (line is null) return null;
        var exec = line[5..].Trim();
        if (exec.StartsWith('"')) return ParseRunCommand(exec);
        var space = exec.IndexOf(' ');
        return space > 0 ? exec[..space] : exec;
    }

    private static string? TaskCommand()
    {
        var (code, output) = Schtasks("/query", "/tn", TaskName, "/xml");
        return code == 0 ? ParseTaskCommand(output) : null;
    }

    private static bool CreateTask(string exePath, out string? error)
    {
        var file = Path.Combine(Path.GetTempPath(), $"clevocontrol-autostart-{Environment.ProcessId}.xml");
        try
        {
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            File.WriteAllText(file, TaskXml(exePath, user), Encoding.Unicode);
            var (code, output) = Schtasks("/create", "/tn", TaskName, "/xml", file, "/f");
            error = code == 0 ? null : output.Trim();
            return code == 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static void DeleteTask()
    {
        if (TaskCommand() is not null) Schtasks("/delete", "/tn", TaskName, "/f");
    }

    private static void DeleteRunValue()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        k?.DeleteValue(Name, false);
    }

    private static (int Code, string Output) Schtasks(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(15000))
        {
            try { p.Kill(); } catch { }
            return (-1, "schtasks timed out");
        }
        return (p.ExitCode, stdout.Result + stderr.Result);
    }

    // ---- Linux ------------------------------------------------------------------------------

    /// <summary>freedesktop .desktop entry for the app (menu entry and autostart).</summary>
    public static string LinuxDesktopEntry(string exePath, bool autostart) => $"""
        [Desktop Entry]
        Type=Application
        Name=ClevoControl
        GenericName=Laptop control center
        Comment=Keyboard lighting, fans and performance profiles for Clevo laptops
        Exec="{exePath}"{(autostart ? " " + Arguments : "")}
        Icon=clevocontrol
        Terminal=false
        Categories=System;Settings;HardwareSettings;
        Keywords=Clevo;keyboard;RGB;fan;TUXEDO;XMG;
        StartupWMClass=ClevoControl
        {(autostart ? "X-GNOME-Autostart-enabled=true\n" : "")}
        """;
}
