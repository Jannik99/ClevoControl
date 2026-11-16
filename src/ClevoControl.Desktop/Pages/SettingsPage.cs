using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
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
        root.Children.Add(VendorCard());
        return root;
    }

    // ------------------------------------------------------------------ vendor software at startup

    /// <summary>
    /// Card with the autostart entries of Clevo's / TUXEDO's own software (found by <see cref="VendorAutostart"/>,
    /// which only looks for the entry kinds of the running OS), each with a toggle. Discovery and switching run
    /// off the UI thread; a failed or cancelled switch flips back.
    /// </summary>
    private static Control VendorCard()
    {
        VendorAutostart.Text = Loc.T;
        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.T("Suche Autostart-Einträge …", "Looking for startup entries …"), 13, sub: true));
        var subtitle = OperatingSystem.IsWindows()
            ? Loc.T("Verhindert, dass Clevos eigene Programme beim Anmelden starten und Lüfter oder Licht überschreiben. Jederzeit wieder einschaltbar.",
                "Keeps Clevo's own programs from starting at sign-in and overriding fans or light. You can turn them back on at any time.")
            : Loc.T("Verhindert, dass das TUXEDO Control Center beim Start läuft und Lüfter oder Licht überschreibt. Jederzeit wieder einschaltbar.",
                "Keeps TUXEDO Control Center from running at startup and overriding fans or light. You can turn it back on at any time.");
        var card = Ui.Card(body, Loc.T("Andere Hersteller-Software beim Start", "Vendor software at startup"), subtitle, icon: Icons.Power);
        Task.Run(() =>
        {
            IReadOnlyList<VendorStartupItem> items;
            IReadOnlyList<string> running;
            try { items = VendorAutostart.Discover(); }
            catch (Exception ex) { Log.Error("VendorAutostart", ex); items = []; }
            try { running = VendorAutostart.RunningControlApps(); }
            catch { running = []; }
            Dispatcher.UIThread.Post(() => FillVendorCard(body, items, running));
        });
        return card;
    }

    private static void FillVendorCard(StackPanel body, IReadOnlyList<VendorStartupItem> items, IReadOnlyList<string> running)
    {
        body.Children.Clear();
        if (items.Count == 0)
            body.Children.Add(Ui.Text(Loc.T("Keine Autostart-Einträge von Clevo- oder TUXEDO-Software gefunden.",
                "No startup entries of Clevo or TUXEDO software found."), 13, sub: true, bottom: running.Count > 0 ? 12 : 0));
        foreach (var item in items) body.Children.Add(VendorRow(item));
        if (running.Count > 0)
        {
            Button? quit = null;
            quit = Ui.Button(Loc.T("Clevo Control Center jetzt beenden", "Quit Clevo Control Center now"), async () =>
            {
                if (!await Ui.ConfirmAsync(Owner, Loc.T("Clevo Control Center und die LED-Tastatur-App jetzt beenden? Ungespeicherte Einstellungen darin gehen verloren.",
                        "Quit Clevo Control Center and the LED keyboard app now? Unsaved settings in them are lost."))) return;
                VendorAutostart.QuitControlApps();
                quit!.IsVisible = false;
            }, icon: Icons.Delete);
            quit.HorizontalAlignment = HorizontalAlignment.Left;
            quit.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(quit);
        }
        Ui.TrimBottom(body.Children.Count > 0 ? body.Children[^1] : null);
    }

    private static Control VendorRow(VendorStartupItem item)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var status = Ui.Text("", 12, sub: true);
        status.IsVisible = false;
        var current = item.Enabled;
        var reverting = false;
        ToggleSwitch? ts = null;
        var toggle = Ui.Toggle(item.Enabled, v =>
        {
            if (reverting || v == current || ts is null) return;
            ts.IsEnabled = false;
            status.IsVisible = false;
            Task.Run(() =>
            {
                var ok = VendorAutostart.SetEnabled(item, v, out var error);
                Dispatcher.UIThread.Post(() =>
                {
                    ts.IsEnabled = true;
                    if (ok) { current = v; Log.Info($"Vendor autostart {item.Id} → {(v ? "on" : "off")}"); return; }
                    reverting = true;
                    ts.IsChecked = current;
                    reverting = false;
                    status.Text = Loc.T("Nicht geändert: ", "Not changed: ") + (error ?? "?");
                    status.IsVisible = true;
                });
            });
        }, item.Name);
        ts = toggle.Tag as ToggleSwitch;
        box.Children.Add(Ui.Row(item.Name, toggle, item.Description + "\n" + VendorAutostart.KindLabel(item), bottom: 0));
        if (item.Warning is { } w)
        {
            var warn = Ui.SetRes(Ui.Text(w, 12), TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
            warn.Margin = new Thickness(0, 4, 0, 0);
            box.Children.Add(warn);
        }
        box.Children.Add(status);
        return box;
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
