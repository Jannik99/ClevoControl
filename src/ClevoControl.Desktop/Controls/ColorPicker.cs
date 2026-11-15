using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClevoControl.Core;

namespace ClevoControl.Desktop.Controls;

/// <summary>Saturation/value square + hue slider + RGB sliders + hex field.</summary>
public sealed class ColorPicker : StackPanel
{
    private readonly Border _square;
    private readonly Ellipse _marker;
    private readonly Slider _hue;
    private readonly TextBox _hex;
    private readonly Slider _r, _g, _b;
    private double _h, _s, _v;
    private bool _updating;

    public event Action<RgbColor>? ColorChanged;

    public ColorPicker(RgbColor initial)
    {
        (_h, _s, _v) = initial.ToHsv();

        _square = new Border
        {
            Height = 170, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            Cursor = new Cursor(StandardCursorType.Cross),
        };
        var white = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        };
        var black = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) },
        };
        var canvas = new Canvas();
        _marker = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false };
        canvas.Children.Add(_marker);
        var layers = new Grid();
        layers.Children.Add(new Border { Background = white });
        layers.Children.Add(new Border { Background = black });
        layers.Children.Add(canvas);
        _square.Child = layers;
        _square.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_square).Properties.IsLeftButtonPressed) return;
            e.Pointer.Capture(_square);
            PickSv(e.GetPosition(_square));
        };
        _square.PointerMoved += (_, e) =>
        {
            if (e.Pointer.Captured == _square && e.GetCurrentPoint(_square).Properties.IsLeftButtonPressed)
                PickSv(e.GetPosition(_square));
        };
        _square.PointerReleased += (_, e) => e.Pointer.Capture(null);
        _square.SizeChanged += (_, _) => UpdateVisuals();
        Children.Add(_square);

        _hue = new Slider { Minimum = 0, Maximum = 359, Value = _h };
        _hue.ValueChanged += (_, e) =>
        {
            if (_updating) return;
            _h = e.NewValue;
            if (_s < 0.05) _s = 1;
            if (_v < 0.05) _v = 1;
            Emit();
        };
        var hueHost = GradientSlider(_hue, Ui.HueBrush());
        hueHost.Margin = new Thickness(0, 12, 0, 6);
        Children.Add(hueHost);

        _r = Channel("R", Colors.Red);
        _g = Channel("G", Colors.Lime);
        _b = Channel("B", Color.FromRgb(40, 120, 255));

        var hexRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var hexLabel = Ui.Text("HEX", 12, true, sub: true);
        hexLabel.Width = 28;
        hexLabel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hexLabel, Dock.Left);
        hexRow.Children.Add(hexLabel);
        _hex = new TextBox
        {
            Text = initial.ToHex(), FontFamily = new FontFamily("Consolas, DejaVu Sans Mono, monospace"),
            Width = 110, HorizontalAlignment = HorizontalAlignment.Left,
        };
        _hex.LostFocus += (_, _) => ApplyHex();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        hexRow.Children.Add(_hex);
        Children.Add(hexRow);

        UpdateVisuals();
    }

    /// <summary>Slider drawn over a thin gradient track (same approach as <see cref="Ui.SliderRow"/>).</summary>
    private static Panel GradientSlider(Slider slider, IBrush track)
    {
        slider.Background = track;
        slider.Classes.Add("gradient");
        return new Panel
        {
            Children =
            {
                new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = track, VerticalAlignment = VerticalAlignment.Center },
                slider,
            },
        };
    }

    private Slider Channel(string name, Color color)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var label = Ui.Text(name, 12, true, sub: true);
        label.Width = 28;
        label.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(label, Dock.Left);
        var s = new Slider { Minimum = 0, Maximum = 255, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1 };
        s.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            var c = new RgbColor((byte)_r!.Value, (byte)_g!.Value, (byte)_b!.Value);
            (_h, _s, _v) = c.ToHsv();
            Emit(fromRgb: true);
        };
        row.Children.Add(label);
        row.Children.Add(GradientSlider(s, Ui.Gradient(Colors.Black, color)));
        Children.Add(row);
        return s;
    }

    public RgbColor SelectedColor => RgbColor.FromHsv(_h, _s, _v);

    public void SetColor(RgbColor c)
    {
        (_h, _s, _v) = c.ToHsv();
        UpdateVisuals();
    }

    private void ApplyHex()
    {
        if (RgbColor.TryParse(_hex.Text ?? "", out var c))
        {
            (_h, _s, _v) = c.ToHsv();
            Emit();
        }
        else _hex.Text = SelectedColor.ToHex();
    }

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / Math.Max(1, _square.Bounds.Width), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(1, _square.Bounds.Height), 0, 1);
        Emit();
    }

    private void Emit(bool fromRgb = false)
    {
        UpdateVisuals(fromRgb);
        ColorChanged?.Invoke(SelectedColor);
    }

    private void UpdateVisuals(bool fromRgb = false)
    {
        _updating = true;
        _square.Background = RgbColor.FromHsv(_h, 1, 1).ToBrush();
        Canvas.SetLeft(_marker, _s * _square.Bounds.Width - 8);
        Canvas.SetTop(_marker, (1 - _v) * _square.Bounds.Height - 8);
        _marker.Fill = SelectedColor.ToBrush();
        _hue.Value = _h;
        var c = SelectedColor;
        if (!fromRgb)
        {
            _r.Value = c.R;
            _g.Value = c.G;
            _b.Value = c.B;
        }
        if (!_hex.IsFocused) _hex.Text = c.ToHex();
        _updating = false;
    }
}
