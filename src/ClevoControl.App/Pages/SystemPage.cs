using System.Windows;
using System.Windows.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.App.Pages;

/// <summary>System information for support requests and an overview of Clevo's background software.</summary>
public sealed class SystemPage(ControlCenter cc) : AppPage(cc)
{
    private StackPanel? _components;

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Info & Support", "About & support")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Systeminfos für Fehlerberichte, Clevo-Hintergrundprogramme und Infos zur App.",
            "System info for bug reports, Clevo background software and about the app.")));

        var items = SystemInfoService.Collect(Cc.Hardware);
        var list = new StackPanel();
        foreach (var i in items.Where(i => !i.Key.StartsWith("EC spec", StringComparison.Ordinal) && !i.Key.StartsWith("EC fan", StringComparison.Ordinal)))
        {
            var val = i.Value;
            if (i.Key == "Keyboard type") val = MainWindow.KeyboardTypeText(Cc.Hardware.KeyboardType);
            else if (i.Key == "Driver" && !Cc.Hardware.IsReal) val = Loc.T("Demo-Modus (kein Clevo-Treiber gefunden)", "Demo mode (no Clevo driver found)");
            var value = Ui.Text(val, 13);
            value.TextAlignment = TextAlignment.Right;
            value.MaxWidth = 560;
            list.Children.Add(Ui.Row(KeyLabel(i.Key), value, bottom: 8));
        }
        var copy = Ui.Button(Loc.T("Für Fehlerbericht kopieren", "Copy for bug report"), () =>
        {
            Clipboard.SetText("```\n" + SystemInfoService.AsText(items) + "```");
            MessageBox.Show(Loc.T("Kopiert. Füge den Text in dein GitHub-Issue oder Forenposting ein.", "Copied. Paste it into your GitHub issue or forum post."), "ClevoControl");
        }, accent: true, icon: Icons.Save);
        var issue = Ui.Button(Loc.T("Problem melden (GitHub)", "Report a problem (GitHub)"), () =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsPage.RepoUrl + "/issues/new") { UseShellExecute = true }), icon: Icons.OpenExternal);
        list.Children.Add(new Border { Height = 6 });
        var log = Ui.Button(Loc.T("Logdatei öffnen", "Open log file"), () =>
        {
            if (System.IO.File.Exists(Log.FilePath)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
            else MessageBox.Show(Loc.T("Noch keine Einträge.", "No entries yet."), "ClevoControl");
        }, icon: Icons.History);
        list.Children.Add(Ui.Wrap(copy, issue, log));
        root.Children.Add(Ui.Card(list, Loc.T("Systeminformationen", "System information"), icon: Icons.Laptop));

        root.Children.Add(Ui.Card(Ui.Stack(
            Ui.Text(Cc.Store.FilePath, 12, sub: true, bottom: 10),
            Ui.Wrap(Ui.Button(Loc.T("Ordner öffnen", "Open folder"), () =>
                System.Diagnostics.Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(Cc.Store.FilePath)!), icon: Icons.OpenExternal))),
            Loc.T("Einstellungsdatei", "Settings file"), icon: Icons.Save));

        root.Children.Add(Ui.Section(Loc.T("Clevo-Software im Hintergrund", "Clevo background software")));
        root.Children.Add(Ui.Text(Loc.T("Clevos eigene Programme laufen weiter neben ClevoControl. Hier siehst du, was läuft. Die störenden Popups (Feststelltaste, Lautstärke …) kommen von „Fn hotkeys and OSD“.",
            "Clevo's own programs keep running next to ClevoControl. Here you see what is running. The annoying popups (caps lock, volume …) come from “Fn hotkeys and OSD”."), 12.5, sub: true, bottom: 10));
        _components = new StackPanel();
        root.Children.Add(_components);
        FillComponents();

        var about = Ui.Stack(
            Ui.Text($"ClevoControl {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)}", 14, true, bottom: 6),
            Ui.Text(Loc.T(
                "Diese Software wurde mit KI (Claude von Anthropic) generiert und auf einem Clevo N9x0TD_TF getestet. Sie ist ein inoffizielles Community-Projekt und steht in keiner Verbindung zu Clevo. Nutzung auf eigene Gefahr – siehe Lizenz (MIT).",
                "This software was generated with AI (Claude by Anthropic) and tested on a Clevo N9x0TD_TF. It is an unofficial community project and not affiliated with Clevo. Use at your own risk – see license (MIT)."), 12.5, sub: true, bottom: 6),
            Ui.Text(Loc.T("Benötigt den Clevo-Control-Center-Treiber (InsydeDCHU / AcpiBridge). Dieser wird nicht mitgeliefert, sondern vom installierten Treiber geladen.",
                "Requires the Clevo Control Center driver (InsydeDCHU / AcpiBridge). It is not bundled but loaded from the installed driver."), 12.5, sub: true, bottom: 10),
            Ui.Wrap(Ui.Button("GitHub", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsPage.RepoUrl) { UseShellExecute = true }), icon: Icons.OpenExternal)));
        var aboutCard = Ui.Card(about, Loc.T("Über ClevoControl", "About ClevoControl"), icon: Icons.Info);
        aboutCard.Margin = new Thickness(0, 16, 0, 8);
        root.Children.Add(aboutCard);
        return root;
    }

    private static string KeyLabel(string key) => key switch
    {
        "Model" => Loc.T("Modell", "Model"),
        "System manufacturer" => Loc.T("Hersteller", "Manufacturer"),
        "Mainboard" => "Mainboard",
        "BIOS" => Loc.T("BIOS-Version", "BIOS version"),
        "BIOS date" => Loc.T("BIOS-Datum", "BIOS date"),
        "EC firmware" => Loc.T("EC-Firmware", "EC firmware"),
        "CPU" => Loc.T("Prozessor", "Processor"),
        "GPU" => Loc.T("Grafik", "Graphics"),
        "RAM" => Loc.T("Arbeitsspeicher", "Memory"),
        "Windows" => "Windows",
        "Keyboard type" => Loc.T("Tastaturtyp", "Keyboard type"),
        "Driver" => Loc.T("Clevo-Treiber", "Clevo driver"),
        "Support check" => Loc.T("Unterstützung", "Support check"),
        "ClevoControl" => Loc.T("App-Version", "App version"),
        _ => key,
    };

    private void FillComponents()
    {
        _components!.Children.Clear();
        foreach (var c in SystemInfoService.ClevoComponents())
        {
            var comp = c;
            var state = Ui.Text(comp.Running ? Loc.T("läuft", "running") : Loc.T("läuft nicht", "not running"), 13, sub: !comp.Running);
            UIElement control = state;
            if (comp.Running && comp.ProcessName is "FnKey")
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                state.Margin = new Thickness(0, 0, 12, 0);
                state.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(state);
                panel.Children.Add(Ui.Button(Loc.T("Bis zur Abmeldung beenden", "End until sign-out"), () =>
                {
                    var ok = MessageBox.Show(
                        Loc.T("Damit verschwinden die Popups, aber auch Fn-Funktionen wie Helligkeit oder Flugmodus funktionieren bis zur nächsten Anmeldung nicht mehr. Fortfahren?",
                              "This removes the popups, but Fn functions such as brightness or airplane mode also stop working until the next sign-in. Continue?"),
                        "ClevoControl", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (ok == MessageBoxResult.Yes)
                    {
                        SystemInfoService.StopProcess("FnKey");
                        FillComponents();
                    }
                }));
                control = panel;
            }
            _components.Children.Add(Ui.SettingsCard(comp.Running ? Icons.Auto : Icons.Power, comp.Name, comp.Description, control));
        }
    }
}
