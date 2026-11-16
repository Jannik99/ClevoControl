using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.App.Pages;

public sealed class SettingsPage(ControlCenter cc) : AppPage(cc)
{
    public const string RepoUrl = "https://github.com/Jannik99/ClevoControl";

    public override UIElement Build()
    {
        var g = Cc.Settings.General;
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Einstellungen", "Settings")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("App-Verhalten und Start", "App behaviour and startup")));

        var exe = Environment.ProcessPath ?? "";
        var s = new StackPanel();
        s.Children.Add(Ui.Switch(Loc.T("Mit Windows starten", "Start with Windows"), Autostart.IsEnabled, v =>
        {
            g.StartWithWindows = v;
            Autostart.Set(v, exe);
            Cc.Store.SaveSoon();
        }));
        s.Children.Add(Ui.Switch(Loc.T("Beim Autostart minimiert starten", "Start minimized on autostart"), g.StartMinimized, v => { g.StartMinimized = v; Cc.Store.SaveSoon(); }));
        s.Children.Add(Ui.Switch(Loc.T("Beim Schließen im Infobereich weiterlaufen", "Keep running in the tray when closed"), g.CloseToTray, v => { g.CloseToTray = v; Cc.Store.SaveSoon(); },
            Loc.T("Effekte, Lüfterkurve und Automatik laufen nur, solange die App läuft.", "Effects, fan curve and automation only run while the app is running.")));
        s.Children.Add(Ui.Switch(Loc.T("Globale Tastenkürzel", "Global hotkeys"), g.SceneHotkeys, v => { g.SceneHotkeys = v; Cc.Store.SaveSoon(); },
            Loc.T("Strg+Alt+Umschalt+1…9: Szenen · +0: Tastaturlicht · +F: Lüfter voll. Wirkt sofort.",
                "Ctrl+Alt+Shift+1…9: scenes · +0: keyboard light · +F: fans full. Takes effect immediately.")));
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
                // Ask after the combo box has closed its drop-down.
                Application.Current.Dispatcher.BeginInvoke(AskRestart);
            }, 160)));
        root.Children.Add(Ui.Card(s, Loc.T("Allgemein", "General"), icon: Icons.Settings));
        root.Children.Add(VendorCard());
        return root;
    }

    // ------------------------------------------------------------------ vendor software at startup

    /// <summary>
    /// Card with the autostart entries of Clevo's own software (found by <see cref="VendorAutostart"/>), each
    /// with a toggle. Discovery and switching run off the UI thread; a failed or cancelled switch flips back.
    /// </summary>
    private static UIElement VendorCard()
    {
        VendorAutostart.Text = Loc.T;
        var body = new StackPanel();
        body.Children.Add(Ui.Text(Loc.T("Suche Autostart-Einträge …", "Looking for startup entries …"), 13, sub: true));
        var card = Ui.Card(body, Loc.T("Andere Hersteller-Software beim Start", "Vendor software at startup"),
            Loc.T("Verhindert, dass Clevos eigene Programme beim Anmelden starten und Lüfter oder Licht überschreiben. Jederzeit wieder einschaltbar.",
                "Keeps Clevo's own programs from starting at sign-in and overriding fans or light. You can turn them back on at any time."),
            icon: Icons.Power);
        Task.Run(() =>
        {
            IReadOnlyList<VendorStartupItem> items;
            IReadOnlyList<string> running;
            try { items = VendorAutostart.Discover(); }
            catch (Exception ex) { Log.Error("VendorAutostart", ex); items = []; }
            try { running = VendorAutostart.RunningControlApps(); }
            catch { running = []; }
            Application.Current?.Dispatcher.BeginInvoke(() => FillVendorCard(body, items, running));
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
            quit = Ui.Button(Loc.T("Clevo Control Center jetzt beenden", "Quit Clevo Control Center now"), () =>
            {
                if (MessageBox.Show(Loc.T("Clevo Control Center und die LED-Tastatur-App jetzt beenden? Ungespeicherte Einstellungen darin gehen verloren.",
                            "Quit Clevo Control Center and the LED keyboard app now? Unsaved settings in them are lost."),
                        "ClevoControl", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                VendorAutostart.QuitControlApps();
                quit!.Visibility = Visibility.Collapsed;
            }, icon: Icons.Delete);
            quit.HorizontalAlignment = HorizontalAlignment.Left;
            quit.Margin = new Thickness(0, 4, 0, 0);
            body.Children.Add(quit);
        }
        Ui.TrimBottom(body.Children.Count > 0 ? body.Children[^1] : null);
    }

    private static UIElement VendorRow(VendorStartupItem item)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var status = Ui.Text("", 12, sub: true);
        status.Visibility = Visibility.Collapsed;
        var current = item.Enabled;
        var reverting = false;
        CheckBox? toggle = null;
        toggle = Ui.Toggle(item.Enabled, v =>
        {
            if (reverting || v == current) return;
            toggle!.IsEnabled = false;
            status.Visibility = Visibility.Collapsed;
            Task.Run(() =>
            {
                var ok = VendorAutostart.SetEnabled(item, v, out var error);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    toggle.IsEnabled = true;
                    if (ok) { current = v; Log.Info($"Vendor autostart {item.Id} → {(v ? "on" : "off")}"); return; }
                    reverting = true;
                    toggle.IsChecked = current;
                    reverting = false;
                    status.Text = Loc.T("Nicht geändert: ", "Not changed: ") + (error ?? "?");
                    status.Visibility = Visibility.Visible;
                });
            });
        });
        box.Children.Add(Ui.Row(item.Name, toggle, item.Description + "\n" + VendorAutostart.KindLabel(item), bottom: 0));
        if (item.Warning is { } w)
        {
            var warn = Ui.Text(w, 12);
            warn.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
            warn.Margin = new Thickness(0, 4, 0, 0);
            box.Children.Add(warn);
        }
        box.Children.Add(status);
        return box;
    }

    private static void AskRestart()
    {
        if (!RestartDialog()) return;
        try
        {
            // Start the new process after this one has shut down and released the single-instance mutex.
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path.");
            var all = Environment.GetCommandLineArgs();
            var args = new List<string>();
            for (var i = 1; i < all.Length; i++)
            {
                if (all[i] == "--screenshots") { i++; continue; }
                args.Add(all[i]);
            }
            var line = string.Join(' ', new[] { exe }.Concat(args).Select(a => $"\"{a}\""));
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & start \"\" {line}") { CreateNoWindow = true, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("Restart", ex);
            MessageBox.Show(Loc.T("Die App konnte nicht neu gestartet werden. Bitte starte sie selbst neu.", "Could not restart the app. Please restart it yourself."), "ClevoControl");
            return;
        }
        ((App)Application.Current).ExitApp();
    }

    /// <summary>"Restart now" / "Later" dialog. Returns true for restart.</summary>
    private static bool RestartDialog()
    {
        var restart = false;
        var owner = Application.Current.MainWindow;
        var w = new Window
        {
            Title = "ClevoControl", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner is { IsVisible: true }) w.Owner = owner;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var yes = Ui.Button(Loc.T("Jetzt neu starten", "Restart now"), () => { restart = true; w.Close(); }, accent: true);
        var no = Ui.Button(Loc.T("Später", "Later"), w.Close);
        yes.MinWidth = no.MinWidth = 90;
        yes.IsDefault = true;
        no.IsCancel = true;
        row.Children.Add(yes);
        row.Children.Add(no);
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
        body.Children.Add(Ui.Text(Loc.T("Die Sprache wird nach einem Neustart der App übernommen.", "The language is applied after restarting the app.")));
        body.Children.Add(row);
        w.Content = body;
        w.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        w.ShowDialog();
        return restart;
    }
}
