using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClevoControl.Core;

namespace ClevoControl.App;

/// <summary>Tiny two-language localisation: T("Deutsch", "English").</summary>
public static class Loc
{
    public static bool German { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    public static void Init(string setting) =>
        German = setting switch
        {
            "de" => true,
            "en" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de",
        };

    public static string T(string de, string en) => German ? de : en;
}

/// <summary>Segoe Fluent Icons code points used across the app.</summary>
public static class Icons
{
    public const string Home = "", Settings = "", Game = "", Keyboard = "", Color = "",
        Brightness = "", Lightning = "", Battery = "", BatterySaver = "", Volume = "", Mute = "",
        Microphone = "", Speaker = "", Star = "", Music = "", Video = "", Power = "",
        Tune = "", Timer = "", Heart = "", Thermometer = "", Gauge = "", Lamp = "",
        Bulb = "", Shuffle = "", Pulse = "", Diagnostic = "", Monitor = "", Chip = "",
        Laptop = "", Swap = "", Bell = "", Layers = "", Frame = "", Fire = "",
        Chart = "", Puzzle = "", History = "", Info = "", Warning = "", Add = "",
        Delete = "", Edit = "", Up = "", Refresh = "", OpenExternal = "", Broadcast = "",
        Circle = "", Sparkle = "", Plug = "", Moon = "", Shield = "", Target = "",
        Code = "", Save = "", Fan = "", Max = "", Auto = "", Briefcase = "", Brush = "";
}

/// <summary>Helpers to build the pages in code, styled after the Windows 11 Settings app.</summary>
public static class Ui
{
    // Fluent theme resource keys
    public const string TextPrimary = "TextFillColorPrimaryBrush";
    public const string TextSecondary = "TextFillColorSecondaryBrush";
    public const string CardBg = "CardBackgroundFillColorDefaultBrush";
    public const string CardStroke = "CardStrokeColorDefaultBrush";
    public const string Accent = "AccentFillColorDefaultBrush";

    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    public static SolidColorBrush ToBrush(this RgbColor c) => new(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));

    public static Color ToWpf(this RgbColor c) => System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);

    public static TextBlock Text(string text, double size = 14, bool bold = false, bool sub = false, double bottom = 0)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, bottom),
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, sub ? TextSecondary : TextPrimary);
        return t;
    }

    public static TextBlock Icon(string glyph, double size = 16, string brush = TextPrimary)
    {
        var t = new TextBlock
        {
            Text = glyph,
            FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock PageTitle(string title) => Text(title, 28, true, bottom: 20);

    public static TextBlock PageSubtitle(string text) => text.Length == 0 ? new TextBlock() : Text(text, 14, sub: true, bottom: 20);

    public static TextBlock Section(string text) => Text(text, 14, true, bottom: 8);

    /// <summary>
    /// A group of settings: optional section header above, content inside a Windows-style card.
    /// </summary>
    public static Border Card(UIElement content, string? title = null, string? subtitle = null, string? icon = null)
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

    public static Border CardBorder(UIElement child, Thickness margin, Thickness? padding = null)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = padding ?? new Thickness(16, 14, 16, 14),
            Margin = margin,
            Child = child,
        };
        b.SetResourceReference(Border.BackgroundProperty, CardBg);
        b.SetResourceReference(Border.BorderBrushProperty, CardStroke);
        return b;
    }

    /// <summary>One Windows-Settings-style row as its own card: icon, title, description, control on the right.</summary>
    public static Border SettingsCard(string icon, string title, string? description, UIElement? control)
    {
        var g = new Grid { MinHeight = 40 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
            if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
            if (control is CheckBox cb) cb.Margin = new Thickness(0);
            Grid.SetColumn(control, 2);
            g.Children.Add(control);
        }
        return CardBorder(g, new Thickness(0, 0, 0, 4), new Thickness(16, 12, 16, 12));
    }

    public static StackPanel Stack(params UIElement[] children)
    {
        var s = new StackPanel();
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    public static WrapPanel Wrap(params UIElement[] children)
    {
        var w = new WrapPanel();
        foreach (var c in children) w.Children.Add(c);
        return w;
    }

    /// <summary>Label left, control right, optional description below the label.</summary>
    public static Grid Row(string label, UIElement control, string? description = null, double bottom = 14)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, bottom) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Text(label));
        if (description is not null) left.Children.Add(Text(description, 12, sub: true));
        g.Children.Add(left);
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(control);
        return g;
    }

    /// <summary>A bare Windows 11 toggle switch with "On"/"Off" text.</summary>
    public static CheckBox Toggle(bool value, Action<bool> changed)
    {
        var cb = new CheckBox { IsChecked = value, Style = (Style)Application.Current.FindResource("ToggleSwitch") };
        void Label() => cb.Tag = cb.IsChecked == true ? Loc.T("Ein", "On") : Loc.T("Aus", "Off");
        Label();
        cb.Checked += (_, _) => { Label(); changed(true); };
        cb.Unchecked += (_, _) => { Label(); changed(false); };
        return cb;
    }

    /// <summary>Toggle switch with title and description (used inside cards).</summary>
    public static Grid Switch(string label, bool value, Action<bool> changed, string? description = null) =>
        Row(label, Toggle(value, changed), description);

    /// <summary>Slider with a label and live value text.</summary>
    public static FrameworkElement SliderRow(string label, double min, double max, double value, Action<double> changed,
        Func<double, string>? format = null, double step = 1, Brush? track = null, double bottom = 12)
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
            IsSnapToTickEnabled = step >= 1, TickFrequency = step, Margin = new Thickness(0, 4, 0, 0), IsMoveToPointEnabled = true,
        };
        if (track is not null)
        {
            slider.Style = (Style)Application.Current.FindResource("GradientSlider");
            slider.Background = track;
        }
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = format(e.NewValue);
            changed(e.NewValue);
        };
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, bottom) };
        s.Children.Add(header);
        s.Children.Add(slider);
        s.Tag = slider;
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
        if (icon is not null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icon(icon, 14, accent ? "TextOnAccentFillColorPrimaryBrush" : TextPrimary);
            ic.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(ic);
            sp.Children.Add(new TextBlock { Text = text });
            content = sp;
        }
        var b = new Button { Content = content, Margin = new Thickness(0, 0, 8, 8), MinWidth = 32 };
        if (accent) b.Style = (Style)Application.Current.FindResource("AccentButtonStyle");
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
            Style = (Style)Application.Current.FindResource("TileRadio"),
            Content = s,
            GroupName = group,
            IsChecked = isChecked,
            Width = width,
        };
        r.Checked += (_, _) => select();
        return r;
    }

    public static Border Swatch(RgbColor c, double size, Action? click = null, string? tooltip = null)
    {
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
            Background = c.ToBrush(), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 8),
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tooltip ?? c.ToHex(),
        };
        b.SetResourceReference(Border.BorderBrushProperty, "ControlStrongStrokeColorDefaultBrush");
        if (click is not null) b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    public static LinearGradientBrush HueBrush()
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        for (var i = 0; i <= 6; i++) b.GradientStops.Add(new GradientStop(RgbColor.FromHsv(i * 60, 1, 1).ToWpf(), i / 6.0));
        return b;
    }

    public static LinearGradientBrush Gradient(Color a, Color b) => new(a, b, new Point(0, 0), new Point(1, 0));

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
        b.SetResourceReference(Border.BackgroundProperty, warning ? "SystemFillColorCautionBackgroundBrush" : "SystemFillColorAttentionBackgroundBrush");
        b.SetResourceReference(Border.BorderBrushProperty, CardStroke);
        return b;
    }
}
