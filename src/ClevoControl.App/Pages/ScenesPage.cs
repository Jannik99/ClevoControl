using System.Windows;
using System.Windows.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

public sealed class ScenesPage(ControlCenter cc) : AppPage(cc)
{
    private StackPanel? _list;

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Szenen", "Scenes")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Speichere Licht, Lüfter, Leistung und Lautstärke als Szene und schalte sie per Klick, Tray-Menü oder Strg+Alt+Umschalt+1…9 um. Strg+Alt+Umschalt+0 schaltet das Tastaturlicht an/aus.",
            "Save light, fans, performance and volume as a scene and switch via click, tray menu or Ctrl+Alt+Shift+1…9. Ctrl+Alt+Shift+0 toggles the keyboard light.")));

        var name = new TextBox { Width = 220, Text = Loc.T("Meine Szene", "My scene") };
        var icon = Ui.Combo(new[] { "★", "🎬", "🎮", "🎵", "💼", "🌙", "🔋", "☕", "📚", "🎧", "🔥", "❄" }.Select(i => (i, i)), "★", _ => { }, 70);
        var save = Ui.Button(Loc.T("Aktuellen Zustand speichern", "Save current state"), () =>
        {
            var ic = (icon.SelectedItem as ComboBoxItem)?.Tag as string ?? "★";
            Cc.Settings.Scenes.Add(Cc.CaptureScene(name.Text.Trim().Length > 0 ? name.Text.Trim() : "Scene", ic));
            Cc.Store.SaveSoon();
            Fill();
        }, accent: true);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        name.Margin = new Thickness(0, 0, 8, 8);
        icon.Margin = new Thickness(0, 0, 8, 8);
        row.Children.Add(name);
        row.Children.Add(icon);
        row.Children.Add(save);
        root.Children.Add(Ui.Card(row, Loc.T("Neue Szene", "New scene")));

        _list = new StackPanel();
        root.Children.Add(_list);
        Fill();

        root.Children.Add(Ui.Button(Loc.T("Standardszenen wiederherstellen", "Restore default scenes"), () =>
        {
            Cc.Settings.Scenes = Scene.Defaults();
            Cc.Store.SaveSoon();
            Fill();
        }));
        return root;
    }

    private void Fill()
    {
        _list!.Children.Clear();
        var scenes = Cc.Settings.Scenes;
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var idx = i;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var parts = new List<string>();
            if (scene.Keyboard is { } kb) parts.Add(LightingPage.EffectName(kb.Effect) + (kb.Effect is LightingEffectKind.Static ? " " + kb.Color.ToHex() : "") + $" · {kb.Brightness} %");
            if (scene.Profile is { } p) parts.Add(PowerPage.ProfileName(p));
            if (scene.FanMode is { } f) parts.Add(Loc.T("Lüfter: ", "Fans: ") + FansPage.ModeName(f));
            if (scene.Volume is { } v) parts.Add(Loc.T("Lautstärke ", "Volume ") + v + " %");
            var left = Ui.Stack(Ui.Text($"{scene.Icon}  {scene.Name}" + (i < 9 ? $"   ·   Ctrl+Alt+Shift+{i + 1}" : ""), 15, true), Ui.Text(string.Join("  ·  ", parts), 12.5, sub: true));
            g.Children.Add(left);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(Ui.Button(Loc.T("Anwenden", "Apply"), () => Cc.ApplyScene(scene), accent: true));
            buttons.Children.Add(Ui.Button("↑", () => { if (idx > 0) { scenes.RemoveAt(idx); scenes.Insert(idx - 1, scene); Cc.Store.SaveSoon(); Fill(); } }));
            buttons.Children.Add(Ui.Button(Loc.T("Überschreiben", "Overwrite"), () =>
            {
                var cap = Cc.CaptureScene(scene.Name, scene.Icon);
                scenes[idx] = cap;
                Cc.Store.SaveSoon();
                Fill();
            }));
            buttons.Children.Add(Ui.Button("✕", () => { scenes.RemoveAt(idx); Cc.Store.SaveSoon(); Fill(); }));
            Grid.SetColumn(buttons, 1);
            g.Children.Add(buttons);
            _list.Children.Add(Ui.Card(g));
        }
    }
}
