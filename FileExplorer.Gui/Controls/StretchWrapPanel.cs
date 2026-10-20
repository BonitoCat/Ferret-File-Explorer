using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Input;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static Avalonia.Input.NavigationDirection;

namespace FileExplorer.Gui.Controls;

public class StretchWrapPanel : VirtualizingPanel
{
    private const int BufferRows = 2;
    private const int BaseNameLines = 2;
    private const double FallbackViewportHeight = 1200;

    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<StretchWrapPanel, double>(nameof(ItemWidth), 128);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<StretchWrapPanel, double>(nameof(ItemHeight), 136);

    public static readonly StyledProperty<int> NameLinesProperty =
        AvaloniaProperty.Register<StretchWrapPanel, int>(nameof(NameLines), BaseNameLines);

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<StretchWrapPanel, Control, object?>("RecycleKey");

    private static readonly object ItemIsOwnContainer = new();

    private readonly Dictionary<int, Control> _realized = new();
    private readonly Dictionary<Control, int> _realizedIndices = new();
    private readonly Dictionary<object, Stack<Control>> _recyclePool = new();

    private Rect _viewport;
    private int _firstRealizedRow = -1;
    private int _lastRealizedRow = -1;
    private bool _isInLayout;

    private double[]? _rowTops;
    private int _rowTopsColumns = -1;
    private bool _rowTopsDirty = true;

    private Control? _focusedElement;
    private int _focusedIndex = -1;
    private Control? _scrollToElement;
    private int _scrollToIndex = -1;

    static StretchWrapPanel()
    {
        AffectsMeasure<StretchWrapPanel>(ItemWidthProperty, ItemHeightProperty, NameLinesProperty);
    }

    public StretchWrapPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
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

    public int NameLines
    {
        get => GetValue(NameLinesProperty);
        set => SetValue(NameLinesProperty, value);
    }

    public int Columns { get; private set; } = 1;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == NameLinesProperty || change.Property == ItemHeightProperty || change.Property == TextElement.FontFamilyProperty)
        {
            _rowTopsDirty = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        IReadOnlyList<object?> items = Items;
        int columns = GetColumnCount(availableSize.Width, items.Count);
        if (columns != Columns)
        {
            KeepAnchorInView(Columns, columns);
        }

        Columns = columns;
        int rows = (items.Count + Columns - 1) / Columns;
        double width = double.IsInfinity(availableSize.Width) ? Columns * ItemWidth : availableSize.Width;
        double height = GetRowTop(GetRowTops(), rows);

        if (ItemContainerGenerator == null)
        {
            return new(width, height);
        }

        _isInLayout = true;
        try
        {
            (int firstIndex, int lastIndex) = GetRealizationRange(items.Count);

            if (_focusedElement != null && (ItemsControl == null || KeyboardNavigation.GetTabOnceActiveElement(ItemsControl) != _focusedElement))
            {
                ReleaseFocusedElement();
            }

            foreach (int index in _realized.Keys.ToList())
            {
                if (index < firstIndex || index > lastIndex)
                {
                    RecycleElement(_realized[index], index);
                }
            }

            for (int index = firstIndex; index <= lastIndex; index++)
            {
                Control element = GetOrCreateElement(items, index);
                element.Measure(GetItemSize(index));
            }

            _focusedElement?.Measure(GetItemSize(_focusedIndex));
            _scrollToElement?.Measure(GetItemSize(_scrollToIndex));
        }
        finally
        {
            _isInLayout = false;
        }

        return new(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _isInLayout = true;
        try
        {
            Columns = GetColumnCount(finalSize.Width, Items.Count);

            foreach ((int index, Control element) in _realized)
            {
                element.Arrange(GetItemRect(index, finalSize.Width));
            }

            if (_focusedElement != null && _focusedIndex >= 0)
            {
                _focusedElement.Arrange(GetItemRect(_focusedIndex, finalSize.Width));
            }

            if (_scrollToElement != null && _scrollToIndex >= 0)
            {
                _scrollToElement.Arrange(GetItemRect(_scrollToIndex, finalSize.Width));
            }
        }
        finally
        {
            _isInLayout = false;
        }

        return finalSize;
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);
        _rowTopsDirty = true;

        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null && e.NewStartingIndex >= 0)
        {
            ShiftIndices(e.NewStartingIndex, e.NewItems.Count);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null && e.OldStartingIndex >= 0)
        {
            RemoveIndices(e.OldStartingIndex, e.OldItems.Count);
        }
        else
        {
            RecycleAll();
        }

        InvalidateMeasure();
    }

    protected override Control? ScrollIntoView(int index)
    {
        IReadOnlyList<object?> items = Items;
        if (_isInLayout || index < 0 || index >= items.Count || ItemContainerGenerator == null || !IsEffectivelyVisible)
        {
            return null;
        }

        if (_realized.TryGetValue(index, out Control? realized))
        {
            realized.BringIntoView();
            return realized;
        }

        if (this.GetPresentationSource() == null)
        {
            return null;
        }

        Control element = GetOrCreateElement(items, index);
        _realized.Remove(index);
        _realizedIndices.Remove(element);
        element.Measure(GetItemSize(index));
        element.Arrange(GetItemRect(index, Bounds.Width));
        if (_scrollToElement != null && _scrollToElement != element)
        {
            RecycleRemovedElement(_scrollToElement);
        }

        _scrollToElement = element;
        _scrollToIndex = index;

        element.BringIntoView();
        UpdateLayout();
        element.BringIntoView();

        if (_scrollToIndex == index)
        {
            _scrollToElement = null;
            _scrollToIndex = -1;
            _realized[index] = element;
            _realizedIndices[element] = index;
        }

        return element;
    }

    protected override Control? ContainerFromIndex(int index)
    {
        if (_realized.TryGetValue(index, out Control? element))
        {
            return element;
        }

        if (index == _focusedIndex)
        {
            return _focusedElement;
        }

        return index == _scrollToIndex ? _scrollToElement : null;
    }

    protected override int IndexFromContainer(Control container)
    {
        if (_realizedIndices.TryGetValue(container, out int index))
        {
            return index;
        }

        if (container == _focusedElement)
        {
            return _focusedIndex;
        }

        return container == _scrollToElement ? _scrollToIndex : -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers()
    {
        List<Control> containers = _realized.Values.ToList();
        if (_focusedElement != null)
        {
            containers.Add(_focusedElement);
        }

        return containers;
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        int count = Items.Count;
        if (count == 0)
        {
            return null;
        }

        int index = from is Control control ? IndexFromContainer(control) : -1;
        if (index < 0)
        {
            return ScrollIntoView(0);
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

        return ScrollIntoView(target);
    }

    private void KeepAnchorInView(int oldColumns, int newColumns)
    {
        if (_viewport.Height <= 0 || _viewport.Top <= 0 || ItemHeight <= 0)
        {
            return;
        }

        ScrollViewer? scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer == null)
        {
            return;
        }

        double[]? oldTops = GetRowTops();
        double[]? newTops = BuildRowTops(newColumns);
        int anchorRow = FindRow(oldTops, _viewport.Top);
        int anchorIndex = anchorRow * oldColumns;
        double rowOffset = _viewport.Top - GetRowTop(oldTops, anchorRow);
        double newTop = GetRowTop(newTops, anchorIndex / newColumns) + rowOffset;
        double delta = newTop - _viewport.Top;
        if (Math.Abs(delta) < 0.5)
        {
            return;
        }

        _viewport = _viewport.WithY(newTop);
        Dispatcher.UIThread.Post(() =>
        {
            scrollViewer.Offset = scrollViewer.Offset.WithY(Math.Max(0, scrollViewer.Offset.Y + delta));
        }, DispatcherPriority.Render);
    }

    private (int First, int Last) GetRealizationRange(int count)
    {
        if (count == 0 || ItemHeight <= 0)
        {
            _firstRealizedRow = -1;
            _lastRealizedRow = -1;
            return (0, -1);
        }

        (int firstRow, int lastRow) = GetVisibleRows();
        int totalRows = (count + Columns - 1) / Columns;
        firstRow = Math.Max(0, firstRow - BufferRows);
        lastRow = Math.Min(totalRows - 1, lastRow + BufferRows);

        _firstRealizedRow = firstRow;
        _lastRealizedRow = lastRow;
        return (firstRow * Columns, Math.Min(count - 1, (lastRow + 1) * Columns - 1));
    }

    private (int First, int Last) GetVisibleRows()
    {
        double top = _viewport.Height > 0 ? _viewport.Top : 0;
        double bottom = _viewport.Height > 0 ? _viewport.Bottom : FallbackViewportHeight;
        double[]? tops = GetRowTops();
        if (tops == null)
        {
            int firstUniformRow = Math.Max(0, (int) Math.Floor(top / ItemHeight));
            return (firstUniformRow, Math.Max(firstUniformRow, (int) Math.Ceiling(bottom / ItemHeight)));
        }

        int firstRow = FindRow(tops, top);
        return (firstRow, Math.Max(firstRow, FindRow(tops, bottom) + 1));
    }

    private Rect GetItemRect(int index, double width)
    {
        double cellWidth = Math.Max(ItemWidth, width / Columns);
        double offset = (cellWidth - ItemWidth) / 2;
        int row = index / Columns;
        int column = index % Columns;
        double[]? tops = GetRowTops();
        return new(column * cellWidth + offset, GetRowTop(tops, row), ItemWidth, GetRowHeight(tops, row));
    }

    private Size GetItemSize(int index)
    {
        return new(ItemWidth, index < 0 ? ItemHeight : GetRowHeight(GetRowTops(), index / Columns));
    }

    private double[]? GetRowTops()
    {
        if (!_rowTopsDirty && _rowTopsColumns == Columns)
        {
            return _rowTops;
        }

        _rowTops = BuildRowTops(Columns);
        _rowTopsColumns = Columns;
        _rowTopsDirty = false;
        return _rowTops;
    }

    private double[]? BuildRowTops(int columns)
    {
        IReadOnlyList<object?> items = Items;
        if (NameLines == BaseNameLines || items.Count == 0 || columns <= 0)
        {
            return null;
        }

        FontFamily fontFamily = GetValue(TextElement.FontFamilyProperty);
        int rows = (items.Count + columns - 1) / columns;
        double[] tops = new double[rows + 1];
        for (int row = 0; row < rows; row++)
        {
            double extra = 0;
            int end = Math.Min(items.Count, (row + 1) * columns);
            for (int index = row * columns; index < end; index++)
            {
                if (items[index] is INamedItem item)
                {
                    extra = Math.Max(extra, GridNameMeasurer.GetExtraHeight(item.Name, NameLines, BaseNameLines, fontFamily));
                }
            }

            tops[row + 1] = tops[row] + ItemHeight + Math.Ceiling(extra);
        }

        return tops;
    }

    private double GetRowTop(double[]? tops, int row)
    {
        if (tops == null)
        {
            return row * ItemHeight;
        }

        return tops[Math.Clamp(row, 0, tops.Length - 1)];
    }

    private double GetRowHeight(double[]? tops, int row)
    {
        if (tops == null || row < 0 || row >= tops.Length - 1)
        {
            return ItemHeight;
        }

        return tops[row + 1] - tops[row];
    }

    private int FindRow(double[]? tops, double y)
    {
        if (tops == null)
        {
            return Math.Max(0, (int) Math.Floor(y / ItemHeight));
        }

        int found = Array.BinarySearch(tops, y);
        int row = found >= 0 ? found : ~found - 1;
        return Math.Clamp(row, 0, Math.Max(0, tops.Length - 2));
    }

    private Control GetOrCreateElement(IReadOnlyList<object?> items, int index)
    {
        if (_realized.TryGetValue(index, out Control? realized))
        {
            return realized;
        }

        Control? special = TakeSpecialElement(index, ref _focusedIndex, ref _focusedElement)
                           ?? TakeSpecialElement(index, ref _scrollToIndex, ref _scrollToElement);
        Control element = special ?? CreateOrRecycleElement(items[index], index);

        _realized[index] = element;
        _realizedIndices[element] = index;
        return element;
    }

    private static Control? TakeSpecialElement(int index, ref int specialIndex, ref Control? specialElement)
    {
        if (specialIndex != index)
        {
            return null;
        }

        Control? element = specialElement;
        specialIndex = -1;
        specialElement = null;
        return element;
    }

    private Control CreateOrRecycleElement(object? item, int index)
    {
        ItemContainerGenerator generator = ItemContainerGenerator!;
        if (!generator.NeedsContainer(item, index, out object? recycleKey))
        {
            Control own = (Control) item!;
            if (!own.IsSet(RecycleKeyProperty))
            {
                generator.PrepareItemContainer(own, own, index);
                AddInternalChild(own);
                own.SetValue(RecycleKeyProperty, ItemIsOwnContainer);
                generator.ItemContainerPrepared(own, item, index);
            }

            own.SetCurrentValue(IsVisibleProperty, true);
            return own;
        }

        if (recycleKey != null && _recyclePool.TryGetValue(recycleKey, out Stack<Control>? pool) && pool.Count > 0)
        {
            Control recycled = pool.Pop();
            recycled.SetCurrentValue(IsVisibleProperty, true);
            generator.PrepareItemContainer(recycled, item, index);
            generator.ItemContainerPrepared(recycled, item, index);
            return recycled;
        }

        Control created = generator.CreateContainer(item, index, recycleKey);
        created.SetValue(RecycleKeyProperty, recycleKey);
        generator.PrepareItemContainer(created, item, index);
        AddInternalChild(created);
        generator.ItemContainerPrepared(created, item, index);
        return created;
    }

    private void RecycleElement(Control element, int index)
    {
        _realized.Remove(index);
        _realizedIndices.Remove(element);

        object? recycleKey = element.GetValue(RecycleKeyProperty);
        if (recycleKey == null)
        {
            RemoveInternalChild(element);
            return;
        }

        if (recycleKey == ItemIsOwnContainer)
        {
            element.SetCurrentValue(IsVisibleProperty, false);
            return;
        }

        if (ItemsControl != null && KeyboardNavigation.GetTabOnceActiveElement(ItemsControl) == element)
        {
            if (_focusedElement != element)
            {
                ReleaseFocusedElement();
            }

            _focusedElement = element;
            _focusedIndex = index;
            return;
        }

        PushToRecyclePool(recycleKey, element);
    }

    private void ReleaseFocusedElement()
    {
        if (_focusedElement == null)
        {
            return;
        }

        Control element = _focusedElement;
        _focusedElement = null;
        _focusedIndex = -1;
        RecycleRemovedElement(element);
    }

    private void RecycleRemovedElement(Control element)
    {
        object? recycleKey = element.GetValue(RecycleKeyProperty);
        if (recycleKey == null || recycleKey == ItemIsOwnContainer)
        {
            RemoveInternalChild(element);
            return;
        }

        PushToRecyclePool(recycleKey, element);
    }

    private void PushToRecyclePool(object recycleKey, Control element)
    {
        if (element.IsKeyboardFocusWithin)
        {
            ItemsControl?.Focus();
        }

        if (ItemsControl != null && KeyboardNavigation.GetTabOnceActiveElement(ItemsControl) == element)
        {
            KeyboardNavigation.SetTabOnceActiveElement(ItemsControl, null);
        }

        ItemContainerGenerator?.ClearItemContainer(element);
        if (!_recyclePool.TryGetValue(recycleKey, out Stack<Control>? pool))
        {
            pool = new();
            _recyclePool[recycleKey] = pool;
        }

        pool.Push(element);
        element.SetCurrentValue(IsVisibleProperty, false);
    }

    private void ShiftIndices(int startIndex, int count)
    {
        List<KeyValuePair<int, Control>> moved = _realized
            .Where(pair => pair.Key >= startIndex)
            .OrderByDescending(pair => pair.Key)
            .ToList();

        foreach ((int oldIndex, Control element) in moved)
        {
            int newIndex = oldIndex + count;
            _realized.Remove(oldIndex);
            _realized[newIndex] = element;
            _realizedIndices[element] = newIndex;
            ItemContainerGenerator?.ItemContainerIndexChanged(element, oldIndex, newIndex);
        }

        if (_focusedIndex >= startIndex)
        {
            _focusedIndex += count;
        }

        if (_scrollToIndex >= startIndex)
        {
            _scrollToIndex += count;
        }
    }

    private void RemoveIndices(int startIndex, int count)
    {
        int endIndex = startIndex + count;
        foreach (int index in _realized.Keys.Where(i => i >= startIndex && i < endIndex).ToList())
        {
            Control element = _realized[index];
            _realized.Remove(index);
            _realizedIndices.Remove(element);
            RecycleRemovedElement(element);
        }

        List<KeyValuePair<int, Control>> moved = _realized
            .Where(pair => pair.Key >= endIndex)
            .OrderBy(pair => pair.Key)
            .ToList();

        foreach ((int oldIndex, Control element) in moved)
        {
            int newIndex = oldIndex - count;
            _realized.Remove(oldIndex);
            _realized[newIndex] = element;
            _realizedIndices[element] = newIndex;
            ItemContainerGenerator?.ItemContainerIndexChanged(element, oldIndex, newIndex);
        }

        _focusedIndex = AdjustSpecialIndexForRemoval(_focusedIndex, startIndex, endIndex, count, ref _focusedElement);
        _scrollToIndex = AdjustSpecialIndexForRemoval(_scrollToIndex, startIndex, endIndex, count, ref _scrollToElement);
    }

    private int AdjustSpecialIndexForRemoval(int index, int startIndex, int endIndex, int count, ref Control? element)
    {
        if (index < startIndex)
        {
            return index;
        }

        if (index >= endIndex)
        {
            return index - count;
        }

        if (element != null)
        {
            RecycleRemovedElement(element);
            element = null;
        }

        return -1;
    }

    private void RecycleAll()
    {
        foreach (Control element in _realized.Values.ToList())
        {
            RecycleRemovedElement(element);
        }

        _realized.Clear();
        _realizedIndices.Clear();

        ReleaseFocusedElement();

        if (_scrollToElement != null)
        {
            RecycleRemovedElement(_scrollToElement);
            _scrollToElement = null;
            _scrollToIndex = -1;
        }
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _viewport = e.EffectiveViewport.Intersect(new Rect(Bounds.Size));
        if (ItemHeight <= 0)
        {
            return;
        }

        (int firstRow, int lastRow) = GetVisibleRows();
        int totalRows = (Items.Count + Columns - 1) / Columns;
        int firstNeeded = Math.Max(0, firstRow);
        int lastNeeded = Math.Min(Math.Max(0, totalRows - 1), lastRow);
        if (firstNeeded < _firstRealizedRow || lastNeeded > _lastRealizedRow || _firstRealizedRow < 0)
        {
            InvalidateMeasure();
        }
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

        int rows = (Items.Count + Columns - 1) / Columns;
        double rowHeight = rows > 0 ? GetRowTop(GetRowTops(), rows) / rows : ItemHeight;
        return Math.Max(1, (int) (viewportHeight / Math.Max(1, rowHeight)));
    }

    private int GetColumnCount(double width, int count)
    {
        if (double.IsInfinity(width) || double.IsNaN(width) || ItemWidth <= 0)
        {
            return Math.Max(1, count);
        }

        return Math.Max(1, (int) (width / ItemWidth));
    }
}
