using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClevoControl.Desktop.Controls;

/// <summary>
/// Clickable laptop keyboard in the user's real layout (QWERTZ, QWERTY, AZERTY …, ISO or ANSI shape,
/// see <see cref="Core.Platform.KeyboardLayout"/>). Mapped keys are highlighted, the selected key is
/// outlined. Modifier keys are not selectable (they are used as modifiers of a mapping).
/// Key codes are virtual keys as stored in the settings (see KeyboardLayout for their meaning per platform).
/// </summary>
public sealed class VirtualKeyboard : Viewbox
{
    private const double Unit = 44, Gap = 4;
    private readonly Dictionary<int, List<KeyView>> _keys = [];
    private readonly Canvas _canvas = new();
    private readonly Core.Platform.KeyboardLayout _layout = Core.Platform.KeyboardLayout.Current;
    private HashSet<int> _mapped = [];
    private int? _selected;

    /// <summary>A drawn key: its element, legend and how to paint it (Border or, for ISO Enter, an L-shaped Path).</summary>
    private sealed record KeyView(Control Root, TextBlock Text, Action<string, string, double> Paint);

    public event Action<int, string>? KeySelected;

    public VirtualKeyboard(bool numpad)
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _canvas;
        var main = _layout.MainRows();
        var width = Draw(main, 0);
        if (numpad) width = Draw(_layout.NumpadRows(), width + Unit * 0.5);
        _canvas.Width = width;
        _canvas.Height = main.Count * (Unit + Gap) - Gap;
        AutomationProperties.SetName(this, Loc.T("Tastatur", "Keyboard") + " – " + _layout.DisplayName);
    }

    /// <summary>Display name of a key on the user's layout (e.g. "Ü", "F5", "Entf").</summary>
    public static string LabelFor(int vk) => Core.Platform.KeyboardLayout.Current.KeyLabel(vk);

    private static double Px(double w) => w * Unit + (w - 1) * Gap;

    private double Draw(IReadOnlyList<IReadOnlyList<Core.Platform.KeyCap>> rows, double x0)
    {
        double maxX = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            var x = x0;
            var y = r * (Unit + Gap);
            foreach (var (label, vk, w) in rows[r])
            {
                var width = Px(w);
                if (vk == 0x0D && label.Length == 0)
                {
                    // lower part of the ISO Enter key – drawn together with the upper part
                }
                else if (vk == 0x0D && r + 1 < rows.Count && rows[r + 1].FirstOrDefault(k => k.Vk == 0x0D && k.Label.Length == 0) is { Width: > 0 } lower)
                {
                    Add(vk, IsoEnter(label, width, Px(lower.Width), x, y));
                }
                else if (label.Length > 0 || vk != 0)
                {
                    Add(vk, Key(label, vk, width, x, y));
                }
                x += width + Gap;
            }
            maxX = Math.Max(maxX, x - Gap);
        }
        return maxX;
    }

    private void Add(int vk, KeyView view)
    {
        _canvas.Children.Add(view.Root);
        if (vk == 0) return;
        if (!_keys.TryGetValue(vk, out var list)) _keys[vk] = list = [];
        list.Add(view);
        var root = view.Root;
        root.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(root, LabelFor(vk));
        root.Focusable = true;
        AutomationProperties.SetName(root, LabelFor(vk));
        root.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Select(vk); };
        root.KeyDown += (_, e) => { if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space) { Select(vk); e.Handled = true; } };
        root.PointerEntered += (_, _) => { if (_selected != vk && !_mapped.Contains(vk)) Paint(vk, "ControlFillColorSecondaryBrush", "ControlStrokeColorDefaultBrush", 1); };
        root.PointerExited += (_, _) => Restyle(vk);
        root.GotFocus += (_, _) => Restyle(vk);
        root.LostFocus += (_, _) => Restyle(vk);
        Restyle(vk);
    }

    private static TextBlock Legend(string label) => new()
    {
        Text = label, FontSize = label.Length > 3 ? 11 : 14, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static KeyView Key(string label, int vk, double width, double x, double y)
    {
        var text = Legend(label);
        var b = new Border
        {
            Width = width, Height = Unit, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = text,
            Background = Brushes.Transparent,
        };
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        var view = new KeyView(b, text, (bg, stroke, thickness) =>
        {
            Ui.SetRes(b, Border.BackgroundProperty, bg);
            Ui.SetRes(b, Border.BorderBrushProperty, stroke);
            b.BorderThickness = new Thickness(thickness);
        });
        if (vk == 0)
        {
            // Modifier / Fn: shown for orientation only (not selectable, but clearly readable).
            Ui.SetRes(b, Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            Ui.SetRes(text, TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            ToolTip.SetTip(b, Loc.T("Zusatztaste – wähle sie unten als „Nur zusammen mit“", "Modifier – choose it below under “Only together with”"));
        }
        return view;
    }

    /// <summary>The tall ISO Enter key: upper part <paramref name="top"/> wide, lower part <paramref name="bottom"/> wide (right-aligned).</summary>
    private static KeyView IsoEnter(string label, double top, double bottom, double x, double y)
    {
        var path = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(Core.Platform.KeyboardLayout.IsoEnterPath(top, bottom, Unit, Gap, 6)), StrokeThickness = 1 };
        var text = Legend(label);
        text.Height = Unit;
        text.VerticalAlignment = VerticalAlignment.Top;
        text.Padding = new Thickness(0, 12, 0, 0);
        var grid = new Grid { Width = top, Height = 2 * Unit + Gap };
        grid.Children.Add(path);
        grid.Children.Add(text);
        Canvas.SetLeft(grid, x);
        Canvas.SetTop(grid, y);
        return new KeyView(grid, text, (bg, stroke, thickness) =>
        {
            Ui.SetRes(path, Shape.FillProperty, bg);
            Ui.SetRes(path, Shape.StrokeProperty, stroke);
            path.StrokeThickness = thickness;
        });
    }

    public void Select(int vk)
    {
        var old = _selected;
        _selected = vk;
        if (old is { } o) Restyle(o);
        Restyle(vk);
        KeySelected?.Invoke(vk, LabelFor(vk));
    }

    public void SetMapped(IEnumerable<int> keys)
    {
        _mapped = keys.ToHashSet();
        foreach (var vk in _keys.Keys) Restyle(vk);
    }

    private void Paint(int vk, string bg, string stroke, double thickness)
    {
        if (!_keys.TryGetValue(vk, out var views)) return;
        foreach (var v in views) v.Paint(bg, stroke, thickness);
    }

    private void Restyle(int vk)
    {
        if (!_keys.TryGetValue(vk, out var views)) return;
        var mapped = _mapped.Contains(vk);
        var focused = views.Any(v => v.Root.IsKeyboardFocusWithin);
        var selected = _selected == vk;
        Paint(vk, mapped ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush",
            selected || focused ? "TextFillColorPrimaryBrush" : "ControlStrokeColorDefaultBrush", selected ? 3 : focused ? 2 : 1);
        foreach (var v in views)
            Ui.SetRes(v.Text, TextBlock.ForegroundProperty, mapped ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
    }
}
