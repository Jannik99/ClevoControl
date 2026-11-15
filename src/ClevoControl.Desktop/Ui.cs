using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ClevoControl.Core;
using Material.Icons.Avalonia;

namespace ClevoControl.Desktop;

/// <summary>
/// Helpers to build the pages in code. Same method names and parameters as <c>Ui</c> in the WPF app
/// (src/ClevoControl.App/Ui.cs), so pages can be ported nearly line by line.
/// Colors are referenced by their WinUI resource names (defined in App.axaml for light and dark).
/// </summary>
public static class Ui
{
    public const string TextPrimary = "TextFillColorPrimaryBrush";
    public const string TextSecondary = "TextFillColorSecondaryBrush";
    public const string CardBg = "CardBackgroundFillColorDefaultBrush";
    public const string CardStroke = "CardStrokeColorDefaultBrush";
    public const string Accent = "AccentFillColorDefaultBrush";

    /// <summary>Current value of a theme brush (does not follow later theme changes – prefer <see cref="SetRes"/>).</summary>
    public static IBrush Res(string key)
    {
        var app = Application.Current!;
        return app.TryGetResource(key, app.ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;
    }

    /// <summary>Binds a property to a theme resource (like WPF's SetResourceReference).</summary>
    public static T SetRes<T>(T control, AvaloniaProperty property, string key) where T : StyledElement
    {
        control.Bind(property, control.GetResourceObservable(key));
        return control;
    }

    public static SolidColorBrush ToBrush(this RgbColor c) => new(Color.FromRgb(c.R, c.G, c.B));

    public static Color ToAva(this RgbColor c) => Color.FromRgb(c.R, c.G, c.B);

    public static TextBlock Text(string text, double size = 14, bool bold = false, bool sub = false, double bottom = 0)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, bottom),
        };
        return SetRes(t, TextBlock.ForegroundProperty, sub ? TextSecondary : TextPrimary);
    }

    /// <summary>An icon (glyph from <see cref="Icons"/>); unknown glyphs such as emoji are shown as text.</summary>
    public static Control Icon(string glyph, double size = 16, string brush = TextPrimary)
    {
        if (Icons.Kind(glyph) is { } kind)
        {
            var mi = new MaterialIcon { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
            return SetRes(mi, TemplatedControl.ForegroundProperty, brush);
        }
        var t = new TextBlock { Text = glyph, FontSize = size * 0.9, VerticalAlignment = VerticalAlignment.Center };
        return SetRes(t, TextBlock.ForegroundProperty, brush);
    }

    public static TextBlock PageTitle(string title) => Text(title, 28, true, bottom: 20);

    public static TextBlock PageSubtitle(string text) => text.Length == 0 ? new TextBlock() : Text(text, 14, sub: true, bottom: 20);

    public static TextBlock Section(string text) => Text(text, 14, true, bottom: 8);

    /// <summary>
    /// A group of settings: optional header, content inside a Windows-style card.
    /// With an icon, the content is indented to line up with the title (like WinUI's SettingsExpander);
    /// pass <paramref name="indent"/> = false for content that needs the full width (charts, tile grids, color picker).
    /// </summary>
    public static Border Card(Control content, string? title = null, string? subtitle = null, string? icon = null, bool indent = true)
    {
        var inner = new StackPanel();
        if (title is not null)
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, subtitle is null ? 14 : 2) };
            if (icon is not null)
            {
                var ic = Icon(icon, 20);
                ic.Margin = new Thickness(0, 0, 14, 0);
                DockPanel.SetDock(ic, Dock.Left);
                head.Children.Add(ic);
            }
            head.Children.Add(Text(title, 14, true));
            inner.Children.Add(head);
            if (subtitle is not null)
            {
                var st = Text(subtitle, 12, sub: true, bottom: 14);
                if (icon is not null) st.Margin = new Thickness(34, 0, 0, 14);
                inner.Children.Add(st);
            }
        }
        if (title is not null && icon is not null && indent)
        {
            var m = content.Margin;
            content.Margin = new Thickness(m.Left + CardIndent, m.Top, m.Right, m.Bottom);
        }
        inner.Children.Add(content);
        return CardBorder(inner, new Thickness(0, 0, 0, 8));
    }

    /// <summary>Indent of card content below a header with icon (20 px icon + 14 px gap).</summary>
    public const double CardIndent = 34;

    /// <summary>
    /// Card frame. The bottom margin of the last content element (rows, buttons, sliders all bring their own)
    /// is removed when the card is shown, so the space at the bottom equals the padding at the top.
    /// </summary>
    public static Border CardBorder(Control child, Thickness margin, Thickness? padding = null)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(16, 14, 16, 14),
            Margin = margin,
            Child = child,
        };
        SetRes(b, Border.BackgroundProperty, CardBg);
        SetRes(b, Border.BorderBrushProperty, CardStroke);
        void Trim(object? s, Avalonia.Interactivity.RoutedEventArgs e)
        {
            b.Loaded -= Trim;
            TrimBottom(child);
        }
        b.Loaded += Trim;
        return b;
    }

    /// <summary>Removes the bottom margin of the last element (recursively through panels and content controls).</summary>
    public static void TrimBottom(Control? c)
    {
        if (c is null) return;
        var m = c.Margin;
        switch (c)
        {
            case StackPanel { Orientation: Orientation.Vertical } sp:
                if (m.Bottom > 0) c.Margin = m.WithBottom(0);
                TrimBottom(sp.Children.LastOrDefault(x => x.IsVisible));
                break;
            case StackPanel sp:
                if (m.Bottom > 0) c.Margin = m.WithBottom(0);
                foreach (var x in sp.Children) TrimBottom(x);
                break;
            case WrapPanel wp:
                // Each wrapped element keeps its bottom margin (spacing between lines); the panel hides the last one.
                c.Margin = m.WithBottom(-wp.Children.Where(x => x.IsVisible).Select(x => x.Margin.Bottom).DefaultIfEmpty(0).Max());
                break;
            case Grid g:
                if (m.Bottom > 0) c.Margin = m.WithBottom(0);
                var last = g.RowDefinitions.Count - 1;
                foreach (var x in g.Children)
                    if (last <= 0 || Grid.GetRow(x) + Grid.GetRowSpan(x) - 1 >= last) TrimBottom(x);
                break;
            case ContentControl { Content: Control inner }:
                if (m.Bottom > 0) c.Margin = m.WithBottom(0);
                TrimBottom(inner);
                break;
            case Decorator { Child: { } inner } when c is not Border { BorderThickness.Bottom: > 0 }:
                if (m.Bottom > 0) c.Margin = m.WithBottom(0);
                TrimBottom(inner);
                break;
            default:
                if (m.Bottom > 0) c.Margin = m.WithBottom(0);
                break;
        }
    }

    /// <summary>One settings row as its own card: icon, title, description, control on the right.</summary>
    public static Border SettingsCard(string icon, string title, string? description, Control? control)
    {
        var g = new Grid { MinHeight = 40, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var ic = Icon(icon, 20);
        ic.Margin = new Thickness(2, 0, 18, 0);
        g.Children.Add(ic);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(Text(title));
        if (description is not null) text.Children.Add(Text(description, 12, sub: true));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 2);
            g.Children.Add(control);
        }
        return CardBorder(g, new Thickness(0, 0, 0, 4), new Thickness(16, 12, 16, 12));
    }

    public static StackPanel Stack(params Control[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    public static WrapPanel Wrap(params Control[] children)
    {
        var w = new WrapPanel();
        foreach (var c in children) w.Children.Add(c);
        return w;
    }

    /// <summary>Label left, control right, optional description below the label. The label is also the control's accessible name.</summary>
    public static Grid Row(string label, Control control, string? description = null, double bottom = 14)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, bottom), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Text(label));
        if (description is not null) left.Children.Add(Text(description, 12, sub: true));
        g.Children.Add(left);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(control);
        // Toggle() returns a panel with the switch in Tag; name the switch itself.
        var target = control.Tag as ToggleSwitch ?? control;
        if (string.IsNullOrEmpty(AutomationProperties.GetName(target))) AutomationProperties.SetName(target, label);
        return g;
    }

    /// <summary>
    /// A toggle switch with the "On"/"Off" text on its left (Windows 11 Settings style). Returns a panel;
    /// the <see cref="ToggleSwitch"/> itself is in <c>Tag</c>.
    /// </summary>
    public static Control Toggle(bool value, Action<bool> changed, string? name = null)
    {
        var state = Text(value ? Loc.T("Ein", "On") : Loc.T("Aus", "Off"));
        state.Margin = new Thickness(0, 0, 12, 0);
        state.VerticalAlignment = VerticalAlignment.Center;
        state.TextWrapping = TextWrapping.NoWrap;
        var ts = new ToggleSwitch
        {
            IsChecked = value,
            OnContent = null,
            OffContent = null,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            // The Fluent template keeps a 12 px gap for the (empty) On/Off content: line the switch up with the card edge.
            Margin = new Thickness(0, 0, -12, 0),
        };
        if (name is not null) AutomationProperties.SetName(ts, name);
        ts.IsCheckedChanged += (_, _) =>
        {
            state.Text = ts.IsChecked == true ? Loc.T("Ein", "On") : Loc.T("Aus", "Off");
            changed(ts.IsChecked == true);
        };
        var p = new StackPanel { Orientation = Orientation.Horizontal, Tag = ts };
        p.Children.Add(state);
        p.Children.Add(ts);
        return p;
    }

    /// <summary>Toggle switch with title and description (used inside cards).</summary>
    public static Grid Switch(string label, bool value, Action<bool> changed, string? description = null) =>
        Row(label, Toggle(value, changed, label), description);

    /// <summary>Slider with a label and live value text. The slider itself is in <c>Tag</c>.</summary>
    public static Control SliderRow(string label, double min, double max, double value, Action<double> changed,
        Func<double, string>? format = null, double step = 1, IBrush? track = null, double bottom = 12)
    {
        format ??= v => v.ToString("0");
        var valueText = Text(format(value), 13, sub: true);
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        var header = new Grid();
        header.Children.Add(Text(label));
        header.Children.Add(valueText);
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, SmallChange = step, LargeChange = step * 5,
            IsSnapToTickEnabled = step >= 1, TickFrequency = step, Margin = new Thickness(0, 0, 0, 0),
        };
        AutomationProperties.SetName(slider, label);
        if (track is not null)
        {
            // Gradient track (hue / brightness): drawn behind a transparent slider track.
            slider.Background = track;
            slider.Classes.Add("gradient");
        }
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, bottom), Tag = slider };
        s.Children.Add(header);
        if (track is not null)
            s.Children.Add(new Panel
            {
                Children =
                {
                    new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = track, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) },
                    slider,
                },
            });
        else s.Children.Add(slider);
        return s;
    }

    public static ComboBox Combo<T>(IEnumerable<(T Value, string Label)> items, T selected, Action<T> changed, double width = 200)
    {
        var cb = new ComboBox { Width = width };
        foreach (var (v, l) in items)
        {
            var item = new ComboBoxItem { Content = l, Tag = v };
            cb.Items.Add(item);
            if (EqualityComparer<T>.Default.Equals(v, selected)) cb.SelectedItem = item;
        }
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedItem is ComboBoxItem { Tag: T v }) changed(v);
        };
        return cb;
    }

    /// <summary>
    /// Button with optional icon. With empty <paramref name="text"/> it is an icon-only button (min. 40 px wide);
    /// then <paramref name="tip"/> (or a tooltip set later) is used as tooltip and accessible name.
    /// </summary>
    public static Button Button(string text, Action click, bool accent = false, string? icon = null, string? tip = null)
    {
        object content = text;
        if (icon is not null && text.Length == 0)
        {
            content = Icon(icon, 14, accent ? "TextOnAccentFillColorPrimaryBrush" : TextPrimary);
        }
        else if (icon is not null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icon(icon, 14, accent ? "TextOnAccentFillColorPrimaryBrush" : TextPrimary);
            ic.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            content = sp;
        }
        var b = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8), MinWidth = text.Length == 0 ? 40 : 32, MinHeight = 32 };
        if (accent) b.Classes.Add("accent");
        if (tip is not null) ToolTip.SetTip(b, tip);
        AutomationProperties.SetName(b, tip ?? text);
        if (text.Length == 0 && tip is null)
        {
            // Pages often set the tooltip after creating the button: use it as the name once shown.
            void Name(object? s, Avalonia.Interactivity.RoutedEventArgs e)
            {
                b.Loaded -= Name;
                if (string.IsNullOrEmpty(AutomationProperties.GetName(b)) && ToolTip.GetTip(b) is string t)
                    AutomationProperties.SetName(b, t.Split('\n')[0]);
            }
            b.Loaded += Name;
        }
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Icon-only button; <paramref name="tip"/> is the tooltip and the accessible name.</summary>
    public static Button IconButton(string icon, string tip, Action click, bool accent = false) =>
        Button("", click, accent, icon, tip);

    /// <summary>
    /// Grid of equal-width cells (e.g. <see cref="Tile"/>s) whose column count follows the window width:
    /// <c>cols = max(1, floor((width + gap) / (minWidth + gap)))</c>. Children stretch; their own width and
    /// margin are ignored. Has a bottom margin of <paramref name="gap"/> (removed as last element of a card).
    /// Add more children later through <c>Children.Add</c>.
    /// </summary>
    public static Controls.TileGrid TileGrid(double minWidth = 150, double gap = 8, params Control[] children)
    {
        var g = new Controls.TileGrid { MinItemWidth = minWidth, Gap = gap, Margin = new Thickness(0, 0, 0, gap) };
        foreach (var c in children) g.Children.Add(c);
        return g;
    }

    /// <summary>
    /// Readable text color on a colored background: black (90 % opacity) if the relative luminance
    /// (WCAG) is above 0.179, otherwise white.
    /// </summary>
    public static IBrush ContrastText(RgbColor c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        var l = 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        return l > 0.179 ? new SolidColorBrush(Color.FromArgb(230, 0, 0, 0)) : Brushes.White;
    }

    /// <summary>
    /// Makes a clickable element (e.g. a Border used as a tile or swatch) usable with the keyboard:
    /// focusable, Enter/Space run <paramref name="click"/>, and <paramref name="name"/> is its accessible name.
    /// Mouse clicks are not wired here.
    /// </summary>
    public static T Clickable<T>(T control, string name, Action click) where T : Control
    {
        control.Focusable = true;
        control.IsTabStop = true;
        AutomationProperties.SetName(control, name);
        control.KeyDown += (_, e) =>
        {
            if (e.Key is not (Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space)) return;
            e.Handled = true;
            click();
        };
        return control;
    }

    /// <summary>
    /// Shows an undo strip (message + "Undo" button) in <paramref name="host"/> at <paramref name="index"/>
    /// (-1 = at the end, 0 = at the top). It removes itself after <paramref name="seconds"/>; clicking
    /// "Undo" removes it and calls <paramref name="undo"/>. A previous undo strip in the same host is replaced.
    /// Use after destructive actions (delete scene, rule, mapping …) instead of a confirmation dialog.
    /// </summary>
    /// <returns>The strip, e.g. to remove it early.</returns>
    public static Border ShowUndo(Panel host, string message, Action undo, int index = -1, double seconds = 8)
    {
        foreach (var old in host.Children.OfType<Border>().Where(x => x.Tag as string == UndoTag).ToList()) host.Children.Remove(old);
        var g = new DockPanel();
        var ic = Icon(Icons.Info, 16, Accent);
        ic.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(ic, Dock.Left);
        Border? bar = null;
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        var btn = Button(Loc.T("Rückgängig", "Undo"), () =>
        {
            timer.Stop();
            host.Children.Remove(bar!);
            undo();
        }, icon: Icons.History);
        btn.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(btn, Dock.Right);
        var text = Text(message, 12.5);
        text.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ic);
        g.Children.Add(btn);
        g.Children.Add(text);
        bar = new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 6, 8, 6), Margin = new Thickness(0, 4, 0, 8),
            Child = g, BorderThickness = new Thickness(1), Tag = UndoTag,
        };
        SetRes(bar, Border.BackgroundProperty, "SystemFillColorAttentionBackgroundBrush");
        SetRes(bar, Border.BorderBrushProperty, CardStroke);
        AutomationProperties.SetLiveSetting(bar, AutomationLiveSetting.Polite);
        timer.Tick += (_, _) => { timer.Stop(); host.Children.Remove(bar); };
        if (index < 0 || index > host.Children.Count) host.Children.Add(bar);
        else host.Children.Insert(index, bar);
        timer.Start();
        return bar;
    }

    private const string UndoTag = "ui-undo";

    private static Thickness WithBottom(this Thickness t, double bottom) => new(t.Left, t.Top, t.Right, bottom);

    public static RadioButton Tile(string icon, string title, string? subtitle, bool isChecked, string group, Action select, double width = 150)
    {
        var s = new StackPanel();
        var ic = Icon(icon, 20);
        ic.HorizontalAlignment = HorizontalAlignment.Left;
        ic.Margin = new Thickness(0, 2, 0, 8);
        s.Children.Add(ic);
        s.Children.Add(Text(title, 14));
        if (subtitle is not null) s.Children.Add(Text(subtitle, 12, sub: true));
        var r = new RadioButton
        {
            Content = s,
            GroupName = group,
            IsChecked = isChecked,
            Width = width,
            // Same height for every tile of a row (with or without a two-line description).
            MinHeight = subtitle is null ? 72 : 96,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(r, subtitle is null ? title : $"{title}, {subtitle}");
        r.Classes.Add("tile");
        r.IsCheckedChanged += (_, _) => { if (r.IsChecked == true) select(); };
        return r;
    }

    public static Border Swatch(RgbColor c, double size, Action? click = null, string? tooltip = null)
    {
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = c.ToBrush(), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 8),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(b, tooltip ?? c.ToHex());
        SetRes(b, Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
        if (click is not null)
        {
            b.PointerReleased += (_, _) => click();
            Clickable(b, (tooltip ?? c.ToHex()).Split(" · ")[0], click);
        }
        return b;
    }

    public static LinearGradientBrush HueBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (var i = 0; i <= 6; i++) b.GradientStops.Add(new GradientStop(RgbColor.FromHsv(i * 60, 1, 1).ToAva(), i / 6.0));
        return b;
    }

    public static LinearGradientBrush Gradient(Color a, Color b) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
    };

    /// <summary>InfoBar like in WinUI (informational / warning).</summary>
    public static Border InfoBar(string text, bool warning = false)
    {
        var g = new DockPanel();
        var ic = Icon(warning ? Icons.Warning : Icons.Info, 16, warning ? "SystemFillColorCautionBrush" : Accent);
        ic.Margin = new Thickness(0, 1, 12, 0);
        ic.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(ic, Dock.Left);
        g.Children.Add(ic);
        g.Children.Add(Text(text, 12.5));
        var b = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 4, 0, 8), Child = g, BorderThickness = new Thickness(1) };
        SetRes(b, Border.BackgroundProperty, warning ? "SystemFillColorCautionBackgroundBrush" : "SystemFillColorAttentionBackgroundBrush");
        SetRes(b, Border.BorderBrushProperty, CardStroke);
        return b;
    }

    /// <summary>Simple message box (Avalonia has none built in).</summary>
    public static Task MessageAsync(Window? owner, string text, string title = "ClevoControl") =>
        DialogAsync(owner, text, title, [Loc.T("OK", "OK")]);

    /// <summary>Yes/No question. Returns true for yes.</summary>
    public static async Task<bool> ConfirmAsync(Window? owner, string text, string title = "ClevoControl") =>
        await DialogAsync(owner, text, title, [Loc.T("Ja", "Yes"), Loc.T("Nein", "No")]) == 0;

    /// <summary>Dialog with buttons; returns the index of the clicked button (-1 when closed).</summary>
    public static async Task<int> DialogAsync(Window? owner, string text, string title, string[] buttons)
    {
        var result = -1;
        var w = new Window
        {
            Title = title, Width = 460, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = owner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = Button(buttons[i], () => { result = index; w.Close(); }, accent: i == 0);
            b.MinWidth = 90;
            row.Children.Add(b);
        }
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
        body.Children.Add(Text(text));
        body.Children.Add(row);
        w.Content = body;
        SetRes(w, Window.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        if (owner is { IsVisible: true }) await w.ShowDialog(owner);
        else
        {
            var done = new TaskCompletionSource();
            w.Closed += (_, _) => done.TrySetResult();
            w.Show();
            await done.Task;
        }
        return result;
    }

    /// <summary>Opens a file, folder or URL with the default application.</summary>
    public static void Open(string target) => Core.Platform.Shell.Open(target);
}
