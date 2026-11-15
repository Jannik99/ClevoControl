using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using ClevoControl.Core.Lighting;
using ClevoControl.Core.Platform;

namespace ClevoControl.Desktop.Controls;

/// <summary>
/// Semi-transparent overlay over one monitor. The user drags a rectangle around the health/mana
/// bar; the result is returned as relative coordinates (0..1).
/// On Wayland a window cannot place itself, so the overlay opens fullscreen on the screen the
/// compositor chooses (usually the current one).
/// </summary>
public sealed class RegionSelectWindow : Window
{
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) };
    private readonly Rectangle _rect = new()
    {
        Stroke = Brushes.Lime, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)), IsVisible = false,
    };
    private Point _start;
    private bool _dragging;

    public Rect? Result { get; private set; }

    public RegionSelectWindow(MonitorInfo monitor)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = new SolidColorBrush(Color.FromRgb(20, 24, 32));
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Cursor = new Cursor(StandardCursorType.Cross);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "ClevoControl";

        // Place the window on the chosen monitor (physical pixels → DIPs via the screen's scaling).
        var scaling = 1.0;
        var screen = Screens.All.FirstOrDefault(s => s.Bounds.X == monitor.X && s.Bounds.Y == monitor.Y)
                     ?? Screens.All.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint(monitor.X + 1, monitor.Y + 1)));
        if (screen is not null) scaling = screen.Scaling;
        Position = new PixelPoint(monitor.X, monitor.Y);
        Width = monitor.Width / scaling;
        Height = monitor.Height / scaling;
        // Wayland ignores Position: fullscreen at least covers a whole screen.
        if (Shell.IsWayland) WindowState = WindowState.FullScreen;

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 24, 32)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10, 16, 10),
            Child = new TextBlock
            {
                Text = Loc.T("Ziehe einen Rahmen eng um die Lebens-/Manaleiste (am besten wenn sie VOLL ist). Esc = Abbrechen",
                             "Drag a tight box around the health/mana bar (ideally while it is FULL). Esc = cancel"),
                Foreground = Brushes.White, FontSize = 15,
            },
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(hint, 30);
        Canvas.SetTop(hint, 30);
        _canvas.Children.Add(hint);
        _canvas.Children.Add(_rect);
        Content = _canvas;

        Opened += (_, _) =>
        {
            if (!Shell.IsWayland) Position = new PixelPoint(monitor.X, monitor.Y);
            Activate();
            Focus();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _canvas.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
            _start = e.GetPosition(_canvas);
            _dragging = true;
            _rect.IsVisible = true;
            Update(_start);
            e.Pointer.Capture(_canvas);
        };
        _canvas.PointerMoved += (_, e) => { if (_dragging) Update(e.GetPosition(_canvas)); };
        _canvas.PointerReleased += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            var end = e.GetPosition(_canvas);
            var r = new Rect(_start, end).Normalize();
            var w = _canvas.Bounds.Width;
            var h = _canvas.Bounds.Height;
            if (r.Width >= 4 && r.Height >= 2 && w > 0 && h > 0)
                Result = new Rect(r.X / w, r.Y / h, r.Width / w, r.Height / h);
            Close();
        };
    }

    /// <summary>Shows the overlay and waits until it is closed. Returns the selected region or null.</summary>
    public Task<Rect?> ShowAsync()
    {
        var done = new TaskCompletionSource<Rect?>();
        Closed += (_, _) => done.TrySetResult(Result);
        Show();
        return done.Task;
    }

    private void Update(Point p)
    {
        var r = new Rect(_start, p).Normalize();
        Canvas.SetLeft(_rect, r.X);
        Canvas.SetTop(_rect, r.Y);
        _rect.Width = r.Width;
        _rect.Height = r.Height;
    }
}
