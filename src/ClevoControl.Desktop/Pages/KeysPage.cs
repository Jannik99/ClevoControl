using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using AKey = Avalonia.Input.Key;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClevoControl.Core;
using ClevoControl.Core.Platform;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;
using ClevoControl.Desktop.Controls;

namespace ClevoControl.Desktop.Pages;

/// <summary>
/// Key mapping (FlexiKey replacement) with a clickable virtual keyboard: pick a key, choose what it
/// should do, done. Mapped keys light up in the accent color.
/// Windows: applied live by the keyboard hook. Linux: ClevoControl does not grab the keyboard; the
/// mappings are exported as a keyd configuration (card at the end of the page).
/// </summary>
public sealed class KeysPage(ControlCenter cc) : AppPage(cc)
{
    private KeyRemapSettings R => Cc.Settings.KeyRemap;
    private VirtualKeyboard? _keyboard;
    private ContentControl? _editor;
    private StackPanel? _list;
    private TextBox? _keydText;
    private TextBox? _keydCommand;
    private string? _savedPath;

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
        if (m.Win) parts.Add(OperatingSystem.IsWindows() ? "Win" : Loc.T("Super (Win)", "Super (Win)"));
        parts.Add(VirtualKeyboard.LabelFor(m.Key));
        return string.Join(" + ", parts);
    }

    public override Control Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Tastenbelegung", "Key mapping")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Klicke auf eine Taste und lege fest, was sie tun soll – eine Tastenkombination, ein Text, ein Programm oder gar nichts.",
            "Click a key and choose what it should do – a key combination, a text, a program or nothing at all.")));

        if (OperatingSystem.IsLinux())
            root.Children.Add(Ui.InfoBar(Loc.T(
                "Unter Linux greift ClevoControl nicht selbst in die Tastatur ein. Die Belegungen werden als Konfiguration für keyd erzeugt und wirken erst, nachdem du diese Datei installiert hast – siehe „Mit keyd anwenden“ unten.",
                "On Linux, ClevoControl does not intercept the keyboard itself. The mappings are exported as a keyd configuration and take effect only after you install that file – see “Apply with keyd” below.")));

        root.Children.Add(Ui.SettingsCard(Icons.Keyboard, Loc.T("Tastenbelegung aktiv", "Key mapping active"),
            OperatingSystem.IsLinux()
                ? Loc.T("Schaltet alle Belegungen auf einmal an oder aus (in der keyd-Konfiguration).", "Turns all mappings on or off at once (in the keyd configuration).")
                : Loc.T("Gilt, solange ClevoControl läuft. Schaltet alle Belegungen auf einmal an oder aus.", "Works while ClevoControl is running. Turns all mappings on or off at once."),
            Ui.Toggle(R.Enabled, v => { R.Enabled = v; Changed(); })));
        root.Children.Add(new Border { Height = 8 });

        _keyboard = new VirtualKeyboard(R.ShowNumpad) { Margin = new Thickness(0, 0, 0, 12) };
        _keyboard.KeySelected += (vk, _) => ShowEditor(vk);
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Ui.SetRes(dot, Border.BackgroundProperty, Ui.Accent);
        legend.Children.Add(dot);
        var mappedText = Ui.Text(Loc.T("belegt", "mapped"), 12, sub: true);
        mappedText.VerticalAlignment = VerticalAlignment.Center;
        legend.Children.Add(mappedText);
        var numpad = new CheckBox { Content = Loc.T("Ziffernblock anzeigen", "Show number pad"), IsChecked = R.ShowNumpad, Margin = new Thickness(24, 0, 0, 0) };
        numpad.IsCheckedChanged += (_, _) => { R.ShowNumpad = numpad.IsChecked == true; Cc.Store.SaveSoon(); Reload(); };
        legend.Children.Add(numpad);
        root.Children.Add(Ui.Card(Ui.Stack(_keyboard, legend), Loc.T("Taste wählen", "Choose a key"), icon: Icons.Keyboard));

        _editor = new ContentControl { Content = Ui.Card(Ui.Text(Loc.T("Wähle oben eine Taste aus.", "Choose a key above."), sub: true)) };
        root.Children.Add(_editor);

        root.Children.Add(Ui.Section(Loc.T("Alle Belegungen", "All mappings")));
        _list = new StackPanel();
        root.Children.Add(_list);

        if (OperatingSystem.IsLinux())
        {
            root.Children.Add(new Border { Height = 12 });
            root.Children.Add(Ui.Section(Loc.T("Mit keyd anwenden", "Apply with keyd")));
            root.Children.Add(KeydCard());
        }

        Fill();
        return root;
    }

    private static void Reload() => (Owner as MainWindow)?.Navigate("keys");

    /// <summary>A mapping changed: apply it (Windows hook) and refresh the keyd export (Linux).</summary>
    private void Changed()
    {
        Cc.ApplyKeyRemap();
        RefreshKeyd();
    }

    private void RefreshKeyd()
    {
        if (_keydText is not null) _keydText.Text = KeydConfig.Generate(R, Cc.Settings.General.WinKeyLock);
    }

    private string InstallCommand() =>
        $"sudo cp {Quote(_savedPath ?? "clevocontrol.conf")} {KeydConfig.InstallPath} && sudo keyd reload";

    private static string Quote(string path) =>
        path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or '~') ? path : "'" + path.Replace("'", "'\\''") + "'";

    private Control KeydCard()
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Text(Loc.T(
            "keyd ist ein Dienst zum Umbelegen von Tasten, der unter X11, Wayland und in der Konsole funktioniert und von den meisten Distributionen als Paket angeboten wird (z. B. „sudo apt install keyd“ bzw. „sudo pacman -S keyd“, danach „sudo systemctl enable --now keyd“). Die Belegungen oben – und die Sperre der Super-/Windows-Taste – werden automatisch in diese Konfiguration übersetzt.",
            "keyd is a key remapping daemon that works on X11, Wayland and the console and is packaged by most distributions (e.g. “sudo apt install keyd” or “sudo pacman -S keyd”, then “sudo systemctl enable --now keyd”). The mappings above – and the Super/Windows key lock – are translated into this configuration automatically."),
            12.5, sub: true, bottom: 6));
        var link = new Button { Content = "github.com/rvaiya/keyd", Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand) };
        Ui.SetRes(link, Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, Ui.Accent);
        link.Click += (_, _) => Ui.Open("https://github.com/rvaiya/keyd");
        s.Children.Add(link);

        s.Children.Add(Ui.Switch(Loc.T("Super-/Windows-Taste sperren", "Lock Super/Windows key"), Cc.Settings.General.WinKeyLock,
            v => { Cc.Settings.General.WinKeyLock = v; Cc.ApplyGeneral(); RefreshKeyd(); },
            Loc.T("Verhindert z. B. versehentliches Öffnen des Startmenüs beim Spielen. Wird ebenfalls über keyd angewendet.",
                "Prevents e.g. opening the start menu by accident while gaming. Also applied through keyd.")));

        s.Children.Add(Ui.InfoBar(Loc.T(
            "Belegungen vom Typ „Programm oder Link öffnen“ werden nicht übertragen (keyd würde sie als root starten); sie stehen nur als Kommentar in der Datei. Lege solche Tastenkürzel am besten in den Tastatur-Einstellungen deines Desktops (GNOME, KDE …) an. Texte mit Sonderzeichen werden ebenfalls nur als Kommentar übernommen.",
            "Mappings of the type “Open program or link” are not converted (keyd would start them as root); they only appear as comments in the file. Create such shortcuts in your desktop's keyboard settings (GNOME, KDE …) instead. Texts with special characters are also only kept as comments."), warning: true));

        _keydText = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 120, MaxHeight = 320,
            FontFamily = new FontFamily("Cascadia Mono,DejaVu Sans Mono,Liberation Mono,Noto Sans Mono,Consolas,monospace"), FontSize = 12.5,
            Margin = new Thickness(0, 4, 0, 10),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_keydText, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        RefreshKeyd();
        s.Children.Add(_keydText);

        var status = Ui.Text("", 12.5, sub: true);
        var copy = Ui.Button(Loc.T("Kopieren", "Copy"), async () =>
        {
            if (TopLevel.GetTopLevel(_keydText)?.Clipboard is { } cb)
            {
                await cb.SetTextAsync(_keydText.Text ?? "");
                status.Text = Loc.T("In die Zwischenablage kopiert.", "Copied to the clipboard.");
            }
        }, icon: Icons.Code);
        var save = Ui.Button(Loc.T("Speichern unter …", "Save as …"), async () =>
        {
            if (TopLevel.GetTopLevel(_keydText)?.StorageProvider is not { } sp) return;
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.T("keyd-Konfiguration speichern", "Save keyd configuration"),
                SuggestedFileName = "clevocontrol.conf",
                DefaultExtension = "conf",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType(Loc.T("keyd-Konfiguration", "keyd configuration")) { Patterns = ["*.conf"] }, FilePickerFileTypes.All],
            });
            if (file is null) return;
            try
            {
                await using (var stream = await file.OpenWriteAsync())
                {
                    stream.SetLength(0);
                    await using var w = new StreamWriter(stream);
                    await w.WriteAsync(KeydConfig.Generate(R, Cc.Settings.General.WinKeyLock));
                }
                _savedPath = file.TryGetLocalPath() ?? file.Name;
                if (_keydCommand is not null) _keydCommand.Text = InstallCommand();
                status.Text = Loc.T($"Gespeichert: {_savedPath}", $"Saved: {_savedPath}");
            }
            catch (Exception ex)
            {
                status.Text = Loc.T("Speichern fehlgeschlagen: ", "Saving failed: ") + ex.Message;
            }
        }, accent: true, icon: Icons.Save);
        s.Children.Add(Ui.Wrap(copy, save));
        s.Children.Add(status);

        s.Children.Add(Ui.Text(Loc.T("Installieren (im Terminal, nach dem Speichern):", "Install (in a terminal, after saving):"), 13, true, bottom: 4));
        s.Children.Add(Ui.Text(Loc.T(
            "Die Belegungen wirken erst, wenn die Datei installiert und keyd neu geladen wurde. Nach jeder Änderung hier erneut speichern und installieren.",
            "The mappings take effect only once the file is installed and keyd is reloaded. After every change here, save and install again."), 12, sub: true, bottom: 6));
        _keydCommand = new TextBox
        {
            IsReadOnly = true, Text = InstallCommand(), TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono,DejaVu Sans Mono,Liberation Mono,Noto Sans Mono,Consolas,monospace"), FontSize = 12.5,
        };
        s.Children.Add(_keydCommand);

        return Ui.Card(s, Loc.T("Mit keyd anwenden", "Apply with keyd"),
            Loc.T($"Erzeugt die Datei {KeydConfig.InstallPath} für den Dienst keyd.", $"Generates the file {KeydConfig.InstallPath} for the keyd daemon."), Icons.Code);
    }

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
            c.IsCheckedChanged += (_, _) => set(c.IsChecked == true);
            return c;
        }
        mods.Children.Add(Mod(Loc.T("Strg", "Ctrl"), m.Ctrl, v => m.Ctrl = v));
        mods.Children.Add(Mod("Alt", m.Alt, v => m.Alt = v));
        mods.Children.Add(Mod(Loc.T("Umschalt", "Shift"), m.Shift, v => m.Shift = v));
        mods.Children.Add(Mod(OperatingSystem.IsWindows() ? "Win" : "Super (Win)", m.Win, v => m.Win = v));
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
        Ui.SetRes(status, TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
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

    private static Control ValueEditor(KeyMapping m)
    {
        switch (m.Action)
        {
            case KeyActionType.Shortcut:
            {
                var box = new TextBox { Text = m.Value, MinWidth = 260 };
                box.TextChanged += (_, _) => m.Value = (box.Text ?? "").Trim();
                var record = new Button { Content = Loc.T("Aufnehmen …", "Record …"), Margin = new Thickness(8, 0, 0, 0) };
                var recording = false;
                record.Click += (_, _) => { recording = true; record.Content = Loc.T("Jetzt Kombination drücken", "Press the combination now"); record.Focus(); };
                // Tunnel (= WPF PreviewKeyDown) so Enter/Space/Tab are recorded instead of clicking or moving focus.
                record.AddHandler(InputElement.KeyDownEvent, (_, e) =>
                {
                    if (!recording) return;
                    if (e.Key is AKey.LeftCtrl or AKey.RightCtrl or AKey.LeftAlt or AKey.RightAlt or AKey.LeftShift or AKey.RightShift or AKey.LWin or AKey.RWin
                        || e.PhysicalKey is PhysicalKey.ControlLeft or PhysicalKey.ControlRight or PhysicalKey.AltLeft or PhysicalKey.AltRight
                            or PhysicalKey.ShiftLeft or PhysicalKey.ShiftRight or PhysicalKey.MetaLeft or PhysicalKey.MetaRight) return;
                    var vk = VirtualKeyOf(e);
                    if (vk == 0) return;
                    var parts = new List<string>();
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
                    parts.Add(KeyRemapService.KeyName(vk));
                    box.Text = string.Join("+", parts);
                    recording = false;
                    record.Content = Loc.T("Aufnehmen …", "Record …");
                    e.Handled = true;
                }, RoutingStrategies.Tunnel);
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                row.Children.Add(box);
                row.Children.Add(record);
                return Ui.Stack(row, Ui.Text(OperatingSystem.IsWindows()
                    ? Loc.T("Beispiele: Ctrl+Shift+Esc (Task-Manager) · Win+D (Desktop) · MediaPlayPause · VolumeMute",
                        "Examples: Ctrl+Shift+Esc (Task Manager) · Win+D (desktop) · MediaPlayPause · VolumeMute")
                    : Loc.T("Beispiele: Ctrl+Alt+T (Terminal) · Win+D (Desktop) · MediaPlayPause · VolumeMute",
                        "Examples: Ctrl+Alt+T (terminal) · Win+D (desktop) · MediaPlayPause · VolumeMute"), 12, sub: true));
            }
            case KeyActionType.Text:
            {
                var box = new TextBox { Text = m.Value, MinWidth = 420, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
                box.TextChanged += (_, _) => m.Value = box.Text ?? "";
                var hint = Ui.Text(Loc.T("Wird beim Tastendruck eingetippt, z. B. deine E-Mail-Signatur. Umlaute und Emojis funktionieren.",
                    "Typed when the key is pressed, e.g. your e-mail signature. Unicode and emojis work."), 12, sub: true);
                if (!OperatingSystem.IsLinux()) return Ui.Stack(box, hint);
                return Ui.Stack(box, Ui.Text(Loc.T("Wird beim Tastendruck eingetippt. Mit keyd (Linux) nur Buchstaben, Ziffern und Leerzeichen – andere Zeichen werden nicht übertragen.",
                    "Typed when the key is pressed. With keyd (Linux) only letters, digits and spaces – other characters are not converted."), 12, sub: true));
            }
            case KeyActionType.Launch:
            {
                var box = new TextBox { Text = m.Value, MinWidth = 360 };
                box.TextChanged += (_, _) => m.Value = (box.Text ?? "").Trim();
                var browse = new Button { Content = Loc.T("Durchsuchen …", "Browse …"), Margin = new Thickness(8, 0, 0, 0) };
                browse.Click += async (_, _) =>
                {
                    if (TopLevel.GetTopLevel(browse)?.StorageProvider is not { } sp) return;
                    List<FilePickerFileType> filters = OperatingSystem.IsWindows()
                        ? [new FilePickerFileType(Loc.T("Programme", "Programs")) { Patterns = ["*.exe", "*.lnk", "*.bat", "*.cmd"] }, FilePickerFileTypes.All]
                        : [FilePickerFileTypes.All];
                    var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = filters });
                    if (files.Count > 0) box.Text = files[0].TryGetLocalPath() ?? files[0].Path.ToString();
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                row.Children.Add(box);
                row.Children.Add(browse);
                var stack = Ui.Stack(row, Ui.Text(OperatingSystem.IsWindows()
                    ? Loc.T("Programm, Datei oder Link, z. B. calc.exe oder https://…", "Program, file or link, e.g. calc.exe or https://…")
                    : Loc.T("Programm, Datei oder Link, z. B. gnome-calculator oder https://…", "Program, file or link, e.g. gnome-calculator or https://…"), 12, sub: true));
                if (OperatingSystem.IsLinux())
                    stack.Children.Add(Ui.InfoBar(Loc.T(
                        "Unter Linux wird diese Belegung nicht über keyd angewendet. Lege das Tastenkürzel besser in den Tastatur-Einstellungen deines Desktops an.",
                        "On Linux this mapping is not applied through keyd. Better create the shortcut in your desktop's keyboard settings."), warning: true));
                return stack;
            }
            default:
                return Ui.Text(Loc.T("Die Taste macht dann nichts mehr – praktisch für Feststelltaste oder Einfg.", "The key then does nothing – handy for Caps Lock or Insert."), 12, sub: true);
        }
    }

    /// <summary>
    /// Windows virtual-key code of a pressed key (what the settings store). Letters, digits and named
    /// keys come from the logical key; punctuation (layout dependent) from the physical position on a
    /// German QWERTZ keyboard, matching the keyboard picture.
    /// </summary>
    private static int VirtualKeyOf(KeyEventArgs e)
    {
        var k = e.Key;
        if (k is >= AKey.A and <= AKey.Z) return 'A' + (k - AKey.A);
        if (k is >= AKey.D0 and <= AKey.D9) return '0' + (k - AKey.D0);
        if (k is >= AKey.F1 and <= AKey.F24) return 0x70 + (k - AKey.F1);
        if (k is >= AKey.NumPad0 and <= AKey.NumPad9) return 0x60 + (k - AKey.NumPad0);
        var vk = k switch
        {
            AKey.Escape => 0x1B, AKey.Tab => 0x09, AKey.Enter => 0x0D, AKey.Space => 0x20, AKey.Back => 0x08,
            AKey.Delete => 0x2E, AKey.Insert => 0x2D, AKey.Home => 0x24, AKey.End => 0x23, AKey.PageUp => 0x21, AKey.PageDown => 0x22,
            AKey.Up => 0x26, AKey.Down => 0x28, AKey.Left => 0x25, AKey.Right => 0x27,
            AKey.PrintScreen => 0x2C, AKey.Pause => 0x13, AKey.Scroll => 0x91, AKey.CapsLock => 0x14, AKey.NumLock => 0x90, AKey.Apps => 0x5D,
            AKey.Multiply => 0x6A, AKey.Add => 0x6B, AKey.Subtract => 0x6D, AKey.Decimal => 0x6E, AKey.Divide => 0x6F,
            AKey.VolumeUp => 0xAF, AKey.VolumeDown => 0xAE, AKey.VolumeMute => 0xAD, AKey.MediaPlayPause => 0xB3,
            AKey.MediaNextTrack => 0xB0, AKey.MediaPreviousTrack => 0xB1, AKey.MediaStop => 0xB2,
            AKey.LaunchApplication2 => 0xB7, AKey.LaunchMail => 0xB4, AKey.BrowserHome => 0xAC,
            _ => 0,
        };
        if (vk != 0) return vk;
        return e.PhysicalKey switch
        {
            // German OEM keys by physical position (same assignment as KeydConfig.KeyName)
            PhysicalKey.BracketLeft => 0xBA, PhysicalKey.BracketRight => 0xBB, PhysicalKey.Comma => 0xBC, PhysicalKey.Slash => 0xBD,
            PhysicalKey.Period => 0xBE, PhysicalKey.Backslash => 0xBF, PhysicalKey.Semicolon => 0xC0, PhysicalKey.Minus => 0xDB,
            PhysicalKey.Backquote => 0xDC, PhysicalKey.Equal => 0xDD, PhysicalKey.Quote => 0xDE, PhysicalKey.IntlBackslash => 0xE2,
            _ => 0,
        };
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
            controls.Children.Add(Ui.Toggle(map.Enabled, v => { map.Enabled = v; Changed(); _keyboard.SetMapped(R.Mappings.Where(x => x.Enabled).Select(x => x.Key)); }));
            var edit = Ui.Button("", () => _keyboard.Select(map.Key), icon: Icons.Edit);
            edit.Margin = new Thickness(12, 0, 0, 0);
            ToolTip.SetTip(edit, Loc.T("Bearbeiten", "Edit"));
            controls.Children.Add(edit);
            var del = Ui.Button("", () => { R.Mappings.Remove(map); Cc.ApplyKeyRemap(); Reload(); }, icon: Icons.Delete);
            del.Margin = new Thickness(8, 0, 0, 0);
            ToolTip.SetTip(del, Loc.T("Löschen", "Delete"));
            controls.Children.Add(del);
            _list.Children.Add(Ui.SettingsCard(ActionIcon(map.Action), TriggerName(map), desc, controls));
        }
    }
}
