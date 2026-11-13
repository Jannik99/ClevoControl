using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClevoControl.Core.Lighting;

namespace ClevoControl.App.Controls;

/// <summary>
/// Semi-transparent overlay over one monitor. The user drags a rectangle around the health/mana
/// bar; the result is returned as relative coordinates (0..1).
/// </summary>
public sealed class RegionSelectWindow : Window
{
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) };
    private readonly Rectangle _rect = new() { Stroke = Brushes.Lime, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)), Visibility = Visibility.Collapsed };
    private Point _start;
    private readonly MonitorInfo _monitor;

    public Rect? Result { get; private set; }

    public RegionSelectWindow(MonitorInfo monitor)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;

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
        };
        Canvas.SetLeft(hint, 30);
        Canvas.SetTop(hint, 30);
        _canvas.Children.Add(hint);
        _canvas.Children.Add(_rect);
        Content = _canvas;

        Loaded += (_, _) =>
        {
            // Position on the chosen monitor (convert physical pixels to DIPs).
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = monitor.X / dpi.DpiScaleX;
            Top = monitor.Y / dpi.DpiScaleY;
            Width = monitor.Width / dpi.DpiScaleX;
            Height = monitor.Height / dpi.DpiScaleY;
            Activate();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_canvas);
            _rect.Visibility = Visibility.Visible;
            Update(_start);
            CaptureMouse();
        };
        MouseMove += (_, e) => { if (IsMouseCaptured) Update(e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, e) =>
        {
            ReleaseMouseCapture();
            var end = e.GetPosition(_canvas);
            var r = new Rect(_start, end);
            if (r.Width >= 4 && r.Height >= 2)
                Result = new Rect(r.X / ActualWidth, r.Y / ActualHeight, r.Width / ActualWidth, r.Height / ActualHeight);
            Close();
        };
    }

    private void Update(Point p)
    {
        var r = new Rect(_start, p);
        Canvas.SetLeft(_rect, r.X);
        Canvas.SetTop(_rect, r.Y);
        _rect.Width = r.Width;
        _rect.Height = r.Height;
    }
}
