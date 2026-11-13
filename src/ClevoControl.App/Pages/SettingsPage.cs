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
        root.Children.Add(Ui.PageSubtitle(""));

        var exe = Environment.ProcessPath ?? "";
        var s = new StackPanel();
        s.Children.Add(Ui.Switch(Loc.T("Mit Windows starten", "Start with Windows"), Autostart.IsEnabled, v =>
        {
            g.StartWithWindows = v;
            Autostart.Set(v, exe);
            Cc.Store.SaveSoon();
        }));
        s.Children.Add(Ui.Switch(Loc.T("Beim Autostart minimiert starten", "Start minimized on autostart"), g.StartMinimized, v => { g.StartMinimized = v; Cc.Store.SaveSoon(); }));
        s.Children.Add(Ui.Switch(Loc.T("Schließen minimiert in den Infobereich", "Closing minimizes to tray"), g.CloseToTray, v => { g.CloseToTray = v; Cc.Store.SaveSoon(); },
            Loc.T("Effekte, Lüfterkurve und Automatik laufen nur, solange die App läuft.", "Effects, fan curve and automation only run while the app is running.")));
        s.Children.Add(Ui.Switch(Loc.T("Tastenkürzel für Szenen (Strg+Alt+0…9)", "Scene hotkeys (Ctrl+Alt+0…9)"), g.SceneHotkeys, v => { g.SceneHotkeys = v; Cc.Store.SaveSoon(); },
            Loc.T("Wirkt nach Neustart der App", "Takes effect after restarting the app")));
        s.Children.Add(Ui.Switch(Loc.T("Lüfter beim Beenden auf Automatik", "Set fans to automatic on exit"), g.RestoreAutoFanOnExit, v => { g.RestoreAutoFanOnExit = v; Cc.Store.SaveSoon(); }));
        s.Children.Add(Ui.Row(Loc.T("Sprache", "Language"), Ui.Combo(new[] { ("auto", Loc.T("Automatisch", "Automatic")), ("de", "Deutsch"), ("en", "English") }, g.Language,
            l => { g.Language = l; Cc.Store.SaveSoon(); MessageBox.Show(Loc.T("Wird nach einem Neustart der App übernommen.", "Applied after restarting the app."), "ClevoControl"); }, 160)));
        root.Children.Add(Ui.Card(s, Loc.T("Allgemein", "General")));

        var hw = Cc.Hardware;
        var info = new StackPanel();
        info.Children.Add(Ui.Row(Loc.T("Hardware", "Hardware"), Ui.Text(hw.IsReal ? Loc.T("Clevo DCHU verbunden", "Clevo DCHU connected") : Loc.T("Simulation", "Simulation"), 13, true), bottom: 8));
        info.Children.Add(Ui.Row(Loc.T("Tastatur", "Keyboard"), Ui.Text(MainWindow.KeyboardTypeText(hw.KeyboardType), 13, true), bottom: 8));
        info.Children.Add(Ui.Text(hw.Description, 11.5, sub: true, bottom: 6));
        info.Children.Add(Ui.Text(Loc.T("Einstellungsdatei: ", "Settings file: ") + Cc.Store.FilePath, 11.5, sub: true, bottom: 10));
        info.Children.Add(Ui.Wrap(
            Ui.Button(Loc.T("Ordner öffnen", "Open folder"), () => Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(Cc.Store.FilePath)!)),
            Ui.Button("GitHub", () => Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true }))));
        root.Children.Add(Ui.Card(info, Loc.T("System", "System")));

        var about = Ui.Stack(
            Ui.Text($"ClevoControl {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)}", 14, true, bottom: 6),
            Ui.Text(Loc.T(
                "Diese Software wurde mit KI (Claude von Anthropic) generiert und auf einem Clevo N9x0TD_TF getestet. Sie ist ein inoffizielles Community-Projekt und steht in keiner Verbindung zu Clevo. Nutzung auf eigene Gefahr – siehe Lizenz (MIT).",
                "This software was generated with AI (Claude by Anthropic) and tested on a Clevo N9x0TD_TF. It is an unofficial community project and not affiliated with Clevo. Use at your own risk – see license (MIT)."), 12.5, sub: true, bottom: 6),
            Ui.Text(Loc.T("Benötigt den Clevo-Control-Center-Treiber (InsydeDCHU / AcpiBridge). Dieser wird nicht mitgeliefert, sondern vom installierten Treiber geladen.",
                "Requires the Clevo Control Center driver (InsydeDCHU / AcpiBridge). It is not bundled but loaded from the installed driver."), 12.5, sub: true));
        root.Children.Add(Ui.Card(about, Loc.T("Über", "About")));
        return root;
    }
}
