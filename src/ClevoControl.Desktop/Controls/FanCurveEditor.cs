using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ClevoControl.Core.Services;
using ClevoControl.Core.Settings;

namespace ClevoControl.Desktop.Controls;

/// <summary>
/// Drag the points to edit a fan curve. Shows the hard safety floor (red area) and the current
/// temperature as a vertical marker.
/// </summary>
public sealed class FanCurveEditor : Control
{
    private const double MinT = 30, MaxT = 100, PadL = 40, PadB = 26, PadT = 10, PadR = 12;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private readonly List<FanCurvePoint> _points;
    private int _drag = -1;

    public FanCurveEditor(List<FanCurvePoint> points)
    {
        _points = points;
        Height = 240;
        Cursor = ArrowCursor;
        Focusable = true;
    }

    public Color Accent { get; set; } = Color.FromRgb(76, 141, 255);
    public int? CurrentTemperature { get; set; }

    public event Action? Changed;

    private Rect Plot => new(PadL, PadT, Math.Max(10, Bounds.Width - PadL - PadR), Math.Max(10, Bounds.Height - PadT - PadB));

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

    private static FormattedText Label(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    public override void Render(DrawingContext dc)
    {
        var r = Plot;
        // Transparent background so the whole area receives pointer input.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)), 1);
        var label = new SolidColorBrush(Color.FromRgb(149, 157, 171));

        for (var d = 0; d <= 100; d += 25)
        {
            var y = ToScreen(MinT, d).Y;
            dc.DrawLine(grid, new Point(r.Left, y), new Point(r.Right, y));
            dc.DrawText(Label($"{d}%", 10.5, label), new Point(2, y - 7));
        }
        for (var t = 30; t <= 100; t += 10)
        {
            var x = ToScreen(t, 0).X;
            dc.DrawLine(grid, new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawText(Label($"{t}°", 10.5, label), new Point(x - 9, r.Bottom + 6));
        }

        // Safety floor
        var floor = new StreamGeometry();
        using (var g = floor.Open())
        {
            g.BeginFigure(ToScreen(MinT, 0), true);
            for (var t = (int)MinT; t <= MaxT; t++) g.LineTo(ToScreen(t, FanController.Safe(0, t)));
            g.LineTo(ToScreen(MaxT, 0));
            g.EndFigure(true);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(40, 255, 90, 95)), null, floor);

        // Curve
        var pts = _points.OrderBy(p => p.Temperature).ToList();
        if (pts.Count > 0)
        {
            var curve = new StreamGeometry();
            using (var g = curve.Open())
            {
                g.BeginFigure(ToScreen(MinT, pts[0].Duty), false);
                foreach (var p in pts) g.LineTo(ToScreen(p.Temperature, p.Duty));
                g.LineTo(ToScreen(MaxT, pts[^1].Duty));
                g.EndFigure(false);
            }
            var accent = new SolidColorBrush(Accent);
            dc.DrawGeometry(null, new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round }, curve);
            var ring = new Pen(accent, 3);
            foreach (var p in _points)
                dc.DrawEllipse(Brushes.White, ring, ToScreen(p.Temperature, p.Duty), 6, 6);
        }

        if (CurrentTemperature is { } ct && ct >= MinT)
        {
            var x = ToScreen(Math.Min(ct, MaxT), 0).X;
            var orange = new SolidColorBrush(Color.FromRgb(255, 181, 71));
            dc.DrawLine(new Pen(orange, 1.5, DashStyle.Dash), new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawText(Label($"{ct}°C", 11, orange), new Point(x + 4, r.Top));
        }
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private bool NearPoint(Point pos) => _points.Any(p => Dist(ToScreen(p.Temperature, p.Duty), pos) < 12);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var pos = e.GetPosition(this);
        _drag = -1;
        for (var i = 0; i < _points.Count; i++)
        {
            var sp = ToScreen(_points[i].Temperature, _points[i].Duty);
            if (Dist(sp, pos) < 12) { _drag = i; break; }
        }
        if (_drag >= 0)
        {
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        Cursor = _drag >= 0 || NearPoint(pos) ? HandCursor : ArrowCursor;
        if (_drag < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var (t, d) = FromScreen(pos);
        var ordered = _points.OrderBy(p => p.Temperature).ToList();
        var idx = ordered.IndexOf(_points[_drag]);
        var lo = idx > 0 ? ordered[idx - 1].Temperature + 1 : (int)MinT;
        var hi = idx < ordered.Count - 1 ? ordered[idx + 1].Temperature - 1 : (int)MaxT;
        _points[_drag].Temperature = (int)Math.Clamp(Math.Round(t), lo, hi);
        _points[_drag].Duty = (int)Math.Clamp(Math.Round(d), 0, 100);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        _drag = -1;
        e.Pointer.Capture(null);
        Changed?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag < 0) return;
        _drag = -1;
        Changed?.Invoke();
    }
}
