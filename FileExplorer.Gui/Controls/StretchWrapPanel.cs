using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static Avalonia.Input.NavigationDirection;

namespace FileExplorer.Gui.Controls;

public class StretchWrapPanel : VirtualizingPanel
{
    private const int BufferRows = 2;
    private const double FallbackViewportHeight = 1200;

    public static readonly StyledProperty<double> ItemWidthProperty =
        AvaloniaProperty.Register<StretchWrapPanel, double>(nameof(ItemWidth), 128);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<StretchWrapPanel, double>(nameof(ItemHeight), 136);

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

    private Control? _focusedElement;
    private int _focusedIndex = -1;
    private Control? _scrollToElement;
    private int _scrollToIndex = -1;

    static StretchWrapPanel()
    {
        AffectsMeasure<StretchWrapPanel>(ItemWidthProperty, ItemHeightProperty);
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

    public int Columns { get; private set; } = 1;

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

        if (ItemContainerGenerator == null)
        {
            return new(width, rows * ItemHeight);
        }

        _isInLayout = true;
        try
        {
            Size itemSize = new(ItemWidth, ItemHeight);
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
                element.Measure(itemSize);
            }

            _focusedElement?.Measure(itemSize);
            _scrollToElement?.Measure(itemSize);
        }
        finally
        {
            _isInLayout = false;
        }

        return new(width, rows * ItemHeight);
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
        element.Measure(new(ItemWidth, ItemHeight));
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

        int anchorIndex = (int) Math.Floor(_viewport.Top / ItemHeight) * oldColumns;
        double rowOffset = _viewport.Top % ItemHeight;
        double newTop = anchorIndex / newColumns * ItemHeight + rowOffset;
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
        int firstRow = Math.Max(0, (int) Math.Floor(top / ItemHeight));
        int lastRow = Math.Max(firstRow, (int) Math.Ceiling(bottom / ItemHeight));
        return (firstRow, lastRow);
    }

    private Rect GetItemRect(int index, double width)
    {
        double cellWidth = Math.Max(ItemWidth, width / Columns);
        double offset = (cellWidth - ItemWidth) / 2;
        int row = index / Columns;
        int column = index % Columns;
        return new(column * cellWidth + offset, row * ItemHeight, ItemWidth, ItemHeight);
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

        return Math.Max(1, (int) (viewportHeight / ItemHeight));
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
