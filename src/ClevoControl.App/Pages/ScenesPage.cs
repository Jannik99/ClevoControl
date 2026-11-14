using System.Windows;
using System.Windows.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class ScenesPage(ControlCenter cc) : AppPage(cc)
{
    private StackPanel? _list;

    /// <summary>Scenes store an emoji or a glyph in Scene.Icon; map it to a Segoe Fluent glyph.</summary>
    public static string IconGlyph(string icon)
    {
        if (string.IsNullOrEmpty(icon)) return Icons.Star;
        if (icon.Length == 1 && icon[0] >= '') return icon;
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
            "☕" => "",
            _ => Icons.Star,
        };
    }

    public static string HotkeyText(int index) =>
        Loc.T($"Strg+Alt+Umschalt+{index}", $"Ctrl+Alt+Shift+{index}");

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Szenen", "Scenes")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Speichere Licht, Lüfter, Leistung und Lautstärke als Szene und wechsle per Klick oder über das Symbol im Infobereich.",
            "Save light, fans, performance and volume as a scene and switch with one click or from the tray icon.")));

        if (!Cc.Settings.General.SceneHotkeys)
            root.Children.Add(Ui.InfoBar(Loc.T("Tipp: Mit Tastenkürzeln schaltest du Szenen, ohne die App zu öffnen. Aktivieren unter Einstellungen → Globale Tastenkürzel.",
                "Tip: switch scenes without opening the app using hotkeys. Turn on in Settings → Global hotkeys.")));

        var name = new TextBox { Width = 220 };
        var icon = new ComboBox { Width = 70 };
        foreach (var g in new[] { Icons.Star, Icons.Video, Icons.Game, Icons.Music, Icons.Briefcase, Icons.Moon, Icons.BatterySaver, Icons.Fire, Icons.Laptop, Icons.Heart })
            icon.Items.Add(new ComboBoxItem { Content = Ui.Icon(g, 16), Tag = g });
        icon.SelectedIndex = 0;
        var save = Ui.Button(Loc.T("Aktuellen Zustand speichern", "Save current state"), () =>
        {
            var ic = (icon.SelectedItem as ComboBoxItem)?.Tag as string ?? Icons.Star;
            var n = name.Text.Trim().Length > 0 ? name.Text.Trim() : Loc.T("Neue Szene", "New scene");
            Cc.Settings.Scenes.Add(Cc.CaptureScene(n, ic));
            Cc.Store.SaveSoon();
            name.Text = "";
            Fill();
        }, accent: true);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        name.Margin = new Thickness(0, 0, 8, 8);
        icon.Margin = new Thickness(0, 0, 8, 8);
        row.Children.Add(name);
        row.Children.Add(icon);
        row.Children.Add(save);
        root.Children.Add(Ui.Card(row, Loc.T("Neue Szene", "New scene"), icon: Icons.Add));

        _list = new StackPanel();
        root.Children.Add(_list);
        Fill();

        root.Children.Add(Ui.Button(Loc.T("Standardszenen wiederherstellen", "Restore default scenes"), () =>
        {
            if (!Confirm(Loc.T("Alle eigenen Szenen werden durch die Standardszenen ersetzt. Fortfahren?",
                "All your scenes will be replaced by the default scenes. Continue?"))) return;
            Cc.Settings.Scenes = Scene.Defaults();
            Cc.Store.SaveSoon();
            Fill();
        }));
        return root;
    }

    private static bool Confirm(string text) =>
        MessageBox.Show(text, "ClevoControl", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Fill()
    {
        _list!.Children.Clear();
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
            if (scene.Keyboard is { } kb) parts.Add(LightingPage.EffectName(kb.Effect) + (kb.Effect is LightingEffectKind.Static ? " " + kb.Color.ToHex() : "") + $" · {kb.Brightness} %");
            if (scene.Profile is { } p) parts.Add(PowerPage.ProfileName(p));
            if (scene.FanMode is { } f) parts.Add(Loc.T("Lüfter: ", "Fans: ") + FansPage.ModeName(f));
            if (scene.Volume is { } v) parts.Add(Loc.T("Lautstärke ", "Volume ") + v + " %");
            var title = scene.Name + (hotkeys && i < 9 ? "  ·  " + HotkeyText(i + 1) : "");
            var left = Ui.Stack(Ui.Text(title, 15, true), Ui.Text(string.Join("  ·  ", parts), 12.5, sub: true));
            left.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(left, 1);
            g.Children.Add(left);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Ui.Button(Loc.T("Anwenden", "Apply"), () => Cc.ApplyScene(scene), accent: true));
            if (i > 0)
            {
                var up = Ui.Button("", () => { if (idx > 0) { scenes.RemoveAt(idx); scenes.Insert(idx - 1, scene); Cc.Store.SaveSoon(); Fill(); } }, icon: Icons.Up);
                up.ToolTip = Loc.T("Nach oben", "Move up");
                buttons.Children.Add(up);
            }
            buttons.Children.Add(Ui.Button(Loc.T("Mit aktuellem Stand überschreiben", "Overwrite with current state"), () =>
            {
                if (!Confirm(Loc.T($"„{scene.Name}“ mit den aktuellen Einstellungen überschreiben?",
                    $"Overwrite “{scene.Name}” with the current settings?"))) return;
                scenes[idx] = Cc.CaptureScene(scene.Name, scene.Icon);
                Cc.Store.SaveSoon();
                Fill();
            }));
            var del = Ui.Button("", () =>
            {
                if (!Confirm(Loc.T($"Szene „{scene.Name}“ löschen?", $"Delete scene “{scene.Name}”?"))) return;
                scenes.RemoveAt(idx);
                Cc.Store.SaveSoon();
                Fill();
            }, icon: Icons.Delete);
            del.ToolTip = Loc.T("Löschen", "Delete");
            buttons.Children.Add(del);
            Grid.SetColumn(buttons, 2);
            g.Children.Add(buttons);
            _list.Children.Add(Ui.Card(g));
        }
    }
}
