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
            else if (i.Key == "Support check") val = SupportText(val);
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

    /// <summary>
    /// German text for Core's support check ("Verdict: summary", English). Translated here by key so Core stays
    /// language-neutral; unknown summaries stay English. Keep in sync with <c>SupportCheck</c>.
    /// </summary>
    private static string SupportText(string value)
    {
        var split = value.IndexOf(": ", StringComparison.Ordinal);
        if (split < 0) return value;
        var verdict = value[..split] switch
        {
            "Supported" => Loc.T("Unterstützt", "Supported"),
            "Partial" => Loc.T("Teilweise", "Partial"),
            "NotSupported" => Loc.T("Nicht unterstützt", "Not supported"),
            "Unknown" => Loc.T("Unbekannt", "Unknown"),
            var v => v,
        };
        var summary = value[(split + 2)..];
        summary = summary switch
        {
            "Clevo driver (InsydeDCHU) found." => Loc.T("Clevo-Treiber (InsydeDCHU) gefunden.", summary),
            "Clevo driver (InsydeDCHU.dll) not found – install Clevo Control Center / your vendor's driver." =>
                Loc.T("Clevo-Treiber (InsydeDCHU.dll) nicht gefunden – installiere das Clevo Control Center bzw. den Treiber deines Herstellers.", summary),
            "TongFang/Uniwill laptop – not supported by ClevoControl (use TUXEDO Control Center or your vendor's tool)." =>
                Loc.T("TongFang-/Uniwill-Laptop – wird von ClevoControl nicht unterstützt (nutze das TUXEDO Control Center oder das Tool deines Herstellers).", summary),
            "System76 laptop with open firmware – use System76's own tools." =>
                Loc.T("System76-Laptop mit offener Firmware – nutze die Tools von System76.", summary),
            "No Clevo interface found – this does not look like a Clevo-based laptop." =>
                Loc.T("Keine Clevo-Schnittstelle gefunden – das scheint kein Clevo-basierter Laptop zu sein.", summary),
            "Clevo laptop, but the Linux driver is not active – install tuxedo-drivers (see docs/LINUX.md; newer CPUs on non-TUXEDO laptops need a build without the compatibility check)." =>
                Loc.T("Clevo-Laptop, aber der Linux-Treiber ist nicht aktiv – installiere tuxedo-drivers (siehe docs/LINUX.md; neuere CPUs in Nicht-TUXEDO-Laptops brauchen einen Build ohne Kompatibilitätsprüfung).", summary),
            "Clevo laptop – fans and profiles supported; per-key RGB keyboard lighting is not supported yet." =>
                Loc.T("Clevo-Laptop – Lüfter und Profile werden unterstützt, die Per-Key-RGB-Tastaturbeleuchtung noch nicht.", summary),
            "Clevo laptop with active driver – ClevoControl should work." =>
                Loc.T("Clevo-Laptop mit aktivem Treiber – ClevoControl sollte funktionieren.", summary),
            _ => summary,
        };
        return $"{verdict}: {summary}";
    }

    /// <summary>Component name and description from Core (English), translated by name.</summary>
    private static (string Name, string Description) ComponentText(ClevoComponent c) => c.Name switch
    {
        "Control Center Hotkey Service" => (c.Name, Loc.T("Hintergrunddienst, startet die Fn-Tasten-App und verarbeitet Energieereignisse.", c.Description)),
        "Fn hotkeys and OSD" => (c.Name, Loc.T("Fn-Tastenfunktionen und die Bildschirm-Popups (Feststelltaste, Lautstärke …).", c.Description)),
        "Control Center 3.0" => (c.Name, Loc.T("Clevos eigene Steuer-App.", c.Description)),
        "LED keyboard settings" => (c.Name, Loc.T("Clevos App für die Tastaturbeleuchtung.", c.Description)),
        "Flexikey" => (c.Name, Loc.T("Clevos Makro- und Tastenbelegungs-Tool.", c.Description)),
        "HotKey Clipboard Service" => (c.Name, Loc.T("Clevos Hilfsdienst für die Zwischenablage.", c.Description)),
        "TUXEDO Control Center service" => (Loc.T("TUXEDO-Control-Center-Dienst", c.Name),
            Loc.T("tccd steuert ebenfalls Lüfter, Profile und Tastatur und kommt ClevoControl in die Quere. Deaktivieren: sudo systemctl disable --now tccd tccd-sleep", c.Description)),
        "TUXEDO Control Center" => (c.Name, Loc.T("TUXEDOs eigene App (braucht tccd).", c.Description)),
        "ClevoControl service" => (Loc.T("ClevoControl-Dienst", c.Name),
            Loc.T("clevoctl-Daemon (systemd-Benutzerdienst). Die Desktop-App hält ihn an, solange sie läuft.", c.Description)),
        _ => (c.Name, c.Description),
    };

    /// <summary>What a running component can break; null when it does not compete with ClevoControl.</summary>
    private static string? ConflictText(ClevoComponent c) => !c.Running ? null : c.ProcessName switch
    {
        "ControlCenter30" or "tccd" => Loc.T("kann Lüfter/Licht überschreiben", "may override fans/lighting"),
        "LedKeyboardSetting" => Loc.T("kann das Licht überschreiben", "may override the lighting"),
        _ => null,
    };

    private void FillComponents()
    {
        _components!.Children.Clear();
        foreach (var c in SystemInfoService.ClevoComponents())
        {
            var comp = c;
            var conflict = ConflictText(comp);
            var state = Ui.Text(conflict is not null ? Loc.T("läuft – ", "running – ") + conflict
                : comp.Running ? Loc.T("läuft", "running") : Loc.T("läuft nicht", "not running"), 13, conflict is not null, sub: !comp.Running);
            if (conflict is not null) state.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
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
            var (name, description) = ComponentText(comp);
            _components.Children.Add(Ui.SettingsCard(conflict is not null ? Icons.Warning : comp.Running ? Icons.Auto : Icons.Power, name, description, control));
        }
    }
}
