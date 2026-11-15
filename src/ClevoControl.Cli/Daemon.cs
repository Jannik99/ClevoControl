using System.Runtime.InteropServices;
using ClevoControl.Core;
using ClevoControl.Core.Hardware;
using ClevoControl.Core.Settings;

/// <summary>
/// Headless mode: runs everything the desktop app runs in the background (lighting effects,
/// fan curve with safety net, performance profiles, AC/battery switching, OpenRGB server, local API)
/// from settings.json. Used by the Linux systemd service; also works on Windows.
/// <para>Signals (Linux): SIGTERM/SIGINT stop, SIGHUP reloads settings.json,
/// SIGUSR1 = about to sleep, SIGUSR2 = resumed (sent by contrib/linux/clevocontrol-sleep).</para>
/// </summary>
internal static class Daemon
{
    private const int SigUsr1 = 10, SigUsr2 = 12; // Linux x86-64 / arm64 numbers

    public static int Run(IClevoHardware hw)
    {
        Log.Info($"Daemon start: {hw.Description}");
        Console.WriteLine($"clevoctl daemon – {hw.Description}");
        Console.WriteLine($"Settings: {Path.Combine(SettingsStore.DefaultDirectory, "settings.json")}");
        if (!hw.IsReal) Console.Error.WriteLine("warning: no Clevo driver – running on simulated hardware");

        var stop = new ManualResetEventSlim();
        var gate = new object();
        var cc = Start(hw);

        void Reload()
        {
            lock (gate)
            {
                Log.Info("Daemon: reloading settings");
                cc.Dispose();
                cc = Start(hw);
            }
        }

        var registrations = new List<PosixSignalRegistration>
        {
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Set(); }),
            PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.Set(); }),
        };
        if (OperatingSystem.IsLinux())
        {
            registrations.Add(PosixSignalRegistration.Create(PosixSignal.SIGHUP, c => { c.Cancel = true; Reload(); }));
            registrations.Add(PosixSignalRegistration.Create((PosixSignal)SigUsr1, c => { c.Cancel = true; lock (gate) cc.OnSuspend(); }));
            registrations.Add(PosixSignalRegistration.Create((PosixSignal)SigUsr2, c => { c.Cancel = true; lock (gate) cc.OnResume(); }));
        }

        stop.Wait();
        Console.WriteLine("Stopping – fans go back to automatic if a custom curve was active.");
        lock (gate) cc.Dispose();
        foreach (var r in registrations) r.Dispose();
        Log.Info("Daemon stopped");
        return 0;
    }

    private static ControlCenter Start(IClevoHardware hw)
    {
        var cc = new ControlCenter(hw, new SettingsStore());
        if (cc.IntegrationError is { } e) Console.Error.WriteLine("warning: " + e);
        if (cc.Settings.Games.LocalApiEnabled) Console.WriteLine($"Local API on http://127.0.0.1:{cc.Settings.Games.LocalApiPort}/status");
        return cc;
    }
}
