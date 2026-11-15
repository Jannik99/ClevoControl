using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClevoControl.Core;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class ScenesPage(ControlCenter cc) : AppPage(cc)
{
    private StackPanel? _list;
    private readonly StackPanel _undoHost = new();
    private Scene? _justApplied;
    private DispatcherTimer? _appliedTimer;

    /// <summary>Icons offered when saving a scene.</summary>
    private static readonly string[] IconChoices =
        [Icons.Star, Icons.Video, Icons.Game, Icons.Music, Icons.Briefcase, Icons.Moon, Icons.BatterySaver, Icons.Fire, Icons.Laptop, Icons.Heart];

    /// <summary>Scenes store an emoji or a glyph in Scene.Icon; map it to a Segoe Fluent glyph.</summary>
    public static string IconGlyph(string icon)
    {
        if (string.IsNullOrEmpty(icon)) return Icons.Star;
        if (icon.Length == 1 && icon[0] >= '\uE000') return icon;
        return icon switch
        {
            "🎬" => Icons.Video,
            "🎮" => Icons.Game,
            "🎵" or "🎧" => Icons.Music,
            "💼" => Icons.Briefcase,
            "🌙" => Icons.Moon,
            "🔋" => Icons.BatterySaver,
            "🔥" => Icons.Fire,
            "★" => Icons.Star,
            "☕" => "\uE7EC",
            _ => Icons.Star,
        };
    }

    public static string HotkeyText(int index) =>
        Loc.T($"Strg+Alt+Umschalt+{index}", $"Ctrl+Alt+Shift+{index}");

    /// <summary>"Statisch #FF008C, 100 %" – what the lighting part of a scene does.</summary>
    public static string LightText(KeyboardSettings kb, string separator = ", ") =>
        !kb.Enabled || kb.Effect == LightingEffectKind.Off ? Loc.T("aus", "off")
        : LightingPage.EffectName(kb.Effect) + (kb.Effect is LightingEffectKind.Static ? " " + kb.Color.ToHex() : "") + $"{separator}{kb.Brightness} %";

    /// <summary>"„Gaming“ wird von der Automatik für „Spiele“ verwendet."</summary>
    public static string UsageText(string sceneName, IEnumerable<AutoProfileSettings.SceneUsage> usages)
    {
        var where = usages.Select(u => u.Category is { } c ? $"„{AutomationPage.CategoryName(c)}“" : $"„{u.Rule!.DisplayName ?? u.Rule.ProcessName}“").ToList();
        var whereEn = where.Select(w => "“" + w[1..^1] + "”").ToList();
        static string Join(List<string> l, string and) => l.Count <= 1 ? string.Concat(l) : string.Join(", ", l.Take(l.Count - 1)) + and + l[^1];
        return Loc.T($"„{sceneName}“ wird von der Automatik für {Join(where, " und ")} verwendet.",
            $"“{sceneName}” is used by the automation for {Join(whereEn, " and ")}.");
    }

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Szenen", "Scenes")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Speichere Licht, Lüfter, Leistung und Lautstärke als Szene und wechsle per Klick oder über das Symbol im Infobereich.",
            "Save light, fans, performance and volume as a scene and switch with one click or from the tray icon.")));

        if (!Cc.Settings.General.SceneHotkeys)
            root.Children.Add(Ui.InfoBar(Loc.T("Tipp: Mit Tastenkürzeln schaltest du Szenen, ohne die App zu öffnen. Aktivieren unter Einstellungen → Globale Tastenkürzel.",
                "Tip: switch scenes without opening the app using hotkeys. Turn on in Settings → Global hotkeys.")));

        var save = Ui.Button(Loc.T("Szene speichern …", "Save scene …"), () => EditScene(-1), accent: true, icon: Icons.Add);
        root.Children.Add(Ui.Card(save, Loc.T("Neue Szene", "New scene"),
            Loc.T("Speichert die ausgewählten Teile des aktuellen Zustands: Beleuchtung, Leistungsprofil, Lüfter, Lautstärke.",
                "Saves the chosen parts of the current state: lighting, performance profile, fans, volume."), Icons.Add));

        root.Children.Add(_undoHost);
        _list = new StackPanel();
        root.Children.Add(_list);
        Fill();

        root.Children.Add(Ui.Button(Loc.T("Standardszenen wiederherstellen", "Restore default scenes"), () =>
        {
            if (!Confirm(Loc.T("Alle eigenen Szenen werden durch die Standardszenen ersetzt. Zuordnungen der Automatik zu Szenen, die es dann nicht mehr gibt, werden entfernt. Fortfahren?",
                "All your scenes will be replaced by the default scenes. Automation mappings to scenes that no longer exist are removed. Continue?"))) return;
            Cc.RestoreDefaultScenes(Loc.German);
            Fill();
        }));

        Cc.StatusChanged += OnStatusChanged;
        return root;
    }

    public override void OnLeave()
    {
        Cc.StatusChanged -= OnStatusChanged;
        _appliedTimer?.Stop();
    }

    private void OnStatusChanged() => Application.Current.Dispatcher.BeginInvoke(Fill);

    private static bool Confirm(string text) =>
        MessageBox.Show(text, "ClevoControl", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Fill()
    {
        if (_list is null) return;
        _list.Children.Clear();
        var scenes = Cc.Settings.Scenes;
        var hotkeys = Cc.Settings.General.SceneHotkeys;
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var idx = i;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Ui.Icon(IconGlyph(scene.Icon), 20);
            ic.Margin = new Thickness(2, 0, 16, 0);
            g.Children.Add(ic);

            var parts = new List<string>();
            if (scene.Keyboard is { } kb) parts.Add(kb.Enabled && kb.Effect != LightingEffectKind.Off ? LightText(kb, " · ") : Loc.T("Licht aus", "Light off"));
            if (scene.Profile is { } p) parts.Add(PowerPage.ProfileName(p));
            if (scene.FanMode is { } f) parts.Add(Loc.T("Lüfter: ", "Fans: ") + FansPage.ModeName(f));
            if (scene.Volume is { } v) parts.Add(Loc.T("Lautstärke ", "Volume ") + v + " %");

            var head = new WrapPanel();
            head.Children.Add(Ui.Text(scene.Name, 15, true));
            if (hotkeys && i < 9)
            {
                var hk = Ui.Text("  ·  " + HotkeyText(i + 1), 12.5, sub: true);
                hk.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(hk);
            }
            if (IsActive(scene)) head.Children.Add(ActiveBadge());
            var left = Ui.Stack(head, Ui.Text(string.Join("  ·  ", parts), 12.5, sub: true));
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(0, 0, 16, 0);
            Grid.SetColumn(left, 1);
            g.Children.Add(left);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var applied = ReferenceEquals(_justApplied, scene);
            var apply = Ui.Button(applied ? Loc.T("✓ Angewendet", "✓ Applied") : Loc.T("Anwenden", "Apply"), () => Apply(scene), accent: true);
            apply.MinWidth = 112;
            buttons.Children.Add(apply);
            // Up/down always take their space so the buttons of all rows line up.
            var up = Ui.IconButton(Icons.Up, Loc.T("Nach oben", "Move up"), () => { Cc.MoveScene(idx, -1); Fill(); });
            if (i == 0) up.Visibility = Visibility.Hidden;
            buttons.Children.Add(up);
            var down = Ui.IconButton("\uE74B", Loc.T("Nach unten", "Move down"), () => { Cc.MoveScene(idx, 1); Fill(); });
            if (i == scenes.Count - 1) down.Visibility = Visibility.Hidden;
            buttons.Children.Add(down);
            buttons.Children.Add(Ui.Button(Loc.T("Bearbeiten", "Edit"), () => EditScene(idx), icon: Icons.Edit));
            buttons.Children.Add(Ui.IconButton(Icons.Delete, Loc.T("Löschen", "Delete"), () => Delete(scene)));
            Grid.SetColumn(buttons, 2);
            g.Children.Add(buttons);
            _list.Children.Add(Ui.Card(g));
        }
    }

    private bool IsActive(Scene s) => string.Equals(Cc.ActiveScene, s.Name, StringComparison.OrdinalIgnoreCase);

    private static Border ActiveBadge()
    {
        var t = new TextBlock { Text = Loc.T("Aktiv", "Active"), FontSize = 11, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        var b = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(10, 0, 0, 0), Child = t, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, Ui.Accent);
        return b;
    }

    private void Apply(Scene scene)
    {
        Cc.ApplyScene(scene);
        // Short confirmation on the button itself.
        _justApplied = scene;
        _appliedTimer?.Stop();
        _appliedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _appliedTimer.Tick += (_, _) => { _appliedTimer.Stop(); _justApplied = null; Fill(); };
        _appliedTimer.Start();
        Fill();
    }

    /// <summary>Deletes at once with undo; asks first only when the automation uses the scene.</summary>
    private void Delete(Scene scene)
    {
        var uses = Cc.SceneUsages(scene);
        if (uses.Count > 0 && !Confirm(UsageText(scene.Name, uses) + Loc.T(" Die Zuordnung wird entfernt. Trotzdem löschen?", " The mapping will be removed. Delete anyway?"))) return;
        var removal = Cc.DeleteScene(scene);
        Fill();
        Ui.ShowUndo(_undoHost, Loc.T($"„{scene.Name}“ entfernt", $"“{scene.Name}” removed"), () => { Cc.RestoreScene(removal); Fill(); });
    }

    /// <summary>New scene (<paramref name="index"/> = -1) or edit an existing one.</summary>
    private void EditScene(int index)
    {
        var existing = index >= 0 ? Cc.Settings.Scenes[index] : null;
        if (SceneDialog(existing) is not { } r) return;
        var current = Cc.CaptureScene(r.Name, r.Icon);
        var scene = Scene.Compose(r.Name, r.Icon, r.Parts, current, existing, r.Recapture);
        if (existing is null) Cc.AddScene(scene);
        else Cc.ReplaceScene(index, scene);
        Fill();
    }

    private sealed record DialogResult(string Name, string Icon, SceneParts Parts, bool Recapture);

    /// <summary>"Save scene" / "Edit scene": name, icon and which parts the scene contains (with their values).</summary>
    private DialogResult? SceneDialog(Scene? existing)
    {
        var current = Cc.CaptureScene("", "");
        var owner = Application.Current.MainWindow;
        var title = existing is null ? Loc.T("Szene speichern", "Save scene") : Loc.T("Szene bearbeiten", "Edit scene");
        var w = new Window
        {
            Title = title, Width = 480, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        if (owner is { IsVisible: true }) w.Owner = owner;
        w.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        DialogResult? result = null;

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
        body.Children.Add(Ui.Text(title, 20, true, bottom: 16));

        // Name + icon
        body.Children.Add(Ui.Text(Loc.T("Name", "Name"), 13, bottom: 4));
        var name = new TextBox { Text = existing?.Name ?? "" };
        System.Windows.Automation.AutomationProperties.SetName(name, Loc.T("Name", "Name"));
        var icon = new ComboBox { Width = 70, Margin = new Thickness(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(icon, Loc.T("Symbol", "Icon"));
        foreach (var gl in IconChoices) icon.Items.Add(new ComboBoxItem { Content = Ui.Icon(gl, 16), Tag = gl });
        var glyph = existing is null ? Icons.Star : IconGlyph(existing.Icon);
        icon.SelectedIndex = Math.Max(0, Array.IndexOf(IconChoices, glyph));
        var nameRow = new DockPanel();
        DockPanel.SetDock(icon, Dock.Right);
        nameRow.Children.Add(icon);
        nameRow.Children.Add(WithPlaceholder(name, Loc.T("z. B. Abends", "e.g. Evening")));
        body.Children.Add(nameRow);
        var error = Ui.Text("", 12);
        error.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        error.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(error);

        // Parts
        var partsHead = Ui.Text(Loc.T("Was soll die Szene enthalten?", "What should the scene contain?"), 13, true, bottom: 8);
        partsHead.Margin = new Thickness(0, 14, 0, 8);
        body.Children.Add(partsHead);
        CheckBox? recapture = null;
        bool UseStored(SceneParts p) => existing is not null && recapture?.IsChecked != true && existing.Parts.HasFlag(p);
        Scene Source(SceneParts p) => UseStored(p) ? existing! : current;
        var parts = new (SceneParts Part, string Label, Func<Scene, string?> Value)[]
        {
            (SceneParts.Lighting, Loc.T("Beleuchtung", "Lighting"), s => s.Keyboard is { } kb ? LightText(kb) : null),
            (SceneParts.Profile, Loc.T("Leistungsprofil", "Performance profile"), s => s.Profile is { } p ? PowerPage.ProfileName(p) : null),
            (SceneParts.Fans, Loc.T("Lüftermodus", "Fan mode"), s => s.FanMode is { } f ? FansPage.ModeName(f) : null),
            (SceneParts.Volume, Loc.T("Lautstärke", "Volume"), s => s.Volume is { } v ? v + " %" : null),
        };
        var initial = existing?.Parts ?? (SceneParts.Lighting | SceneParts.Profile);
        var boxes = new List<(CheckBox Box, SceneParts Part, string Label, Func<Scene, string?> Value)>();
        foreach (var (part, label, value) in parts)
        {
            var cb = new CheckBox { IsChecked = initial.HasFlag(part), Margin = new Thickness(0, 0, 0, 8) };
            boxes.Add((cb, part, label, value));
            body.Children.Add(cb);
        }
        if (existing is not null)
        {
            recapture = new CheckBox
            {
                Content = Loc.T("Gespeicherte Werte durch den aktuellen Zustand ersetzen", "Replace stored values with the current state"),
                Margin = new Thickness(0, 8, 0, 0),
            };
            body.Children.Add(recapture);
        }

        var save = Ui.Button(Loc.T("Speichern", "Save"), () => { }, accent: true);
        var cancel = Ui.Button(Loc.T("Abbrechen", "Cancel"), w.Close);
        save.MinWidth = cancel.MinWidth = 90;
        save.IsDefault = true;
        cancel.IsCancel = true;

        void Update()
        {
            foreach (var (box, part, label, value) in boxes)
            {
                var v = value(Source(part));
                box.Content = v is null ? $"{label} ({Loc.T("nicht verfügbar", "not available")})" : $"{label} ({v})";
                box.IsEnabled = v is not null;
                if (v is null) box.IsChecked = false;
            }
            var chosen = boxes.Where(b => b.Box.IsChecked == true).Aggregate(SceneParts.None, (a, b) => a | b.Part);
            var problem = Scene.CheckName(Cc.Settings.Scenes, name.Text, existing);
            error.Text = problem == SceneNameProblem.Duplicate ? Loc.T("Es gibt schon eine Szene mit diesem Namen.", "A scene with this name already exists.")
                : chosen == SceneParts.None ? Loc.T("Wähle mindestens einen Teil aus.", "Choose at least one part.")
                : "";
            error.Visibility = error.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            save.IsEnabled = problem is null && chosen != SceneParts.None;
        }
        foreach (var b in boxes) { b.Box.Checked += (_, _) => Update(); b.Box.Unchecked += (_, _) => Update(); }
        if (recapture is not null) { recapture.Checked += (_, _) => Update(); recapture.Unchecked += (_, _) => Update(); }
        name.TextChanged += (_, _) => Update();
        save.Click += (_, _) =>
        {
            if (!save.IsEnabled) return;
            var ic = (icon.SelectedItem as ComboBoxItem)?.Tag as string ?? Icons.Star;
            // Keep the original icon (e.g. an emoji of a default scene) if the user did not pick another one.
            if (existing is not null && ic == IconGlyph(existing.Icon)) ic = existing.Icon;
            var chosen = boxes.Where(b => b.Box.IsChecked == true).Aggregate(SceneParts.None, (a, b) => a | b.Part);
            result = new DialogResult(name.Text.Trim(), ic, chosen, recapture?.IsChecked == true);
            w.Close();
        };
        Update();

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        row.Children.Add(save);
        row.Children.Add(cancel);
        body.Children.Add(row);
        w.Content = body;
        w.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        w.ShowDialog();
        return result;
    }

    /// <summary>TextBox with a grey hint text while it is empty (WPF has no PlaceholderText).</summary>
    public static Grid WithPlaceholder(TextBox box, string placeholder)
    {
        var hint = Ui.Text(placeholder, 14, sub: true);
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(11, 0, 0, 0);
        hint.Opacity = 0.8;
        void Sync() => hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();
        var g = new Grid { Margin = box.Margin };
        box.Margin = new Thickness(0);
        g.Children.Add(box);
        g.Children.Add(hint);
        return g;
    }
}
