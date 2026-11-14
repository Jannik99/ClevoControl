using System.Windows;
using System.Windows.Media;

namespace ClevoControl.App.Controls;

/// <summary>Minimal line chart with a soft fill, no dependencies.</summary>
public sealed class Sparkline : FrameworkElement
{
    private IReadOnlyList<double> _values = [];

    public double Min { get; set; }
    public double Max { get; set; } = 100;
    public Color LineColor { get; set; } = Color.FromRgb(76, 141, 255);

    public void SetValues(IReadOnlyList<double> values)
    {
        _values = values;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (_values.Count < 2 || w <= 0 || h <= 0) return;

        var range = Math.Max(1e-6, Max - Min);
        // Few values yet (app just started): stretch them over the full width instead of a stub at the right.
        var slots = Math.Max(_values.Count, 2);
        var step = w / (slots - 1);
        var x0 = 0.0;
        Point P(int i) => new(x0 + i * step, h - Math.Clamp((_values[i] - Min) / range, 0, 1) * (h - 2) - 1);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(0), false, false);
            for (var i = 1; i < _values.Count; i++) g.LineTo(P(i), true, true);
        }
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(x0, h), true, true);
            for (var i = 0; i < _values.Count; i++) g.LineTo(P(i), true, true);
            g.LineTo(new Point(w, h), true, true);
        }
        var grad = new LinearGradientBrush(Color.FromArgb(90, LineColor.R, LineColor.G, LineColor.B), Color.FromArgb(0, LineColor.R, LineColor.G, LineColor.B), 90);
        dc.DrawGeometry(grad, null, fill);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(LineColor), 2) { LineJoin = PenLineJoin.Round }, line);
    }
}
