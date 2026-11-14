using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClevoControl.App.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Pages;

/// <summary>
/// Key mapping (FlexiKey replacement) with a clickable virtual keyboard: pick a key, choose what it
/// should do, done. Mapped keys light up in the accent color.
/// </summary>
public sealed class KeysPage(ControlCenter cc) : AppPage(cc)
{
    private KeyRemapSettings R => Cc.Settings.KeyRemap;
    private VirtualKeyboard? _keyboard;
    private ContentControl? _editor;
    private StackPanel? _list;

    public static string ActionName(KeyActionType a) => a switch
    {
        KeyActionType.Shortcut => Loc.T("Tastenkombination senden", "Send key combination"),
        KeyActionType.Text => Loc.T("Text schreiben", "Type text"),
        KeyActionType.Launch => Loc.T("Programm oder Link öffnen", "Open program or link"),
        _ => Loc.T("Taste sperren", "Disable key"),
    };

    private static string ActionIcon(KeyActionType a) => a switch
    {
        KeyActionType.Shortcut => Icons.Keyboard,
        KeyActionType.Text => Icons.Edit,
        KeyActionType.Launch => Icons.OpenExternal,
        _ => Icons.Power,
    };

    /// <summary>"Strg+Umschalt+F5" style name of a mapping's trigger, using the German key labels.</summary>
    public static string TriggerName(KeyMapping m)
    {
        var parts = new List<string>();
        if (m.Ctrl) parts.Add(Loc.T("Strg", "Ctrl"));
        if (m.Alt) parts.Add("Alt");
        if (m.Shift) parts.Add(Loc.T("Umschalt", "Shift"));
        if (m.Win) parts.Add("Win");
        parts.Add(VirtualKeyboard.LabelFor(m.Key));
        return string.Join(" + ", parts);
    }

    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Tastenbelegung", "Key mapping")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Klicke auf eine Taste und lege fest, was sie tun soll – eine Tastenkombination, ein Text, ein Programm oder gar nichts.",
            "Click a key and choose what it should do – a key combination, a text, a program or nothing at all.")));

        root.Children.Add(Ui.SettingsCard(Icons.Keyboard, Loc.T("Tastenbelegung aktiv", "Key mapping active"),
            Loc.T("Gilt, solange ClevoControl läuft. Schaltet alle Belegungen auf einmal an oder aus.", "Works while ClevoControl is running. Turns all mappings on or off at once."),
            Ui.Toggle(R.Enabled, v => { R.Enabled = v; Cc.ApplyKeyRemap(); })));
        root.Children.Add(new Border { Height = 8 });

        _keyboard = new VirtualKeyboard(R.ShowNumpad) { Margin = new Thickness(0, 0, 0, 12) };
        _keyboard.KeySelected += (vk, _) => ShowEditor(vk);
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0) };
        dot.SetResourceReference(Border.BackgroundProperty, Ui.Accent);
        legend.Children.Add(dot);
        legend.Children.Add(Ui.Text(Loc.T("belegt", "mapped"), 12, sub: true));
        var numpad = new CheckBox { Content = Loc.T("Ziffernblock anzeigen", "Show number pad"), IsChecked = R.ShowNumpad, Margin = new Thickness(24, 0, 0, 0) };
        numpad.Checked += (_, _) => { R.ShowNumpad = true; Cc.Store.SaveSoon(); Reload(); };
        numpad.Unchecked += (_, _) => { R.ShowNumpad = false; Cc.Store.SaveSoon(); Reload(); };
        legend.Children.Add(numpad);
        root.Children.Add(Ui.Card(Ui.Stack(_keyboard, legend), Loc.T("Taste wählen", "Choose a key"), icon: Icons.Keyboard));

        _editor = new ContentControl { Content = Ui.Card(Ui.Text(Loc.T("Wähle oben eine Taste aus.", "Choose a key above."), sub: true)) };
        root.Children.Add(_editor);

        root.Children.Add(Ui.Section(Loc.T("Alle Belegungen", "All mappings")));
        _list = new StackPanel();
        root.Children.Add(_list);
        Fill();
        return root;
    }

    private static void Reload() => ((MainWindow)Application.Current.MainWindow!).Navigate("keys");

    private void ShowEditor(int vk)
    {
        // Edit the first existing mapping of this key (any modifiers) or start a new one.
        var existing = R.Mappings.FirstOrDefault(m => m.Key == vk);
        var m = existing?.Clone() ?? new KeyMapping { Key = vk };
        var original = existing;

        var s = new StackPanel();

        // Modifiers ("only together with")
        var mods = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        CheckBox Mod(string label, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 18, 0) };
            c.Checked += (_, _) => set(true);
            c.Unchecked += (_, _) => set(false);
            return c;
        }
        mods.Children.Add(Mod(Loc.T("Strg", "Ctrl"), m.Ctrl, v => m.Ctrl = v));
        mods.Children.Add(Mod("Alt", m.Alt, v => m.Alt = v));
        mods.Children.Add(Mod(Loc.T("Umschalt", "Shift"), m.Shift, v => m.Shift = v));
        mods.Children.Add(Mod("Win", m.Win, v => m.Win = v));
        s.Children.Add(Ui.Text(Loc.T("Nur zusammen mit (optional)", "Only together with (optional)"), 13, true, bottom: 4));
        s.Children.Add(mods);

        // Action tiles
        s.Children.Add(Ui.Text(Loc.T("Was soll passieren?", "What should happen?"), 13, true, bottom: 6));
        var valueHost = new StackPanel();
        var actions = new WrapPanel();
        foreach (var a in Enum.GetValues<KeyActionType>())
        {
            var act = a;
            actions.Children.Add(Ui.Tile(ActionIcon(a), ActionName(a), null, m.Action == a, "keyaction", () =>
            {
                m.Action = act;
                valueHost.Children.Clear();
                valueHost.Children.Add(ValueEditor(m));
            }, 210));
        }
        s.Children.Add(actions);
        valueHost.Children.Add(ValueEditor(m));
        s.Children.Add(valueHost);

        var status = Ui.Text("", 12.5);
        status.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        var save = Ui.Button(Loc.T("Übernehmen", "Apply"), () =>
        {
            if (m.Action == KeyActionType.Shortcut && KeyRemapService.ParseShortcut(m.Value) is null)
            {
                status.Text = Loc.T("Bitte eine Tastenkombination aufnehmen oder eintippen, z. B. Ctrl+Shift+Esc.", "Please record or type a key combination, e.g. Ctrl+Shift+Esc.");
                return;
            }
            if (m.Action is KeyActionType.Text or KeyActionType.Launch && string.IsNullOrWhiteSpace(m.Value))
            {
                status.Text = m.Action == KeyActionType.Text ? Loc.T("Bitte einen Text eingeben.", "Please enter a text.") : Loc.T("Bitte ein Programm oder einen Link wählen.", "Please choose a program or link.");
                return;
            }
            if (original is not null) R.Mappings.Remove(original);
            R.Mappings.RemoveAll(x => x.Key == m.Key && x.Ctrl == m.Ctrl && x.Alt == m.Alt && x.Shift == m.Shift && x.Win == m.Win);
            m.Enabled = true;
            R.Mappings.Add(m.Clone());
            Cc.ApplyKeyRemap();
            Reload();
        }, accent: true, icon: "");
        var buttons = Ui.Wrap(save);
        if (original is not null)
            buttons.Children.Add(Ui.Button(Loc.T("Belegung entfernen", "Remove mapping"), () =>
            {
                R.Mappings.Remove(original);
                Cc.ApplyKeyRemap();
                Reload();
            }, icon: Icons.Delete));
        s.Children.Add(new Border { Height = 8 });
        s.Children.Add(buttons);
        s.Children.Add(status);

        _editor!.Content = Ui.Card(s, Loc.T($"Taste „{VirtualKeyboard.LabelFor(vk)}“", $"Key “{VirtualKeyboard.LabelFor(vk)}”"),
            original is null ? Loc.T("Noch nicht belegt.", "Not mapped yet.") : Loc.T("Ist belegt – hier ändern.", "Is mapped – change it here."), Icons.Edit);
    }

    private static UIElement ValueEditor(KeyMapping m)
    {
        switch (m.Action)
        {
            case KeyActionType.Shortcut:
            {
                var box = new TextBox { Text = m.Value, MinWidth = 260 };
                box.TextChanged += (_, _) => m.Value = box.Text.Trim();
                var record = new Button { Content = Loc.T("Aufnehmen …", "Record …"), Margin = new Thickness(8, 0, 0, 0) };
                var recording = false;
                record.Click += (_, _) => { recording = true; record.Content = Loc.T("Jetzt Kombination drücken", "Press the combination now"); record.Focus(); };
                record.PreviewKeyDown += (_, e) =>
                {
                    if (!recording) return;
                    var key = e.Key == Key.System ? e.SystemKey : e.Key;
                    if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                    var parts = new List<string>();
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                    parts.Add(KeyRemapService.KeyName(KeyInterop.VirtualKeyFromKey(key)));
                    box.Text = string.Join("+", parts);
                    recording = false;
                    record.Content = Loc.T("Aufnehmen …", "Record …");
                    e.Handled = true;
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                row.Children.Add(box);
                row.Children.Add(record);
                return Ui.Stack(row, Ui.Text(Loc.T("Beispiele: Ctrl+Shift+Esc (Task-Manager) · Win+D (Desktop) · MediaPlayPause · VolumeMute",
                    "Examples: Ctrl+Shift+Esc (Task Manager) · Win+D (desktop) · MediaPlayPause · VolumeMute"), 12, sub: true));
            }
            case KeyActionType.Text:
            {
                var box = new TextBox { Text = m.Value, MinWidth = 420, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, Margin = new Thickness(0, 4, 0, 4) };
                box.TextChanged += (_, _) => m.Value = box.Text;
                return Ui.Stack(box, Ui.Text(Loc.T("Wird beim Tastendruck eingetippt, z. B. deine E-Mail-Signatur. Umlaute und Emojis funktionieren.",
                    "Typed when the key is pressed, e.g. your e-mail signature. Unicode and emojis work."), 12, sub: true));
            }
            case KeyActionType.Launch:
            {
                var box = new TextBox { Text = m.Value, MinWidth = 360 };
                box.TextChanged += (_, _) => m.Value = box.Text.Trim();
                var browse = new Button { Content = Loc.T("Durchsuchen …", "Browse …"), Margin = new Thickness(8, 0, 0, 0) };
                browse.Click += (_, _) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Programme|*.exe;*.lnk;*.bat;*.cmd|Alle Dateien|*.*", "Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*") };
                    if (dlg.ShowDialog() == true) box.Text = dlg.FileName;
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                row.Children.Add(box);
                row.Children.Add(browse);
                return Ui.Stack(row, Ui.Text(Loc.T("Programm, Datei oder Link, z. B. calc.exe oder https://…", "Program, file or link, e.g. calc.exe or https://…"), 12, sub: true));
            }
            default:
                return Ui.Text(Loc.T("Die Taste macht dann nichts mehr – praktisch für Feststelltaste oder Einfg.", "The key then does nothing – handy for Caps Lock or Insert."), 12, sub: true);
        }
    }

    private void Fill()
    {
        _keyboard!.SetMapped(R.Mappings.Where(m => m.Enabled).Select(m => m.Key));
        _list!.Children.Clear();
        if (R.Mappings.Count == 0)
        {
            _list.Children.Add(Ui.Text(Loc.T("Noch keine Belegungen. Klicke oben auf eine Taste, um anzufangen.", "No mappings yet. Click a key above to start."), sub: true, bottom: 12));
            return;
        }
        foreach (var m in R.Mappings.ToList())
        {
            var map = m;
            var desc = map.Action == KeyActionType.Disable ? ActionName(map.Action) : $"{ActionName(map.Action)}: {map.Value}";
            var controls = new StackPanel { Orientation = Orientation.Horizontal };
            controls.Children.Add(Ui.Toggle(map.Enabled, v => { map.Enabled = v; Cc.ApplyKeyRemap(); _keyboard.SetMapped(R.Mappings.Where(x => x.Enabled).Select(x => x.Key)); }));
            var edit = Ui.Button("", () => _keyboard.Select(map.Key), icon: Icons.Edit);
            edit.Margin = new Thickness(12, 0, 0, 0);
            edit.ToolTip = Loc.T("Bearbeiten", "Edit");
            controls.Children.Add(edit);
            var del = Ui.Button("", () => { R.Mappings.Remove(map); Cc.ApplyKeyRemap(); Reload(); }, icon: Icons.Delete);
            del.Margin = new Thickness(8, 0, 0, 0);
            del.ToolTip = Loc.T("Löschen", "Delete");
            controls.Children.Add(del);
            _list.Children.Add(Ui.SettingsCard(ActionIcon(map.Action), TriggerName(map), desc, controls));
        }
    }
}
