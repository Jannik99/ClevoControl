using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClevoControl.Core;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

/// <summary>FlexiKey replacement: assign shortcuts, texts or programs to keys.</summary>
public sealed class KeysPage(ControlCenter cc) : AppPage(cc)
{
    private StackPanel? _list;
    private KeyRemapSettings R => Cc.Settings.KeyRemap;

    public static string ActionName(KeyActionType a) => a switch
    {
        KeyActionType.Shortcut => Loc.T("Tastenkombination", "Key combination"),
        KeyActionType.Text => Loc.T("Text eingeben", "Type text"),
        KeyActionType.Launch => Loc.T("Programm / Datei / URL starten", "Launch program / file / URL"),
        _ => Loc.T("Taste sperren", "Disable key"),
    };

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Tastenbelegung", "Key mapping")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Belege Tasten mit Tastenkombinationen, Texten oder Programmen – wie Clevos FlexiKey, nur schlanker. Funktioniert, solange ClevoControl läuft.",
            "Assign key combinations, texts or programs to keys – like Clevo's FlexiKey, just leaner. Works while ClevoControl is running.")));

        root.Children.Add(Ui.SettingsCard(Icons.Keyboard, Loc.T("Tastenbelegung aktiv", "Key mapping active"),
            Loc.T("Schaltet alle Belegungen auf einmal an oder aus.", "Turns all mappings on or off at once."),
            Ui.Toggle(R.Enabled, v => { R.Enabled = v; Cc.ApplyKeyRemap(); })));
        root.Children.Add(new Border { Height = 12 });

        root.Children.Add(Editor());
        root.Children.Add(Ui.Section(Loc.T("Belegungen", "Mappings")));
        _list = new StackPanel();
        root.Children.Add(_list);
        Fill();
        root.Children.Add(Ui.InfoBar(Loc.T("Tipp: Läuft Clevos FlexiKey parallel, können sich Belegungen doppeln. Die Windows-Taste lässt sich auf der Startseite komplett sperren.",
            "Tip: if Clevo's FlexiKey runs at the same time, mappings may overlap. The Windows key can be locked completely on the Home page.")));
        return root;
    }

    private Border Editor()
    {
        var mapping = new KeyMapping();
        var keyButton = new Button { Content = Loc.T("Taste drücken …", "Press a key …"), MinWidth = 180 };
        var capturing = false;
        keyButton.Click += (_, _) =>
        {
            capturing = true;
            keyButton.Content = Loc.T("Jetzt Taste drücken", "Press the key now");
            keyButton.Focus();
        };
        keyButton.PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            mapping.Key = KeyInterop.VirtualKeyFromKey(key);
            mapping.Ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            mapping.Alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            mapping.Shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            mapping.Win = Keyboard.Modifiers.HasFlag(ModifierKeys.Windows);
            keyButton.Content = KeyRemapService.Describe(mapping);
            capturing = false;
            e.Handled = true;
        };

        var value = new TextBox { MinWidth = 260 };
        var hint = Ui.Text("", 12, sub: true);
        var action = Ui.Combo(Enum.GetValues<KeyActionType>().Select(a => (a, ActionName(a))), KeyActionType.Shortcut, a =>
        {
            mapping.Action = a;
            value.IsEnabled = a != KeyActionType.Disable;
            hint.Text = Hint(a);
        }, 260);
        hint.Text = Hint(KeyActionType.Shortcut);

        var add = Ui.Button(Loc.T("Hinzufügen", "Add"), () =>
        {
            if (mapping.Key == 0) { MessageBox.Show(Loc.T("Bitte zuerst eine Taste wählen.", "Please choose a key first."), "ClevoControl"); return; }
            mapping.Value = value.Text.Trim();
            if (mapping.Action == KeyActionType.Shortcut && KeyRemapService.ParseShortcut(mapping.Value) is null)
            {
                MessageBox.Show(Loc.T("Die Tastenkombination wurde nicht erkannt. Beispiel: Ctrl+Shift+Esc", "The key combination was not recognised. Example: Ctrl+Shift+Esc"), "ClevoControl");
                return;
            }
            R.Mappings.RemoveAll(m => m.Key == mapping.Key && m.Ctrl == mapping.Ctrl && m.Alt == mapping.Alt && m.Shift == mapping.Shift && m.Win == mapping.Win);
            R.Mappings.Add(mapping.Clone());
            Cc.ApplyKeyRemap();
            mapping = new KeyMapping { Action = mapping.Action };
            keyButton.Content = Loc.T("Taste drücken …", "Press a key …");
            value.Text = "";
            Fill();
        }, accent: true, icon: Icons.Add);

        var s = new StackPanel();
        s.Children.Add(Ui.Row(Loc.T("Taste", "Key"), keyButton, Loc.T("Klicken, dann die gewünschte Taste (gern mit Strg/Alt/Umschalt) drücken", "Click, then press the key (optionally with Ctrl/Alt/Shift)")));
        s.Children.Add(Ui.Row(Loc.T("Aktion", "Action"), action));
        s.Children.Add(Ui.Row(Loc.T("Wert", "Value"), value));
        s.Children.Add(hint);
        s.Children.Add(new Border { Height = 10 });
        s.Children.Add(add);
        return Ui.Card(s, Loc.T("Neue Belegung", "New mapping"), icon: Icons.Add);
    }

    private static string Hint(KeyActionType a) => a switch
    {
        KeyActionType.Shortcut => Loc.T("Beispiele: Ctrl+Shift+Esc · Win+D · MediaPlayPause · VolumeMute · Alt+F4", "Examples: Ctrl+Shift+Esc · Win+D · MediaPlayPause · VolumeMute · Alt+F4"),
        KeyActionType.Text => Loc.T("Beliebiger Text, z. B. deine E-Mail-Signatur. Umlaute und Emojis funktionieren.", "Any text, e.g. your e-mail signature. Unicode and emojis work."),
        KeyActionType.Launch => Loc.T("Pfad zu einem Programm, einer Datei oder eine URL, z. B. calc.exe oder https://…", "Path to a program or file, or a URL, e.g. calc.exe or https://…"),
        _ => Loc.T("Die Taste macht nichts mehr (z. B. Feststelltaste oder Einfg).", "The key does nothing anymore (e.g. Caps Lock or Insert)."),
    };

    private void Fill()
    {
        _list!.Children.Clear();
        if (R.Mappings.Count == 0)
        {
            _list.Children.Add(Ui.Text(Loc.T("Noch keine Belegungen.", "No mappings yet."), sub: true, bottom: 12));
            return;
        }
        foreach (var m in R.Mappings.ToList())
        {
            var map = m;
            var desc = map.Action == KeyActionType.Disable ? ActionName(map.Action) : $"{ActionName(map.Action)}: {map.Value}";
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(Ui.Toggle(map.Enabled, v => { map.Enabled = v; Cc.ApplyKeyRemap(); }));
            var del = Ui.Button("", () => { R.Mappings.Remove(map); Cc.ApplyKeyRemap(); Fill(); }, icon: Icons.Delete);
            del.Margin = new Thickness(12, 0, 0, 0);
            del.ToolTip = Loc.T("Löschen", "Delete");
            controls.Children.Add(del);
            _list.Children.Add(Ui.SettingsCard(Icons.Keyboard, KeyRemapService.Describe(map), desc, controls));
        }
    }
}
