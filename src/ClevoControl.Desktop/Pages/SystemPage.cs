using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.Desktop.Pages;

/// <summary>System information for support requests and an overview of Clevo's background software.</summary>
public sealed class SystemPage(ControlCenter cc) : AppPage(cc)
{
    /// <summary>Linux: stops TUXEDO's daemon, which would fight with ClevoControl over fans and keyboard.</summary>
    private const string DisableTccdCommand = "sudo systemctl disable --now tccd tccd-sleep";

    private StackPanel? _components;

    public override Control Build()
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
        var copy = Ui.Button(Loc.T("Für Fehlerbericht kopieren", "Copy for bug report"), async () =>
        {
            if (await CopyAsync("```\n" + SystemInfoService.AsText(items) + "```"))
                await Ui.MessageAsync(Owner, Loc.T("Kopiert. Füge den Text in dein GitHub-Issue oder Forenposting ein.", "Copied. Paste it into your GitHub issue or forum post."));
        }, accent: true, icon: Icons.Save);
        var issue = Ui.Button(Loc.T("Problem melden (GitHub)", "Report a problem (GitHub)"), () => Ui.Open(SettingsPage.RepoUrl + "/issues/new"), icon: Icons.OpenExternal);
        list.Children.Add(new Border { Height = 6 });
        var log = Ui.Button(Loc.T("Logdatei öffnen", "Open log file"), () =>
        {
            if (File.Exists(Log.FilePath)) Ui.Open(Log.FilePath);
            else _ = Ui.MessageAsync(Owner, Loc.T("Noch keine Einträge.", "No entries yet."));
        }, icon: Icons.History);
        list.Children.Add(Ui.Wrap(copy, issue, log));
        root.Children.Add(Ui.Card(list, Loc.T("Systeminformationen", "System information"), icon: Icons.Laptop));

        root.Children.Add(Ui.Card(Ui.Stack(
            Ui.Text(Cc.Store.FilePath, 12, sub: true, bottom: 10),
            Ui.Wrap(Ui.Button(Loc.T("Ordner öffnen", "Open folder"), () => Ui.Open(Path.GetDirectoryName(Cc.Store.FilePath)!), icon: Icons.OpenExternal))),
            Loc.T("Einstellungsdatei", "Settings file"), icon: Icons.Save));

        if (OperatingSystem.IsWindows())
        {
            root.Children.Add(Ui.Section(Loc.T("Clevo-Software im Hintergrund", "Clevo background software")));
            root.Children.Add(Ui.Text(Loc.T("Clevos eigene Programme laufen weiter neben ClevoControl. Hier siehst du, was läuft. Die störenden Popups (Feststelltaste, Lautstärke …) kommen von „Fn hotkeys and OSD“.",
                "Clevo's own programs keep running next to ClevoControl. Here you see what is running. The annoying popups (caps lock, volume …) come from “Fn hotkeys and OSD”."), 12.5, sub: true, bottom: 10));
        }
        else
        {
            root.Children.Add(Ui.Section(Loc.T("TUXEDO-/Clevo-Software im Hintergrund", "TUXEDO / Clevo background software")));
            root.Children.Add(Ui.Text(Loc.T("Diese Programme steuern dieselbe Hardware wie ClevoControl. Läuft der TUXEDO-Dienst (tccd), stellt er Lüfter, Profile und Tastaturlicht immer wieder zurück.",
                "These programs control the same hardware as ClevoControl. If the TUXEDO service (tccd) is running, it keeps resetting fans, profiles and the keyboard light."), 12.5, sub: true, bottom: 10));
        }
        _components = new StackPanel();
        root.Children.Add(_components);
        FillComponents();

        var about = Ui.Stack(
            Ui.Text($"ClevoControl {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)}", 14, true, bottom: 6),
            Ui.Text(Loc.T(
                "Diese Software wurde mit KI (Claude von Anthropic) generiert und auf einem Clevo N9x0TD_TF getestet. Sie ist ein inoffizielles Community-Projekt und steht in keiner Verbindung zu Clevo. Nutzung auf eigene Gefahr – siehe Lizenz (MIT).",
                "This software was generated with AI (Claude by Anthropic) and tested on a Clevo N9x0TD_TF. It is an unofficial community project and not affiliated with Clevo. Use at your own risk – see license (MIT)."), 12.5, sub: true, bottom: 6),
            Ui.Text(OperatingSystem.IsWindows()
                ? Loc.T("Benötigt den Clevo-Control-Center-Treiber (InsydeDCHU / AcpiBridge). Dieser wird nicht mitgeliefert, sondern vom installierten Treiber geladen.",
                    "Requires the Clevo Control Center driver (InsydeDCHU / AcpiBridge). It is not bundled but loaded from the installed driver.")
                : Loc.T("Benötigt die Kernelmodule von tuxedo-drivers (clevo_acpi / clevo_wmi, tuxedo_io). Sie werden nicht mitgeliefert, sondern über deine Distribution installiert.",
                    "Requires the tuxedo-drivers kernel modules (clevo_acpi / clevo_wmi, tuxedo_io). They are not bundled but installed through your distribution."), 12.5, sub: true, bottom: 10),
            Ui.Wrap(Ui.Button("GitHub", () => Ui.Open(SettingsPage.RepoUrl), icon: Icons.OpenExternal)));
        var aboutCard = Ui.Card(about, Loc.T("Über ClevoControl", "About ClevoControl"), icon: Icons.Info);
        aboutCard.Margin = new Thickness(0, 16, 0, 8);
        root.Children.Add(aboutCard);
        return root;
    }

    /// <summary>Puts text on the clipboard; shows a message and returns false if that is not possible.</summary>
    private static async Task<bool> CopyAsync(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(Owner)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                return true;
            }
        }
        catch (Exception ex) { Log.Error("Clipboard", ex); }
        await Ui.MessageAsync(Owner, Loc.T("Die Zwischenablage ist nicht verfügbar.", "The clipboard is not available."));
        return false;
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
        "Linux" => "Linux",
        "Module" => Loc.T("Kernelmodul", "Kernel module"),
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
            Control control = state;
            if (comp.Running && comp.ProcessName is "FnKey" && OperatingSystem.IsWindows())
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                state.Margin = new Thickness(0, 0, 12, 0);
                state.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(state);
                panel.Children.Add(Ui.Button(Loc.T("Bis zur Abmeldung beenden", "End until sign-out"), async () =>
                {
                    var ok = await Ui.ConfirmAsync(Owner,
                        Loc.T("Damit verschwinden die Popups, aber auch Fn-Funktionen wie Helligkeit oder Flugmodus funktionieren bis zur nächsten Anmeldung nicht mehr. Fortfahren?",
                              "This removes the popups, but Fn functions such as brightness or airplane mode also stop working until the next sign-in. Continue?"));
                    if (ok)
                    {
                        SystemInfoService.StopProcess("FnKey");
                        FillComponents();
                    }
                }));
                control = panel;
            }
            else if (comp.Running && comp.ProcessName is "tccd")
            {
                // A system service: stopping it needs root, so show the command instead of a button.
                var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                state.HorizontalAlignment = HorizontalAlignment.Right;
                state.Margin = new Thickness(0, 0, 0, 6);
                panel.Children.Add(state);
                var cmd = new SelectableTextBlock { Text = DisableTccdCommand, FontFamily = new FontFamily("monospace"), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
                Ui.SetRes(cmd, TextBlock.ForegroundProperty, Ui.TextPrimary);
                ToolTip.SetTip(cmd, Loc.T("Im Terminal ausführen (braucht Root-Rechte), danach „Aktualisieren“.", "Run it in a terminal (needs root), then “Refresh”."));
                panel.Children.Add(cmd);
                var copy = Ui.Button(Loc.T("Befehl kopieren", "Copy command"), async () => await CopyAsync(DisableTccdCommand), icon: Icons.Code);
                var refresh = Ui.Button(Loc.T("Aktualisieren", "Refresh"), FillComponents, icon: Icons.Refresh);
                copy.Margin = refresh.Margin = new Thickness(8, 0, 0, 0);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                buttons.Children.Add(copy);
                buttons.Children.Add(refresh);
                panel.Children.Add(buttons);
                control = panel;
            }
            _components.Children.Add(Ui.SettingsCard(comp.Running ? Icons.Auto : Icons.Power, comp.Name, comp.Description, control));
        }
    }
}
