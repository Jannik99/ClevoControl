using System.Windows;
using System.Windows.Controls;

namespace ClevoControl.App.Controls;

/// <summary>
/// Grid of equal cells whose column count follows the available width:
/// <c>cols = max(1, floor((width + Gap) / (MinItemWidth + Gap)))</c>. Cells stretch to fill the row,
/// all cells of a row get the height of the tallest one, and <see cref="Gap"/> is only placed between cells.
/// Children added here lose their fixed width and margin (e.g. the tile's default 0,0,8,8), so they line up.
/// Same class as in the Avalonia app (src/ClevoControl.Desktop/Controls/TileGrid.cs).
/// </summary>
public sealed class TileGrid : Panel
{
    /// <summary>Smallest cell width; a new column is added once it fits.</summary>
    public double MinItemWidth { get; set; } = 150;

    /// <summary>Space between cells, horizontally and vertically.</summary>
    public double Gap { get; set; } = 8;

    protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        if (visualAdded is FrameworkElement fe)
        {
            fe.Width = double.NaN;
            fe.Margin = default;
            fe.HorizontalAlignment = HorizontalAlignment.Stretch;
            fe.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private List<UIElement> Items() => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

    private int Columns(double width) =>
        double.IsInfinity(width) || width <= 0
            ? Math.Max(1, InternalChildren.Count)
            : Math.Max(1, (int)Math.Floor((width + Gap) / (MinItemWidth + Gap)));

    private double CellWidth(double width, int cols) =>
        double.IsInfinity(width) || width <= 0 ? MinItemWidth : Math.Max(0, (width - Gap * (cols - 1)) / cols);

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items();
        var cols = Columns(availableSize.Width);
        var cellW = CellWidth(availableSize.Width, cols);
        double height = 0;
        for (var start = 0; start < items.Count; start += cols)
        {
            double rowH = 0;
            for (var i = start; i < Math.Min(items.Count, start + cols); i++)
            {
                items[i].Measure(new Size(cellW, double.PositiveInfinity));
                rowH = Math.Max(rowH, items[i].DesiredSize.Height);
            }
            height += rowH + (start > 0 ? Gap : 0);
        }
        var width = double.IsInfinity(availableSize.Width)
            ? Math.Min(items.Count, cols) * (cellW + Gap) - Gap
            : availableSize.Width;
        return new Size(Math.Max(0, width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var items = Items();
        var cols = Columns(finalSize.Width);
        var cellW = CellWidth(finalSize.Width, cols);
        double y = 0;
        for (var start = 0; start < items.Count; start += cols)
        {
            var end = Math.Min(items.Count, start + cols);
            double rowH = 0;
            for (var i = start; i < end; i++) rowH = Math.Max(rowH, items[i].DesiredSize.Height);
            for (var i = start; i < end; i++)
                items[i].Arrange(new Rect((i - start) * (cellW + Gap), y, cellW, rowH));
            y += rowH + Gap;
        }
        return finalSize;
    }
}
