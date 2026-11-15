using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClevoControl.Core;

namespace ClevoControl.App.Controls;

/// <summary>Saturation/value square + hue slider + hex field.</summary>
public sealed class ColorPicker : StackPanel
{
    private readonly Border _square;
    private readonly Ellipse _marker;
    private readonly Canvas _canvas;
    private readonly Slider _hue;
    private readonly TextBox _hex;
    private readonly Slider _r, _g, _b;
    private double _h, _s, _v;
    private bool _updating;

    public event Action<RgbColor>? ColorChanged;

    public ColorPicker(RgbColor initial)
    {
        (_h, _s, _v) = initial.ToHsv();

        _square = new Border { Height = 170, CornerRadius = new CornerRadius(10), ClipToBounds = true, Cursor = Cursors.Cross };
        var white = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 0));
        var black = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, new Point(0, 0), new Point(0, 1));
        _canvas = new Canvas();
        _marker = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false };
        _canvas.Children.Add(_marker);
        var layers = new Grid();
        layers.Children.Add(new Border { Background = white });
        layers.Children.Add(new Border { Background = black });
        layers.Children.Add(_canvas);
        _square.Child = layers;
        _square.MouseLeftButtonDown += (_, e) => { _square.CaptureMouse(); PickSv(e.GetPosition(_square)); };
        _square.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && _square.IsMouseCaptured) PickSv(e.GetPosition(_square)); };
        _square.MouseLeftButtonUp += (_, _) => _square.ReleaseMouseCapture();
        _square.SizeChanged += (_, _) => UpdateVisuals();
        Children.Add(_square);

        _hue = new Slider { Minimum = 0, Maximum = 359, Value = _h, Background = Ui.HueBrush(), Style = (Style)Application.Current.FindResource("GradientSlider") };
        _hue.ValueChanged += (_, e) => { if (_updating) return; _h = e.NewValue; if (_s < 0.05) _s = 1; if (_v < 0.05) _v = 1; Emit(); };
        // Same 28 px label column as the R/G/B rows so all sliders start at the same x.
        var hueRow = new DockPanel { Margin = new Thickness(0, 12, 0, 6) };
        var hueLabel = Ui.Text("H", 12, true, sub: true);
        hueLabel.Width = 28;
        hueLabel.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hueLabel, Dock.Left);
        hueRow.Children.Add(hueLabel);
        hueRow.Children.Add(_hue);
        Children.Add(hueRow);

        _r = Channel("R", Colors.Red);
        _g = Channel("G", Colors.Lime);
        _b = Channel("B", Color.FromRgb(40, 120, 255));

        var hexRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        hexRow.Children.Add(Ui.Text("HEX", 12, true, sub: true));
        DockPanel.SetDock(hexRow.Children[0], Dock.Left);
        ((TextBlock)hexRow.Children[0]).Width = 28;
        ((TextBlock)hexRow.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        _hex = new TextBox { Text = initial.ToHex(), FontFamily = new FontFamily("Consolas"), Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
        _hex.LostFocus += (_, _) => ApplyHex();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        hexRow.Children.Add(_hex);
        Children.Add(hexRow);

        UpdateVisuals();
    }

    private Slider Channel(string name, Color color)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var label = Ui.Text(name, 12, true, sub: true);
        label.Width = 28;
        label.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(label, Dock.Left);
        var s = new Slider { Minimum = 0, Maximum = 255, IsSnapToTickEnabled = true, TickFrequency = 1, Background = Ui.Gradient(Colors.Black, color), Style = (Style)Application.Current.FindResource("GradientSlider") };
        s.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            var c = new RgbColor((byte)_r!.Value, (byte)_g!.Value, (byte)_b!.Value);
            (_h, _s, _v) = c.ToHsv();
            Emit(fromRgb: true);
        };
        row.Children.Add(label);
        row.Children.Add(s);
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
        if (RgbColor.TryParse(_hex.Text, out var c))
        {
            (_h, _s, _v) = c.ToHsv();
            Emit();
        }
        else _hex.Text = SelectedColor.ToHex();
    }

    private void PickSv(Point p)
    {
        _s = Math.Clamp(p.X / Math.Max(1, _square.ActualWidth), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(1, _square.ActualHeight), 0, 1);
        Emit();
    }

    private void Emit(bool fromRgb = false)
    {
        UpdateVisuals(fromRgb);
        ColorChanged?.Invoke(SelectedColor);
    }

    /// <summary>Top/left of the 16 px marker: its centre stays 8 px inside the square, so the clip does not cut it.</summary>
    private static double MarkerPos(double fraction, double size) =>
        (size < 16 ? fraction * size : Math.Clamp(fraction * size, 8, size - 8)) - 8;

    private void UpdateVisuals(bool fromRgb = false)
    {
        _updating = true;
        _square.Background = new SolidColorBrush(RgbColor.FromHsv(_h, 1, 1).ToWpf());
        Canvas.SetLeft(_marker, MarkerPos(_s, _square.ActualWidth));
        Canvas.SetTop(_marker, MarkerPos(1 - _v, _square.ActualHeight));
        _marker.Fill = new SolidColorBrush(SelectedColor.ToWpf());
        _hue.Value = _h;
        var c = SelectedColor;
        if (!fromRgb)
        {
            _r.Value = c.R;
            _g.Value = c.G;
            _b.Value = c.B;
        }
        if (!_hex.IsKeyboardFocused) _hex.Text = c.ToHex();
        _updating = false;
    }
}
