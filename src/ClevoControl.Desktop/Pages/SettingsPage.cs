using System.Diagnostics;
using Avalonia.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.Desktop.Pages;

public sealed class SettingsPage(ControlCenter cc) : AppPage(cc)
{
    public const string RepoUrl = "https://github.com/Jannik99/ClevoControl";

    public override Control Build()
    {
        var g = Cc.Settings.General;
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Einstellungen", "Settings")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("App-Verhalten und Start", "App behaviour and startup")));

        var windows = OperatingSystem.IsWindows();
        var exe = Environment.ProcessPath ?? "";
        var s = new StackPanel();
        s.Children.Add(Ui.Switch(windows ? Loc.T("Mit Windows starten", "Start with Windows") : Loc.T("Mit dem System starten", "Start with the system"),
            Autostart.IsEnabled, v =>
            {
                g.StartWithWindows = v;
                try { Autostart.Set(v, exe); }
                catch (Exception ex)
                {
                    Log.Error("Autostart", ex);
                    _ = Ui.MessageAsync(Owner, Loc.T("Autostart konnte nicht geändert werden: ", "Could not change autostart: ") + ex.Message);
                }
                Cc.Store.SaveSoon();
            }));
        s.Children.Add(Ui.Switch(Loc.T("Beim Autostart minimiert starten", "Start minimized on autostart"), g.StartMinimized, v => { g.StartMinimized = v; Cc.Store.SaveSoon(); }));
        s.Children.Add(Ui.Switch(windows ? Loc.T("Beim Schließen im Infobereich weiterlaufen", "Keep running in the tray when closed")
                                         : Loc.T("Beim Schließen in der Systemleiste weiterlaufen", "Keep running in the panel when closed"),
            g.CloseToTray, v => { g.CloseToTray = v; Cc.Store.SaveSoon(); },
            Loc.T("Effekte, Lüfterkurve und Automatik laufen nur, solange die App läuft.", "Effects, fan curve and automation only run while the app is running.")));
        s.Children.Add(Ui.Switch(Loc.T("Globale Tastenkürzel", "Global hotkeys"), g.SceneHotkeys, v => { g.SceneHotkeys = v; Cc.Store.SaveSoon(); },
            Loc.T("Strg+Alt+Umschalt+1…9: Szenen · +0: Tastaturlicht · +F: Lüfter voll. Wirkt sofort.",
                "Ctrl+Alt+Shift+1…9: scenes · +0: keyboard light · +F: fans full. Takes effect immediately.")));
        if (!windows && Cc.KeyboardInputError is { Length: > 0 } inputError)
            s.Children.Add(Ui.InfoBar(Loc.T("Tastenkürzel brauchen Lesezugriff auf /dev/input: ", "Hotkeys need read access to /dev/input: ") + inputError, true));
        // RestoreAutoFanOnExit only matters for "Maximum": a custom curve is always handed back to the BIOS on exit
        // (ControlCenter.Dispose). The toggle shows the inverted value.
        s.Children.Add(Ui.Switch(Loc.T("Lüfter auf Maximum lassen, wenn die App beendet wird", "Keep fans at maximum when the app exits"), !g.RestoreAutoFanOnExit,
            v => { g.RestoreAutoFanOnExit = !v; Cc.Store.SaveSoon(); },
            Loc.T("Sonst übernimmt beim Beenden das BIOS. Eine eigene Kurve wird immer ans BIOS zurückgegeben.",
                "Otherwise the BIOS takes over on exit. A custom curve is always handed back to the BIOS.")));
        s.Children.Add(Ui.Row(Loc.T("Sprache", "Language"), Ui.Combo(new[] { ("auto", Loc.T("Automatisch", "Automatic")), ("de", "Deutsch"), ("en", "English") }, g.Language,
            l =>
            {
                g.Language = l;
                Cc.Store.SaveSoon();
                _ = AskRestartAsync();
            }, 160)));
        root.Children.Add(Ui.Card(s, Loc.T("Allgemein", "General"), icon: Icons.Settings));
        return root;
    }

    private static async Task AskRestartAsync()
    {
        var answer = await Ui.DialogAsync(Owner, Loc.T("Die Sprache wird nach einem Neustart der App übernommen.", "The language is applied after restarting the app."),
            "ClevoControl", [Loc.T("Jetzt neu starten", "Restart now"), Loc.T("Später", "Later")]);
        if (answer != 0) return;
        try { StartDelayed(); }
        catch (Exception ex)
        {
            Log.Error("Restart", ex);
            await Ui.MessageAsync(Owner, Loc.T("Die App konnte nicht neu gestartet werden. Bitte starte sie selbst neu.", "Could not restart the app. Please restart it yourself."));
            return;
        }
        App.Current.ExitApp();
    }

    /// <summary>
    /// Starts this executable again after a short delay through a detached shell, so the new process starts
    /// after this one has shut down and released the single-instance mutex.
    /// </summary>
    private static void StartDelayed()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
        // Keep options such as --simulate, but not a screenshot run.
        var args = new List<string>();
        for (var i = 0; i < App.Args.Length; i++)
        {
            if (App.Args[i] == "--screenshots") { i++; continue; }
            args.Add(App.Args[i]);
        }
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            var line = string.Join(' ', new[] { exe }.Concat(args).Select(a => $"\"{a}\""));
            psi = new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & start \"\" {line}") { CreateNoWindow = true, UseShellExecute = false };
        }
        else
        {
            psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("sleep 2; \"$0\" \"$@\" >/dev/null 2>&1 &");
            psi.ArgumentList.Add(exe);
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        Process.Start(psi)?.Dispose();
    }
}
