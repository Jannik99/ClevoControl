using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.App.Controls;

/// <summary>
/// Drag the points to edit a fan curve. Shows the hard safety floor (striped) and the current
/// temperature as a vertical marker.
/// </summary>
public sealed class FanCurveEditor : FrameworkElement
{
    private const double MinT = 30, MaxT = 100, PadL = 40, PadB = 26, PadT = 10, PadR = 12;
    private readonly List<FanCurvePoint> _points;
    private int _drag = -1;

    public FanCurveEditor(List<FanCurvePoint> points)
    {
        _points = points;
        Height = 240;
        Cursor = Cursors.Arrow;
        Focusable = true;
    }

    public Color Accent { get; set; } = Color.FromRgb(76, 141, 255);
    public int? CurrentTemperature { get; set; }

    public event Action? Changed;

    private Rect Plot => new(PadL, PadT, Math.Max(10, ActualWidth - PadL - PadR), Math.Max(10, ActualHeight - PadT - PadB));

    private Point ToScreen(double t, double d)
    {
        var r = Plot;
        return new Point(r.Left + (t - MinT) / (MaxT - MinT) * r.Width, r.Bottom - d / 100 * r.Height);
    }

    private (double T, double D) FromScreen(Point p)
    {
        var r = Plot;
        return (MinT + (p.X - r.Left) / r.Width * (MaxT - MinT), (r.Bottom - p.Y) / r.Height * 100);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = Plot;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)), 1);
        var label = new SolidColorBrush(Color.FromRgb(149, 157, 171));
        var tf = new Typeface("Segoe UI");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var d = 0; d <= 100; d += 25)
        {
            var y = ToScreen(MinT, d).Y;
            dc.DrawLine(grid, new Point(r.Left, y), new Point(r.Right, y));
            dc.DrawText(new FormattedText($"{d}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10.5, label, dpi), new Point(2, y - 7));
        }
        for (var t = 30; t <= 100; t += 10)
        {
            var x = ToScreen(t, 0).X;
            dc.DrawLine(grid, new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawText(new FormattedText($"{t}°", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10.5, label, dpi), new Point(x - 9, r.Bottom + 6));
        }

        // Safety floor
        var floor = new StreamGeometry();
        using (var g = floor.Open())
        {
            g.BeginFigure(ToScreen(MinT, 0), true, true);
            for (var t = (int)MinT; t <= MaxT; t++) g.LineTo(ToScreen(t, FanController.Safe(0, t)), true, false);
            g.LineTo(ToScreen(MaxT, 0), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, 255, 90, 95)), null, floor);

        // Curve
        var pts = _points.OrderBy(p => p.Temperature).ToList();
        if (pts.Count > 0)
        {
            var curve = new StreamGeometry();
            using (var g = curve.Open())
            {
                g.BeginFigure(ToScreen(MinT, pts[0].Duty), false, false);
                foreach (var p in pts) g.LineTo(ToScreen(p.Temperature, p.Duty), true, true);
                g.LineTo(ToScreen(MaxT, pts[^1].Duty), true, true);
            }
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Accent), 2.5) { LineJoin = PenLineJoin.Round }, curve);
            foreach (var p in _points)
            {
                var sp = ToScreen(p.Temperature, p.Duty);
                dc.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Accent), 3), sp, 6, 6);
            }
        }

        if (CurrentTemperature is { } ct && ct >= MinT)
        {
            var x = ToScreen(Math.Min(ct, MaxT), 0).X;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(255, 181, 71)), 1.5) { DashStyle = DashStyles.Dash }, new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawText(new FormattedText($"{ct}°C", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, new SolidColorBrush(Color.FromRgb(255, 181, 71)), dpi), new Point(x + 4, r.Top));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);
        _drag = -1;
        for (var i = 0; i < _points.Count; i++)
        {
            var sp = ToScreen(_points[i].Temperature, _points[i].Duty);
            if ((sp - pos).Length < 12) { _drag = i; break; }
        }
        if (_drag >= 0) CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        Cursor = _points.Any(p => (ToScreen(p.Temperature, p.Duty) - pos).Length < 12) || _drag >= 0 ? Cursors.Hand : Cursors.Arrow;
        if (_drag < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var (t, d) = FromScreen(pos);
        var ordered = _points.OrderBy(p => p.Temperature).ToList();
        var idx = ordered.IndexOf(_points[_drag]);
        var lo = idx > 0 ? ordered[idx - 1].Temperature + 1 : (int)MinT;
        var hi = idx < ordered.Count - 1 ? ordered[idx + 1].Temperature - 1 : (int)MaxT;
        _points[_drag].Temperature = (int)Math.Clamp(Math.Round(t), lo, hi);
        _points[_drag].Duty = (int)Math.Clamp(Math.Round(d), 0, 100);
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag >= 0)
        {
            _drag = -1;
            ReleaseMouseCapture();
            Changed?.Invoke();
        }
    }
}
