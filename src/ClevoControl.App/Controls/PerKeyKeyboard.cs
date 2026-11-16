using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClevoControl.Core;
using ClevoControl.Core.Platform;

namespace ClevoControl.App.Controls;

/// <summary>
/// Keyboard picture for per-key lighting: every key (also modifiers, Fn and the number pad) is filled with its color.
/// Click or drag over keys to paint them, right button (or Delete) erases. Same shape as <see cref="VirtualKeyboard"/>
/// (both are built from <see cref="KeyboardLayout"/>), but keys are identified by their layout-independent
/// <see cref="KeyIds">key id</see> instead of a virtual key.
/// </summary>
public sealed class PerKeyKeyboard : Viewbox
{
    private const double Unit = 44, Gap = 4;
    private readonly Canvas _canvas = new();
    private readonly Dictionary<int, KeyView> _views = [];
    private readonly Func<int, bool> _hasLed;
    private IReadOnlyDictionary<int, RgbColor> _colors = new Dictionary<int, RgbColor>();
    private bool? _dragErase;

    private sealed record KeyView(FrameworkElement Root, TextBlock Text, string Name, Action<Brush?, double> Paint);

    /// <summary>A key was painted (erase = false) or erased (erase = true).</summary>
    public event Action<int, bool>? Painted;

    public PerKeyKeyboard(Func<int, bool> hasLed)
    {
        _hasLed = hasLed;
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _canvas;
        var layout = KeyboardLayout.Current;
        var keys = KeyboardGeometry.Build(layout, numpad: true, Unit, Gap);
        foreach (var k in keys)
        {
            if (_views.ContainsKey(k.Id)) continue;
            Add(k, layout);
        }
        _canvas.Width = keys.Max(k => k.X + k.Width);
        _canvas.Height = keys.Max(k => k.Y + k.Height);
        AutomationProperties.SetName(this, Loc.T("Tastatur zum Einfärben", "Keyboard to paint") + " – " + layout.DisplayName);
        // Painting by dragging ends when the button is released anywhere.
        PreviewMouseUp += (_, _) => _dragErase = null;
        MouseLeave += (_, _) => _dragErase = null;
    }

    /// <summary>Ids of all keys that have an LED (for "paint all").</summary>
    public IEnumerable<int> PaintableKeys => _views.Keys.Where(_hasLed);

    public void SetColors(IReadOnlyDictionary<int, RgbColor> colors)
    {
        _colors = colors;
        foreach (var id in _views.Keys) Restyle(id);
    }

    private static string KeyName(KeyRect k, KeyboardLayout layout) => k.Id switch
    {
        KeyIds.Space => layout.KeyLabel(0x20),
        KeyIds.Enter or KeyIds.NumEnter => k.Id == KeyIds.NumEnter ? "Num Enter" : "Enter",
        KeyIds.LeftShift => Loc.T("Umschalt links", "Left Shift"),
        KeyIds.RightShift => Loc.T("Umschalt rechts", "Right Shift"),
        KeyIds.Win => "Windows",
        KeyIds.Menu => layout.KeyLabel(0x5D),
        _ => k.Label,
    };

    private void Add(KeyRect k, KeyboardLayout layout)
    {
        var text = new TextBlock
        {
            Text = k.Label, FontSize = k.Label.Length > 3 ? 11 : 14, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        FrameworkElement root;
        Action<Brush?, double> paint;
        if (k.LowerWidth > 0)
        {
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(KeyboardLayout.IsoEnterPath(k.Width, k.LowerWidth, Unit, Gap, 6)), StrokeThickness = 1 };
            text.Height = Unit;
            text.VerticalAlignment = VerticalAlignment.Top;
            text.Padding = new Thickness(0, 12, 0, 0);
            var grid = new Grid { Width = k.Width, Height = k.Height, Background = Brushes.Transparent };
            grid.Children.Add(path);
            grid.Children.Add(text);
            root = grid;
            paint = (fill, thickness) =>
            {
                if (fill is null) path.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ControlFillColorDefaultBrush");
                else path.Fill = fill;
                path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, thickness > 1 ? "TextFillColorPrimaryBrush" : "ControlStrokeColorDefaultBrush");
                path.StrokeThickness = thickness;
            };
        }
        else
        {
            var b = new Border { Width = k.Width, Height = k.Height, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = text };
            root = b;
            paint = (fill, thickness) =>
            {
                if (fill is null) b.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
                else b.Background = fill;
                b.SetResourceReference(Border.BorderBrushProperty, thickness > 1 ? "TextFillColorPrimaryBrush" : "ControlStrokeColorDefaultBrush");
                b.BorderThickness = new Thickness(thickness);
            };
        }
        Canvas.SetLeft(root, k.X);
        Canvas.SetTop(root, k.Y);
        _canvas.Children.Add(root);
        var name = KeyName(k, layout);
        var view = new KeyView(root, text, name, paint);
        _views[k.Id] = view;

        var id = k.Id;
        if (!_hasLed(id))
        {
            root.Opacity = 0.4;
            root.ToolTip = name + Loc.T(" – keine LED bekannt", " – no LED known");
            Restyle(id);
            return;
        }
        root.Cursor = Cursors.Hand;
        root.Focusable = true;
        AutomationProperties.SetName(root, name);
        root.MouseDown += (_, e) =>
        {
            if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
            _dragErase = e.ChangedButton == MouseButton.Right;
            root.Focus();
            Painted?.Invoke(id, _dragErase.Value);
            e.Handled = true;
        };
        root.MouseEnter += (_, e) =>
        {
            if (_dragErase is { } erase && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed))
                Painted?.Invoke(id, erase);
            else
            {
                _dragErase = null;
                Restyle(id, hover: true);
            }
        };
        root.MouseLeave += (_, _) => Restyle(id);
        root.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { Painted?.Invoke(id, false); e.Handled = true; }
            else if (e.Key is Key.Delete or Key.Back) { Painted?.Invoke(id, true); e.Handled = true; }
        };
        root.GotKeyboardFocus += (_, _) => Restyle(id);
        root.LostKeyboardFocus += (_, _) => Restyle(id);
        // Right-click erases; no context menu.
        root.ContextMenuOpening += (_, e) => e.Handled = true;
        Restyle(id);
    }

    private void Restyle(int id, bool hover = false)
    {
        if (!_views.TryGetValue(id, out var v)) return;
        var painted = _colors.TryGetValue(id, out var c);
        var focused = v.Root.IsKeyboardFocused;
        v.Paint(painted ? c.ToBrush() : hover ? (Brush)FindResource("ControlFillColorSecondaryBrush") : null, focused ? 2.5 : hover ? 1.5 : 1);
        if (painted) v.Text.Foreground = Ui.ContrastText(c);
        else v.Text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        if (_hasLed(id)) v.Root.ToolTip = v.Name + (painted ? " · " + c.ToHex() : Loc.T(" · aus", " · off"));
    }
}
