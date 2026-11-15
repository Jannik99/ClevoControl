using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClevoControl.Core;
using ClevoControl.Core.Automation;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

/// <summary>Automatic scenes per app: games, videos/streaming, music and meetings.</summary>
public sealed class AutomationPage(ControlCenter cc) : AppPage(cc)
{
    private AutoProfileSettings A => Cc.Settings.AutoProfiles;
    private DispatcherTimer? _timer;

    public static string CategoryName(AppCategory c) => c switch
    {
        AppCategory.Game => Loc.T("Spiele", "Games"),
        AppCategory.Video => Loc.T("Filme, Serien & Videos", "Movies, series & videos"),
        AppCategory.Music => Loc.T("Musik", "Music"),
        AppCategory.Meeting => Loc.T("Anrufe & Meetings", "Calls & meetings"),
        _ => Loc.T("Nichts erkannt", "Nothing detected"),
    };

    private static string CategoryIcon(AppCategory c) => c switch
    {
        AppCategory.Game => Icons.Game,
        AppCategory.Video => Icons.Video,
        AppCategory.Music => Icons.Music,
        AppCategory.Meeting => Icons.Microphone,
        _ => Icons.Circle,
    };

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Automatik", "Automation")));
        root.Children.Add(Ui.PageSubtitle(Loc.T(
            "ClevoControl erkennt, was du gerade machst, und wechselt die Szene von selbst – zum Beispiel „Gaming“ beim Zocken oder „Film“ bei Netflix. Verlässt du die App, kommt alles wieder zurück.",
            "ClevoControl recognises what you are doing and switches the scene by itself – e.g. “Gaming” while playing or “Film” on Netflix. When you leave the app, everything goes back.")));

        var status = Ui.Text("", 13);
        var statusIcon = Ui.Icon(Icons.Circle, 20);
        var combos = new List<ComboBox>();
        root.Children.Add(Ui.SettingsCard(Icons.Sparkle, Loc.T("Szenen automatisch wechseln", "Switch scenes automatically"),
            Loc.T("Prüft alle 1,5 Sekunden das Fenster im Vordergrund. Es werden keine Daten gespeichert oder gesendet.", "Checks the foreground window every 1.5 seconds. Nothing is stored or sent."),
            Ui.Toggle(A.Enabled, v =>
            {
                A.Enabled = v;
                Cc.Store.SaveSoon();
                foreach (var c in combos) c.IsEnabled = v;
                Refresh();
            })));

        // Live status
        var statusRow = new DockPanel();
        statusIcon.Margin = new Thickness(2, 0, 18, 0);
        DockPanel.SetDock(statusIcon, Dock.Left);
        statusRow.Children.Add(statusIcon);
        statusRow.Children.Add(status);
        void Refresh()
        {
            var d = Cc.AutoProfiles.Current;
            if (!A.Enabled)
            {
                statusIcon.Text = Icons.Info;
                status.Text = Loc.T("Automatik ist aus – schalte sie oben ein.", "Automation is off – switch it on above.");
                return;
            }
            statusIcon.Text = CategoryIcon(d.Category);
            status.Text = d.Category == AppCategory.None
                ? Loc.T("Im Moment wird nichts Bekanntes verwendet.", "Nothing known is in use right now.")
                : Loc.T($"Erkannt: {d.AppName} ({CategoryName(d.Category)})", $"Detected: {d.AppName} ({CategoryName(d.Category)})")
                  + (Cc.AutoProfiles.ActiveScene is { } s ? Loc.T($" · Szene „{s}“ aktiv", $" · scene “{s}” active") : "");
        }
        Refresh();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        root.Children.Add(Ui.CardBorder(statusRow, new Thickness(0, 0, 0, 8), new Thickness(16, 14, 16, 14)));

        // Category → scene
        var map = new StackPanel();
        var sceneItems = new List<(string, string)> { ("", Loc.T("Nichts ändern", "Do nothing")) };
        sceneItems.AddRange(Cc.Settings.Scenes.Select(s => (s.Name, s.Name)));
        foreach (var c in new[] { AppCategory.Game, AppCategory.Video, AppCategory.Music, AppCategory.Meeting })
        {
            var cat = c;
            A.CategoryScenes.TryGetValue(c, out var current);
            var combo = Ui.Combo(sceneItems, current ?? "", v =>
            {
                if (string.IsNullOrEmpty(v)) A.CategoryScenes.Remove(cat);
                else A.CategoryScenes[cat] = v;
                Cc.Store.SaveSoon();
            }, 220);
            combo.IsEnabled = A.Enabled;
            combos.Add(combo);
            var count = AppCatalog.Apps.Count(a => a.Category == c);
            var desc = c switch
            {
                AppCategory.Game => Loc.T($"{count} bekannte Spiele plus automatisch alle aus Steam, Epic, GOG, EA, Ubisoft, Battle.net, Riot und Xbox sowie Unreal-Engine- und Vollbild-Spiele",
                    $"{count} known games plus automatically everything from Steam, Epic, GOG, EA, Ubisoft, Battle.net, Riot and Xbox, Unreal Engine and fullscreen games"),
                AppCategory.Video => Loc.T($"{count} Player und Streamingdienste – auch im Browser (Netflix, YouTube, Disney+, Prime Video, Twitch …)",
                    $"{count} players and streaming services – also in the browser (Netflix, YouTube, Disney+, Prime Video, Twitch …)"),
                AppCategory.Music => Loc.T($"{count} Musik-Apps und -Dienste (Spotify, Apple Music, TIDAL, YouTube Music …)", $"{count} music apps and services (Spotify, Apple Music, TIDAL, YouTube Music …)"),
                _ => Loc.T($"{count} Apps (Teams, Zoom, Discord, Meet …)", $"{count} apps (Teams, Zoom, Discord, Meet …)"),
            };
            map.Children.Add(Ui.SettingsCard(CategoryIcon(c), CategoryName(c), desc, combo));
        }
        root.Children.Add(Ui.Section(Loc.T("Welche Szene wann?", "Which scene when?")));
        root.Children.Add(map);

        // Options
        root.Children.Add(new Border { Height = 12 });
        root.Children.Add(Ui.SettingsCard(Icons.History, Loc.T("Danach zurückwechseln", "Switch back afterwards"),
            Loc.T("Stellt den vorherigen Zustand wieder her, sobald du die App verlässt.", "Restores the previous state as soon as you leave the app."),
            Ui.Toggle(A.RestoreOnLeave, v => { A.RestoreOnLeave = v; Cc.Store.SaveSoon(); })));
        root.Children.Add(Ui.SettingsCard(Icons.Max, Loc.T("Vollbild-Programme als Spiel behandeln", "Treat fullscreen programs as games"),
            Loc.T("Erkennt auch Spiele, die nicht im Katalog stehen.", "Also detects games that are not in the catalog."),
            Ui.Toggle(A.DetectFullscreenGames, v => { A.DetectFullscreenGames = v; Cc.Store.SaveSoon(); })));

        root.Children.Add(OwnRules());
        root.Children.Add(Catalog());
        return root;
    }

    public override void OnLeave() => _timer?.Stop();

    private readonly StackPanel _rules = new();
    private readonly StackPanel _rulesUndo = new();

    private void FillRules()
    {
        _rules.Children.Clear();
        foreach (var rule in A.CustomRules.ToList())
        {
            var r = rule;
            var del = Ui.IconButton(Icons.Delete, Loc.T("Löschen", "Delete"), () =>
            {
                var index = A.CustomRules.IndexOf(r);
                A.CustomRules.Remove(r);
                Cc.Store.SaveSoon();
                FillRules();
                _rulesUndo.Children.Clear();
                Ui.ShowUndo(_rulesUndo, Loc.T($"„{r.DisplayName ?? r.ProcessName}“ entfernt", $"“{r.DisplayName ?? r.ProcessName}” removed"), () =>
                {
                    A.CustomRules.Insert(Math.Clamp(index, 0, A.CustomRules.Count), r);
                    Cc.Store.SaveSoon();
                    FillRules();
                });
            });
            del.Margin = new Thickness(0);
            var what = r.SceneName is { Length: > 0 } scene ? Loc.T($"Szene „{scene}“", $"scene “{scene}”") : CategoryName(r.Category);
            _rules.Children.Add(Ui.Row($"{r.DisplayName ?? r.ProcessName}  →  {what}", del, r.ProcessName + ".exe", 8));
        }
    }

    /// <summary>Adds (or replaces) a rule for a process; returns the clean process name or null.</summary>
    private string? AddRule(string? text, AppCategory cat)
    {
        var n = (text ?? "").Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        if (n.Length == 0) return null;
        A.CustomRules.RemoveAll(x => string.Equals(x.ProcessName, n, StringComparison.OrdinalIgnoreCase));
        A.CustomRules.Add(new AppRule { ProcessName = n, Category = cat });
        Cc.Store.SaveSoon();
        FillRules();
        return n;
    }

    private Border OwnRules()
    {
        var s = new StackPanel();
        FillRules();
        s.Children.Add(_rules);
        s.Children.Add(_rulesUndo);

        // Add the current foreground app (or type a process name).
        var name = new TextBox { MinWidth = 200, ToolTip = Loc.T("Prozessname ohne .exe", "Process name without .exe") };
        System.Windows.Automation.AutomationProperties.SetName(name, Loc.T("Prozessname", "Process name"));
        var cat = AppCategory.Game;
        var catCombo = Ui.Combo(new[] { AppCategory.Game, AppCategory.Video, AppCategory.Music, AppCategory.Meeting }.Select(c => (c, CategoryName(c))), cat, c => cat = c, 200);
        var pick = Ui.Button(Loc.T("Zuletzt benutzte App übernehmen", "Use last app"), () =>
        {
            if (Cc.AutoProfiles.LastForeign is not { } p)
            {
                _rulesUndo.Children.Clear();
                _rulesUndo.Children.Add(Ui.InfoBar(Loc.T("Noch keine andere App erkannt. Starte sie, wechsle hierher und versuche es noch einmal.",
                    "No other app seen yet. Start it, switch back here and try again.")));
                return;
            }
            _rulesUndo.Children.Clear();
            if (AddRule(p, cat) is { } added)
                Ui.ShowUndo(_rulesUndo, Loc.T($"„{added}“ → {CategoryName(cat)} hinzugefügt", $"“{added}” → {CategoryName(cat)} added"), () =>
                {
                    A.CustomRules.RemoveAll(x => string.Equals(x.ProcessName, added, StringComparison.OrdinalIgnoreCase));
                    Cc.Store.SaveSoon();
                    FillRules();
                });
        }, icon: Icons.Refresh);
        var add = Ui.Button(Loc.T("Hinzufügen", "Add"), () =>
        {
            if (AddRule(name.Text, cat) is not null) name.Text = "";
        }, accent: true, icon: Icons.Add);
        var row = new WrapPanel();
        name.Margin = new Thickness(0, 0, 8, 8);
        catCombo.Margin = new Thickness(0, 0, 8, 8);
        row.Children.Add(ScenesPage.WithPlaceholder(name, Loc.T("Prozessname, z. B. obs64", "Process name, e.g. obs64")));
        row.Children.Add(catCombo);
        row.Children.Add(add);
        row.Children.Add(pick);
        s.Children.Add(row);
        s.Children.Add(Ui.Text(Loc.T("Tipp: Starte die App, wechsle hierher, wähle die Kategorie und klicke auf „Zuletzt benutzte App übernehmen“.",
            "Tip: start the app, switch here, choose the category and click “Use last app”."), 12, sub: true));
        return Ui.Card(s, Loc.T("Eigene Apps", "Your own apps"), Loc.T("Für Programme, die nicht erkannt werden.", "For programs that are not recognised."), Icons.Add);
    }

    private Border Catalog()
    {
        var search = new TextBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 10) };
        var list = new WrapPanel();
        void Fill()
        {
            list.Children.Clear();
            var q = search.Text.Trim();
            foreach (var app in AppCatalog.Apps.Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Category).ThenBy(a => a.Name))
            {
                var chip = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), BorderThickness = new Thickness(1) };
                chip.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
                chip.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = Ui.Icon(CategoryIcon(app.Category), 12, Ui.TextSecondary);
                ic.Margin = new Thickness(0, 0, 6, 0);
                sp.Children.Add(ic);
                sp.Children.Add(Ui.Text(app.Name, 12.5));
                chip.Child = sp;
                list.Children.Add(chip);
            }
        }
        search.TextChanged += (_, _) => Fill();
        Fill();
        var label = Ui.Text(Loc.T("Suchen", "Search"), 12, sub: true, bottom: 4);
        return Ui.Card(Ui.Stack(label, search, list), Loc.T($"Unterstützte Apps ({AppCatalog.Count})", $"Supported apps ({AppCatalog.Count})"),
            Loc.T("Dazu kommen automatisch alle Spiele aus den bekannten Spiele-Bibliotheken.", "Plus all games from the known game libraries, automatically."), Icons.Star);
    }
}
