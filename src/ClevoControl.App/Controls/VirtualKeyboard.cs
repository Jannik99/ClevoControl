using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClevoControl.App.Controls;

/// <summary>
/// Clickable German (QWERTZ, ISO) laptop keyboard. Mapped keys are highlighted, the selected key is
/// outlined. Modifier keys are not selectable (they are used as modifiers of a mapping).
/// </summary>
public sealed class VirtualKeyboard : Viewbox
{
    private const double Unit = 44, Gap = 4;
    private readonly Dictionary<int, Border> _keys = [];
    private readonly Canvas _canvas = new();
    private HashSet<int> _mapped = [];
    private int? _selected;

    public event Action<int, string>? KeySelected;

    // (label, virtual-key code or 0 = not selectable, width in units)
    private static readonly (string Label, int Vk, double W)[][] MainRows =
    [
        [("Esc", 0x1B, 1), ("F1", 0x70, 1), ("F2", 0x71, 1), ("F3", 0x72, 1), ("F4", 0x73, 1), ("F5", 0x74, 1), ("F6", 0x75, 1), ("F7", 0x76, 1),
         ("F8", 0x77, 1), ("F9", 0x78, 1), ("F10", 0x79, 1), ("F11", 0x7A, 1), ("F12", 0x7B, 1), ("Druck", 0x2C, 1), ("Einfg", 0x2D, 1), ("Entf", 0x2E, 1)],
        [("^", 0xDC, 1), ("1", 0x31, 1), ("2", 0x32, 1), ("3", 0x33, 1), ("4", 0x34, 1), ("5", 0x35, 1), ("6", 0x36, 1), ("7", 0x37, 1), ("8", 0x38, 1),
         ("9", 0x39, 1), ("0", 0x30, 1), ("ß", 0xDB, 1), ("´", 0xDD, 1), ("⌫", 0x08, 3)],
        [("Tab", 0x09, 1.5), ("Q", 0x51, 1), ("W", 0x57, 1), ("E", 0x45, 1), ("R", 0x52, 1), ("T", 0x54, 1), ("Z", 0x5A, 1), ("U", 0x55, 1), ("I", 0x49, 1),
         ("O", 0x4F, 1), ("P", 0x50, 1), ("Ü", 0xBA, 1), ("+", 0xBB, 1), ("↵", 0x0D, 2.5)],
        [("⇩ Fest", 0x14, 1.85), ("A", 0x41, 1), ("S", 0x53, 1), ("D", 0x44, 1), ("F", 0x46, 1), ("G", 0x47, 1), ("H", 0x48, 1), ("J", 0x4A, 1), ("K", 0x4B, 1),
         ("L", 0x4C, 1), ("Ö", 0xC0, 1), ("Ä", 0xDE, 1), ("#", 0xBF, 1), ("", 0, 2.15)],
        [("⇧", 0, 1.35), ("<", 0xE2, 1), ("Y", 0x59, 1), ("X", 0x58, 1), ("C", 0x43, 1), ("V", 0x56, 1), ("B", 0x42, 1), ("N", 0x4E, 1), ("M", 0x4D, 1),
         (",", 0xBC, 1), (".", 0xBE, 1), ("-", 0xBD, 1), ("⇧", 0, 2.65)],
        [("Strg", 0, 1.35), ("Fn", 0, 1), ("⊞", 0, 1), ("Alt", 0, 1), ("", 0x20, 5.25), ("AltGr", 0, 1), ("☰", 0x5D, 1), ("Strg", 0, 1.4),
         ("←", 0x25, 1), ("↑", 0x26, 1), ("↓", 0x28, 1), ("→", 0x27, 1)],
    ];

    private static readonly (string Label, int Vk, double W)[][] NumpadRows =
    [
        [("Pos1", 0x24, 1), ("Ende", 0x23, 1), ("Bild↑", 0x21, 1), ("Bild↓", 0x22, 1)],
        [("Num", 0x90, 1), ("/", 0x6F, 1), ("*", 0x6A, 1), ("-", 0x6D, 1)],
        [("7", 0x67, 1), ("8", 0x68, 1), ("9", 0x69, 1), ("+", 0x6B, 1)],
        [("4", 0x64, 1), ("5", 0x65, 1), ("6", 0x66, 1), ("", 0, 1)],
        [("1", 0x61, 1), ("2", 0x62, 1), ("3", 0x63, 1), ("", 0, 1)],
        [("0", 0x60, 2), (",", 0x6E, 1), ("", 0, 1)],
    ];

    public VirtualKeyboard(bool numpad)
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.DownOnly;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _canvas;
        var width = Draw(MainRows, 0);
        if (numpad) width = Draw(NumpadRows, width + Unit * 0.5);
        _canvas.Width = width;
        _canvas.Height = MainRows.Length * (Unit + Gap);
    }

    /// <summary>Display name of a key on this layout (e.g. "Ü", "F5", "Leertaste").</summary>
    public static string LabelFor(int vk)
    {
        if (vk == 0x20) return Loc.T("Leertaste", "Space");
        if (vk == 0x0D) return "Enter";
        if (vk == 0x08) return "Backspace";
        if (vk == 0x14) return Loc.T("Feststelltaste", "Caps Lock");
        if (vk == 0x5D) return Loc.T("Menütaste", "Menu key");
        foreach (var row in MainRows.Concat(NumpadRows))
            foreach (var k in row)
                if (k.Vk == vk && k.Label.Length > 0)
                    return vk is >= 0x60 and <= 0x6F ? "Num " + k.Label : k.Label;
        return Core.Services.KeyRemapService.KeyName(vk);
    }

    private double Draw((string Label, int Vk, double W)[][] rows, double x0)
    {
        double maxX = 0;
        for (var r = 0; r < rows.Length; r++)
        {
            var x = x0;
            foreach (var (label, vk, w) in rows[r])
            {
                var width = w * Unit + (w - 1) * Gap;
                if (label.Length > 0 || vk != 0) _canvas.Children.Add(Key(label, vk, width, x, r * (Unit + Gap)));
                x += width + Gap;
            }
            maxX = Math.Max(maxX, x);
        }
        return maxX;
    }

    private Border Key(string label, int vk, double width, double x, double y)
    {
        var text = new TextBlock
        {
            Text = label, FontSize = label.Length > 3 ? 11 : 14, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var b = new Border
        {
            Width = width, Height = Unit, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = text,
        };
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        if (vk == 0)
        {
            // Modifier / Fn: shown for orientation only.
            b.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            b.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorDisabledBrush");
            b.ToolTip = Loc.T("Zusatztaste – wähle sie unten als „Nur zusammen mit“", "Modifier – choose it below under “Only together with”");
            return b;
        }
        b.Cursor = Cursors.Hand;
        b.ToolTip = LabelFor(vk);
        b.MouseLeftButtonUp += (_, _) => Select(vk);
        b.MouseEnter += (_, _) => { if (_selected != vk && !_mapped.Contains(vk)) b.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush"); };
        b.MouseLeave += (_, _) => Restyle(vk);
        if (!_keys.ContainsKey(vk)) _keys[vk] = b;
        Restyle(vk, b, text);
        return b;
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

    private void Restyle(int vk) { if (_keys.TryGetValue(vk, out var b)) Restyle(vk, b, (TextBlock)b.Child); }

    private void Restyle(int vk, Border b, TextBlock text)
    {
        var mapped = _mapped.Contains(vk);
        var selected = _selected == vk;
        b.SetResourceReference(Border.BackgroundProperty, mapped ? "AccentFillColorDefaultBrush" : "ControlFillColorDefaultBrush");
        text.SetResourceReference(TextBlock.ForegroundProperty, mapped ? "TextOnAccentFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        b.BorderThickness = new Thickness(selected ? 3 : 1);
        b.SetResourceReference(Border.BorderBrushProperty, selected ? "TextFillColorPrimaryBrush" : "ControlStrokeColorDefaultBrush");
    }
}
