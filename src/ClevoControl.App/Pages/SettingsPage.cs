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
            Loc.T("Strg+Alt+Umschalt+1…9: Szenen · +0: Tastaturlicht · +F: Lüfter voll. Wirkt nach einem Neustart der App.",
                "Ctrl+Alt+Shift+1…9: scenes · +0: keyboard light · +F: fans full. Takes effect after restarting the app.")));
        s.Children.Add(Ui.Switch(Loc.T("Beim Beenden Lüfter ans BIOS zurückgeben", "Hand fans back to the BIOS on exit"), g.RestoreAutoFanOnExit, v => { g.RestoreAutoFanOnExit = v; Cc.Store.SaveSoon(); },
            Loc.T("Empfohlen", "Recommended")));
        s.Children.Add(Ui.Row(Loc.T("Sprache", "Language"), Ui.Combo(new[] { ("auto", Loc.T("Automatisch", "Automatic")), ("de", "Deutsch"), ("en", "English") }, g.Language,
            l => { g.Language = l; Cc.Store.SaveSoon(); MessageBox.Show(Loc.T("Wird nach einem Neustart der App übernommen.", "Applied after restarting the app."), "ClevoControl"); }, 160)));
        root.Children.Add(Ui.Card(s, Loc.T("Allgemein", "General"), icon: Icons.Settings));
        return root;
    }
}
