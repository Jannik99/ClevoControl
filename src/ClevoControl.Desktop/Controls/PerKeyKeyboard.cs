using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClevoControl.Core;
using ClevoControl.Core.Platform;

namespace ClevoControl.Desktop.Controls;

/// <summary>
/// Keyboard picture for per-key lighting: every key (also modifiers, Fn and the number pad) is filled with its color.
/// Click or drag over keys to paint them, right button (or Delete) erases. Same shape as <see cref="VirtualKeyboard"/>
/// (both are built from <see cref="KeyboardLayout"/>), but keys are identified by their layout-independent
/// <see cref="KeyIds">key id</see> instead of a virtual key.
/// </summary>
public sealed class PerKeyKeyboard : Viewbox
{
    private const double Unit = 44, Gap = 4;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Dictionary<int, KeyView> _views = [];
    private readonly List<KeyRect> _rects = [];
    private readonly Func<int, bool> _hasLed;
    private IReadOnlyDictionary<int, RgbColor> _colors = new Dictionary<int, RgbColor>();
    private bool? _dragErase;
    private int? _hover;

    /// <summary>A drawn key. Fill and stroke are either theme resources (bound) or a key color (set directly).</summary>
    private sealed class KeyView(Control root, TextBlock text, string name, AvaloniaProperty fill, AvaloniaProperty stroke, StyledElement shape)
    {
        private IDisposable? _fillBinding, _strokeBinding, _textBinding;
        public Control Root { get; } = root;
        public string Name { get; } = name;

        public void Paint(IBrush? color, string fallbackFill, double thickness)
        {
            _fillBinding?.Dispose();
            _fillBinding = null;
            if (color is null) _fillBinding = shape.Bind(fill, shape.GetResourceObservable(fallbackFill));
            else shape.SetValue(fill, color);

            _strokeBinding?.Dispose();
            _strokeBinding = shape.Bind(stroke, shape.GetResourceObservable(thickness > 1 ? "TextFillColorPrimaryBrush" : "ControlStrokeColorDefaultBrush"));
            if (shape is Border b) b.BorderThickness = new Thickness(thickness);
            else if (shape is Shape s) s.StrokeThickness = thickness;
        }

        public void Foreground(IBrush? brush)
        {
            _textBinding?.Dispose();
            _textBinding = null;
            if (brush is null) _textBinding = text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("TextFillColorPrimaryBrush"));
            else text.Foreground = brush;
        }
    }

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
            _rects.Add(k);
            Add(k, layout);
        }
        _canvas.Width = keys.Max(k => k.X + k.Width);
        _canvas.Height = keys.Max(k => k.Y + k.Height);
        AutomationProperties.SetName(this, Loc.T("Tastatur zum Einfärben", "Keyboard to paint") + " – " + layout.DisplayName);

        // Avalonia captures the pointer on the pressed key, so dragging is tracked on the canvas with a hit test.
        _canvas.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(_canvas);
            if (!p.Properties.IsLeftButtonPressed && !p.Properties.IsRightButtonPressed) return;
            if (KeyAt(p.Position) is not { } id) return;
            _dragErase = p.Properties.IsRightButtonPressed;
            _views[id].Root.Focus();
            Painted?.Invoke(id, _dragErase.Value);
            _hover = id;
            e.Handled = true;
        };
        _canvas.PointerMoved += (_, e) =>
        {
            var id = KeyAt(e.GetPosition(_canvas));
            if (id == _hover) return;
            var old = _hover;
            _hover = id;
            if (old is { } o) Restyle(o);
            if (id is not { } k) return;
            if (_dragErase is { } erase) Painted?.Invoke(k, erase);
            else Restyle(k);
        };
        _canvas.PointerReleased += (_, _) => _dragErase = null;
        _canvas.PointerExited += (_, _) =>
        {
            _dragErase = null;
            var old = _hover;
            _hover = null;
            if (old is { } o) Restyle(o);
        };
    }

    /// <summary>Ids of all keys that have an LED (for "paint all").</summary>
    public IEnumerable<int> PaintableKeys => _views.Keys.Where(_hasLed);

    public void SetColors(IReadOnlyDictionary<int, RgbColor> colors)
    {
        _colors = colors;
        foreach (var id in _views.Keys) Restyle(id);
    }

    /// <summary>Paintable key under a canvas position (for the ISO Enter, its upper part or the right-aligned lower part).</summary>
    private int? KeyAt(Point p)
    {
        foreach (var k in _rects)
        {
            if (!_hasLed(k.Id)) continue;
            var inUpper = p.X >= k.X && p.X <= k.X + k.Width && p.Y >= k.Y && p.Y <= k.Y + (k.LowerWidth > 0 ? Unit : k.Height);
            var inLower = k.LowerWidth > 0 && p.X >= k.X + k.Width - k.LowerWidth && p.X <= k.X + k.Width && p.Y >= k.Y + Unit && p.Y <= k.Y + k.Height;
            if (inUpper || inLower) return k.Id;
        }
        return null;
    }

    private static string KeyName(KeyRect k, KeyboardLayout layout) => k.Id switch
    {
        KeyIds.Space => layout.KeyLabel(0x20),
        KeyIds.Enter => "Enter",
        KeyIds.NumEnter => "Num Enter",
        KeyIds.LeftShift => Loc.T("Umschalt links", "Left Shift"),
        KeyIds.RightShift => Loc.T("Umschalt rechts", "Right Shift"),
        KeyIds.Win => "Super",
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
        Control root;
        KeyView view;
        var name = KeyName(k, layout);
        if (k.LowerWidth > 0)
        {
            var path = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(KeyboardLayout.IsoEnterPath(k.Width, k.LowerWidth, Unit, Gap, 6)), StrokeThickness = 1 };
            text.Height = Unit;
            text.VerticalAlignment = VerticalAlignment.Top;
            text.Padding = new Thickness(0, 12, 0, 0);
            var grid = new Grid { Width = k.Width, Height = k.Height };
            grid.Children.Add(path);
            grid.Children.Add(text);
            root = grid;
            view = new KeyView(grid, text, name, Shape.FillProperty, Shape.StrokeProperty, path);
        }
        else
        {
            var b = new Border { Width = k.Width, Height = k.Height, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = text };
            root = b;
            view = new KeyView(b, text, name, Border.BackgroundProperty, Border.BorderBrushProperty, b);
        }
        Canvas.SetLeft(root, k.X);
        Canvas.SetTop(root, k.Y);
        _canvas.Children.Add(root);
        _views[k.Id] = view;

        var id = k.Id;
        if (!_hasLed(id))
        {
            root.Opacity = 0.4;
            ToolTip.SetTip(root, name + Loc.T(" – keine LED bekannt", " – no LED known"));
            Restyle(id);
            return;
        }
        root.Cursor = new Cursor(StandardCursorType.Hand);
        root.Focusable = true;
        AutomationProperties.SetName(root, name);
        root.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { Painted?.Invoke(id, false); e.Handled = true; }
            else if (e.Key is Key.Delete or Key.Back) { Painted?.Invoke(id, true); e.Handled = true; }
        };
        root.GotFocus += (_, _) => Restyle(id);
        root.LostFocus += (_, _) => Restyle(id);
        Restyle(id);
    }

    private void Restyle(int id)
    {
        if (!_views.TryGetValue(id, out var v)) return;
        var painted = _colors.TryGetValue(id, out var c);
        var hover = _hover == id;
        var focused = v.Root.IsKeyboardFocusWithin;
        v.Paint(painted ? c.ToBrush() : null, hover ? "ControlFillColorSecondaryBrush" : "ControlFillColorDefaultBrush", focused ? 2.5 : hover ? 1.5 : 1);
        v.Foreground(painted ? Ui.ContrastText(c) : null);
        if (_hasLed(id)) ToolTip.SetTip(v.Root, v.Name + (painted ? " · " + c.ToHex() : Loc.T(" · aus", " · off")));
    }
}
