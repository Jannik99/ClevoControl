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
        root.Children.Add(Ui.PageTitle(Loc.T("System", "System")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Alles, wonach in Support-Foren zuerst gefragt wird – mit einem Klick kopiert.",
            "Everything support forums ask for first – copied with one click.")));

        var items = SystemInfoService.Collect(Cc.Hardware);
        var list = new StackPanel();
        foreach (var i in items.Where(i => !i.Key.StartsWith("EC spec", StringComparison.Ordinal) && !i.Key.StartsWith("EC fan", StringComparison.Ordinal)))
        {
            var value = Ui.Text(i.Value, 13);
            value.TextAlignment = TextAlignment.Right;
            value.MaxWidth = 560;
            list.Children.Add(Ui.Row(i.Key, value, bottom: 8));
        }
        var copy = Ui.Button(Loc.T("Für Fehlerbericht kopieren", "Copy for bug report"), () =>
        {
            Clipboard.SetText("```\n" + SystemInfoService.AsText(items) + "```");
            MessageBox.Show(Loc.T("Kopiert. Füge den Text in dein GitHub-Issue oder Forenposting ein.", "Copied. Paste it into your GitHub issue or forum post."), "ClevoControl");
        }, accent: true, icon: Icons.Save);
        var issue = Ui.Button(Loc.T("Problem melden (GitHub)", "Report a problem (GitHub)"), () =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsPage.RepoUrl + "/issues/new") { UseShellExecute = true }), icon: Icons.OpenExternal);
        list.Children.Add(new Border { Height = 6 });
        list.Children.Add(Ui.Wrap(copy, issue));
        root.Children.Add(Ui.Card(list, Loc.T("Systeminformationen", "System information"), icon: Icons.Laptop));

        root.Children.Add(Ui.Section(Loc.T("Clevo-Software im Hintergrund", "Clevo background software")));
        root.Children.Add(Ui.Text(Loc.T("Clevos eigene Programme laufen weiter neben ClevoControl. Hier siehst du, was läuft. Die störenden Popups (Feststelltaste, Lautstärke …) kommen von „Fn hotkeys and OSD“.",
            "Clevo's own programs keep running next to ClevoControl. Here you see what is running. The annoying popups (caps lock, volume …) come from “Fn hotkeys and OSD”."), 12.5, sub: true, bottom: 10));
        _components = new StackPanel();
        root.Children.Add(_components);
        FillComponents();
        return root;
    }

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
