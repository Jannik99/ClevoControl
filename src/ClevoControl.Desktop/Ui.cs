using Avalonia;
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

    /// <summary>A group of settings: optional header, content inside a Windows-style card.</summary>
    public static Border Card(Control content, string? title = null, string? subtitle = null, string? icon = null)
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
        inner.Children.Add(content);
        return CardBorder(inner, new Thickness(0, 0, 0, 8));
    }

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
        return b;
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

    /// <summary>Label left, control right, optional description below the label.</summary>
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
        return g;
    }

    /// <summary>A toggle switch with "On"/"Off" text.</summary>
    public static ToggleSwitch Toggle(bool value, Action<bool> changed)
    {
        var ts = new ToggleSwitch
        {
            IsChecked = value,
            OnContent = Loc.T("Ein", "On"),
            OffContent = Loc.T("Aus", "Off"),
            MinWidth = 0,
        };
        ts.IsCheckedChanged += (_, _) => changed(ts.IsChecked == true);
        return ts;
    }

    /// <summary>Toggle switch with title and description (used inside cards).</summary>
    public static Grid Switch(string label, bool value, Action<bool> changed, string? description = null) =>
        Row(label, Toggle(value, changed), description);

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

    public static Button Button(string text, Action click, bool accent = false, string? icon = null)
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
        var b = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8), MinWidth = 32 };
        if (accent) b.Classes.Add("accent");
        b.Click += (_, _) => click();
        return b;
    }

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
            VerticalAlignment = VerticalAlignment.Top,
        };
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
        if (click is not null) b.PointerReleased += (_, _) => click();
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
