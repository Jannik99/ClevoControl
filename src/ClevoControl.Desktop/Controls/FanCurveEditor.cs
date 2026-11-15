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
/// Keyboard: Tab or Left/Right selects a point, Up/Down changes the duty by 5 % (Shift: 1 %),
/// Ctrl+Left/Right moves the temperature by 1 °C.
/// </summary>
public sealed class FanCurveEditor : Control
{
    private const double MinT = 30, MaxT = 100, PadL = 40, PadB = 26, PadT = 10, PadR = 12;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private readonly List<FanCurvePoint> _points;
    private int _drag = -1;
    /// <summary>Point selected with the keyboard (index into the list).</summary>
    private int _sel = -1;
    private bool _pointerFocus;
    /// <summary>Focus came from the keyboard: show the focus visual and the value of the selected point.</summary>
    private bool _keyboardFocus;

    public FanCurveEditor(List<FanCurvePoint> points)
    {
        _points = points;
        Height = 240;
        Cursor = ArrowCursor;
        Focusable = true;
        IsTabStop = true;
        GotFocus += (_, _) =>
        {
            _keyboardFocus = !_pointerFocus;
            _pointerFocus = false;
            if (_sel < 0 || _sel >= _points.Count) _sel = Ordered().FirstOrDefault() is { } first ? _points.IndexOf(first) : -1;
            InvalidateVisual();
        };
        LostFocus += (_, _) =>
        {
            _keyboardFocus = false;
            InvalidateVisual();
        };
    }

    public Color Accent { get; set; } = Color.FromRgb(76, 141, 255);
    public int? CurrentTemperature { get; set; }

    /// <summary>Text in front of the temperature of the marker, e.g. "max(CPU,GPU) ".</summary>
    public string? MarkerPrefix { get; set; }

    public event Action? Changed;

    private Rect Plot => new(PadL, PadT, Math.Max(10, Bounds.Width - PadL - PadR), Math.Max(10, Bounds.Height - PadT - PadB));

    private List<FanCurvePoint> Ordered() => _points.OrderBy(p => p.Temperature).ToList();

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

    private IBrush Res(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

    public override void Render(DrawingContext dc)
    {
        var r = Plot;
        // Transparent background so the whole area receives pointer input.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Bounds.Width, Bounds.Height));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)), 1);
        var label = Res(Ui.TextSecondary, new SolidColorBrush(Color.FromRgb(149, 157, 171)));

        for (var d = 0; d <= 100; d += 25)
        {
            var y = ToScreen(MinT, d).Y;
            dc.DrawLine(grid, new Point(r.Left, y), new Point(r.Right, y));
            var ft = Label($"{d}%", 11, label);
            dc.DrawText(ft, new Point(PadL - 6 - ft.Width, y - ft.Height / 2));
        }
        for (var t = 30; t <= 100; t += 10)
        {
            var x = ToScreen(t, 0).X;
            dc.DrawLine(grid, new Point(x, r.Top), new Point(x, r.Bottom));
            // Centred on the gridline; the last one right-aligned so it stays inside the plot.
            var ft = Label($"{t}°", 11, label);
            dc.DrawText(ft, new Point(t == 100 ? x - ft.Width : x - ft.Width / 2, r.Bottom + 6));
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

        if (CurrentTemperature is { } ct && ct >= MinT)
        {
            var x = ToScreen(Math.Min(ct, MaxT), 0).X;
            var orange = new SolidColorBrush(Color.FromRgb(255, 181, 71));
            dc.DrawLine(new Pen(orange, 1.5, DashStyle.Dash), new Point(x, r.Top), new Point(x, r.Bottom));
            var ft = Label($"{MarkerPrefix}{ct} °C", 11, orange);
            dc.DrawText(ft, new Point(x + 4 + ft.Width > r.Right ? x - 4 - ft.Width : x + 4, r.Top));
        }

        // Curve
        var pts = Ordered();
        var accent = new SolidColorBrush(Accent);
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
            dc.DrawGeometry(null, new Pen(accent, 2.5) { LineJoin = PenLineJoin.Round }, curve);
            var ring = new Pen(accent, 3);
            foreach (var p in _points)
                dc.DrawEllipse(Brushes.White, ring, ToScreen(p.Temperature, p.Duty), 6, 6);
        }

        if (_keyboardFocus)
        {
            // Focus visual: frame around the plot and a halo around the selected point.
            var focus = new Pen(Res(Ui.TextPrimary, Brushes.White), 1.5);
            dc.DrawRectangle(null, focus, new Rect(r.Left - 3, r.Top - 3, r.Width + 6, r.Height + 6), 4, 4);
            if (_sel >= 0 && _sel < _points.Count)
                dc.DrawEllipse(null, focus, ToScreen(_points[_sel].Temperature, _points[_sel].Duty), 10, 10);
        }

        // Value of the point being dragged (or selected with the keyboard).
        var shown = _drag >= 0 ? _drag : _keyboardFocus ? _sel : -1;
        if (shown >= 0 && shown < _points.Count)
        {
            var p = _points[shown];
            var sp = ToScreen(p.Temperature, p.Duty);
            var ft = Label($"{p.Temperature} °C → {p.Duty} %", 12, Res(Ui.TextPrimary, Brushes.White));
            var w = ft.Width + 12;
            var h = ft.Height + 6;
            var x = sp.X + 12 + w > Bounds.Width ? sp.X - 12 - w : sp.X + 12;
            var y = Math.Clamp(sp.Y - 12 - h, 0, Math.Max(0, Bounds.Height - h));
            var box = new Rect(x, y, w, h);
            dc.DrawRectangle(Res("SolidBackgroundFillColorBaseBrush", Brushes.Black), new Pen(accent, 1), box, 4, 4);
            dc.DrawText(ft, new Point(x + 6, y + 3));
        }
    }

    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private bool NearPoint(Point pos) => _points.Any(p => Dist(ToScreen(p.Temperature, p.Duty), pos) < 12);

    /// <summary>Lowest and highest temperature the point may have (between its neighbours).</summary>
    private (int Lo, int Hi) Limits(FanCurvePoint point)
    {
        var ordered = Ordered();
        var idx = ordered.IndexOf(point);
        var lo = idx > 0 ? ordered[idx - 1].Temperature + 1 : (int)MinT;
        var hi = idx < ordered.Count - 1 ? ordered[idx + 1].Temperature - 1 : (int)MaxT;
        return (lo, hi);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var pos = e.GetPosition(this);
        // Focus by pointer: no focus visual.
        if (!IsFocused)
        {
            _pointerFocus = true;
            Focus();
            _pointerFocus = false;
        }
        _keyboardFocus = false;
        InvalidateVisual();
        _drag = -1;
        for (var i = 0; i < _points.Count; i++)
        {
            var sp = ToScreen(_points[i].Temperature, _points[i].Duty);
            if (Dist(sp, pos) < 12) { _drag = i; break; }
        }
        if (_drag >= 0)
        {
            _sel = _drag;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        Cursor = _drag >= 0 || NearPoint(pos) ? HandCursor : ArrowCursor;
        if (_drag < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var (t, d) = FromScreen(pos);
        var (lo, hi) = Limits(_points[_drag]);
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
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_drag < 0) return;
        _drag = -1;
        InvalidateVisual();
        Changed?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _points.Count == 0) return;
        var ordered = Ordered();
        if (_sel < 0 || _sel >= _points.Count) _sel = _points.IndexOf(ordered[0]);
        var pos = ordered.IndexOf(_points[_sel]);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var point = _points[_sel];
        var changed = false;
        switch (e.Key)
        {
            // Tab walks through the points and leaves the chart after the last (or before the first) one.
            case Key.Tab when !shift && pos < ordered.Count - 1:
            case Key.Right when !ctrl && pos < ordered.Count - 1:
                _sel = _points.IndexOf(ordered[pos + 1]);
                break;
            case Key.Tab when shift && pos > 0:
            case Key.Left when !ctrl && pos > 0:
                _sel = _points.IndexOf(ordered[pos - 1]);
                break;
            case Key.Left or Key.Right when !ctrl:
                break; // first / last point: keep the key from scrolling the page
            case Key.Left or Key.Right:
            {
                var (lo, hi) = Limits(point);
                var t = Math.Clamp(point.Temperature + (e.Key == Key.Right ? 1 : -1), lo, hi);
                changed = t != point.Temperature;
                point.Temperature = t;
                break;
            }
            case Key.Up or Key.Down:
            {
                var step = (shift ? 1 : 5) * (e.Key == Key.Up ? 1 : -1);
                var d = Math.Clamp(point.Duty + step, 0, 100);
                changed = d != point.Duty;
                point.Duty = d;
                break;
            }
            default:
                return;
        }
        e.Handled = true;
        _keyboardFocus = true;
        InvalidateVisual();
        if (changed) Changed?.Invoke();
    }
}
