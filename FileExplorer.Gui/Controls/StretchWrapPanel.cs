using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using static Avalonia.Input.NavigationDirection;

namespace FileExplorer.Gui.Controls;

public class StretchWrapPanel : Panel, INavigableContainer
{
    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<StretchWrapPanel, double>(nameof(ItemWidth), 128);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<StretchWrapPanel, double>(nameof(ItemHeight), 136);

    static StretchWrapPanel()
    {
        AffectsMeasure<StretchWrapPanel>(ItemWidthProperty, ItemHeightProperty);
    }

    public double ItemWidth
    {
        get => GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public int Columns { get; private set; } = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        Size itemSize = new(ItemWidth, ItemHeight);
        foreach (Control child in Children)
        {
            child.Measure(itemSize);
        }

        Columns = GetColumnCount(availableSize.Width);
        int rows = (Children.Count + Columns - 1) / Columns;
        double width = double.IsInfinity(availableSize.Width) ? Columns * ItemWidth : availableSize.Width;

        return new(width, rows * ItemHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Columns = GetColumnCount(finalSize.Width);
        double cellWidth = Math.Max(ItemWidth, finalSize.Width / Columns);
        double offset = (cellWidth - ItemWidth) / 2;

        for (int i = 0; i < Children.Count; i++)
        {
            int row = i / Columns;
            int column = i % Columns;
            Children[i].Arrange(new(column * cellWidth + offset, row * ItemHeight, ItemWidth, ItemHeight));
        }

        return finalSize;
    }

    public IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        int count = Children.Count;
        if (count == 0)
        {
            return null;
        }

        int index = from is Control control ? Children.IndexOf(control) : -1;
        if (index < 0)
        {
            return Children[0];
        }

        int target = direction switch
        {
            First => 0,
            Last => count - 1,
            Left or Previous => index - 1,
            Right or Next => index + 1,
            Up => index - Columns,
            Down => GetDownTarget(index, count),
            PageUp => Math.Max(index % Columns, index - Columns * GetPageRows()),
            PageDown => Math.Min(count - 1, index + Columns * GetPageRows()),
            _ => -1,
        };

        if (target < 0 || target >= count || target == index)
        {
            return null;
        }

        return Children[target];
    }

    private int GetDownTarget(int index, int count)
    {
        int target = index + Columns;
        if (target < count)
        {
            return target;
        }

        int lastRow = (count - 1) / Columns;
        return index / Columns < lastRow ? count - 1 : -1;
    }

    private int GetPageRows()
    {
        ScrollViewer? scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        double viewportHeight = scrollViewer?.Viewport.Height ?? Bounds.Height;

        return Math.Max(1, (int) (viewportHeight / ItemHeight));
    }

    private int GetColumnCount(double width)
    {
        if (double.IsInfinity(width) || double.IsNaN(width) || ItemWidth <= 0)
        {
            return Math.Max(1, Children.Count);
        }

        return Math.Max(1, (int) (width / ItemWidth));
    }
}
