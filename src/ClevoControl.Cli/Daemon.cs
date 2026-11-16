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

    private static PosixSignalRegistration[]? s_ignored;

    /// <summary>
    /// One-shot commands: SIGUSR1/2 are meant for the daemon (sleep hook), and their default action would
    /// terminate a command that happens to run at that moment. Kept in a static field so the registrations live on.
    /// </summary>
    public static void IgnoreSleepSignals()
    {
        if (!OperatingSystem.IsLinux() || s_ignored is not null) return;
        try
        {
            s_ignored =
            [
                PosixSignalRegistration.Create((PosixSignal)SigUsr1, c => c.Cancel = true),
                PosixSignalRegistration.Create((PosixSignal)SigUsr2, c => c.Cancel = true),
            ];
        }
        catch (Exception ex)
        {
            Log.Warn("Could not ignore SIGUSR1/2: " + ex.Message);
        }
    }

    public static int Run(IClevoHardware hw)
    {
        Log.Info($"Daemon start: {hw.Description}");
        Console.WriteLine($"clevoctl daemon – {hw.Description}");
        Console.WriteLine($"Settings: {Path.Combine(SettingsStore.DefaultDirectory, "settings.json")}");
        if (!hw.IsReal) Console.Error.WriteLine("warning: no Clevo driver – running on simulated hardware");

        var stop = new ManualResetEventSlim();
        var gate = new object();
        var cc = Start(hw);
        // Between SIGUSR1 and SIGUSR2 (+ settle delay) the EC must not be touched, and a new ControlCenter
        // would write it right away. A reload asked for meanwhile waits until the EC is usable again.
        var suspended = false;
        var reloadPending = false;
        Timer? reloadTimer = null;

        void ReloadNow()
        {
            Log.Info("Daemon: reloading settings");
            // Without saving: settings.json was just edited by hand and the old state must not overwrite it.
            cc.Dispose(saveSettings: false);
            cc = Start(hw);
        }

        void Reload()
        {
            lock (gate)
            {
                if (suspended || reloadTimer is not null)
                {
                    Log.Info("Daemon: reload deferred until the resume settle delay has passed");
                    reloadPending = true;
                    return;
                }
                ReloadNow();
            }
        }

        void Suspend()
        {
            lock (gate)
            {
                suspended = true;
                reloadTimer?.Dispose();
                reloadTimer = null;
                cc.OnSuspend();
            }
        }

        void Resume()
        {
            lock (gate)
            {
                suspended = false;
                if (!reloadPending)
                {
                    cc.OnResume();
                    return;
                }
                // The old instance stays paused; the new one starts once the EC has settled.
                reloadPending = false;
                reloadTimer?.Dispose();
                Timer? timer = null;
                timer = new Timer(_ =>
                {
                    lock (gate)
                    {
                        if (!ReferenceEquals(reloadTimer, timer)) return;
                        reloadTimer = null;
                        timer!.Dispose();
                        ReloadNow();
                    }
                }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                reloadTimer = timer;
                timer.Change(ControlCenter.ResumeSettle, Timeout.InfiniteTimeSpan);
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
            registrations.Add(PosixSignalRegistration.Create((PosixSignal)SigUsr1, c => { c.Cancel = true; Suspend(); }));
            registrations.Add(PosixSignalRegistration.Create((PosixSignal)SigUsr2, c => { c.Cancel = true; Resume(); }));
        }

        stop.Wait();
        Console.WriteLine("Stopping – fans go back to automatic if a custom curve was active.");
        lock (gate)
        {
            reloadTimer?.Dispose();
            reloadTimer = null;
            cc.Dispose();
        }
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
