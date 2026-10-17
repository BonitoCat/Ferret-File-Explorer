using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Models;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Dialogs;
using FileExplorer.Gui.Models;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;
using LoggerLib;
using static Avalonia.Input.DragDrop;
using static Avalonia.Input.InputElement;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel) DataContext!;

    private PlaceItem? _draggedPlace;
    private PlaceCategory? _draggedSection;
    private Point _sidebarDragStart;
    private bool _isSidebarDragging;

    private bool _isRubberBanding;
    private Point _rubberBandStart;

    private bool _potentialFileDrag;
    private Point _fileDragStart;
    private FileItemViewModel? _draggedFile;
    private FileItemViewModel? _clickedAlreadySelectedItem;

    private Button? _hoveredBreadcrumbButton;
    private string? _hoveredBreadcrumbPath;

    private bool _isResizingCol;
    private string? _resizingCol;
    private double _resizeStartX;
    private double _initColNameWidth;
    private double _initColSizeWidth;
    private double _initColTypeWidth;
    private Grid? _resizeHeaderGrid;

    private string _typeAheadBuffer = "";
    private DateTime _typeAheadLastKeyTime = DateTime.MinValue;

    private const string GridFilesListName = "GridFilesListBox";
    private const string ListFilesListName = "ListFilesListBox";
    private readonly List<ListBox> _fileListBoxes = new();

    public FilePickerResult? PickerResult { get; private set; }

    public MainWindow() : this(null, null)
    {
    }

    public MainWindow(string? initialPath) : this(initialPath, null)
    {
    }

    public MainWindow(string? initialPath, FilePickerOptions? pickerOptions)
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(initialPath, pickerOptions);

        if (pickerOptions != null)
        {
            Title = pickerOptions.Title ?? (pickerOptions.Mode == PickerMode.Folder ? LocalizationService.Get("SelectFolder") : LocalizationService.Get("SelectFile"));
            ViewModel.PickerCompleted += OnPickerCompleted;
        }

        Closing += (_, _) =>
        {
            PickerResult ??= FilePickerResult.Cancelled();
        };

        ViewModel.ScriptOpenRequested += OnScriptOpenRequested;
        ViewModel.ConflictResolver = ResolveConflictAsync;

        foreach (PaneViewModel pane in ViewModel.Panes)
        {
            pane.BeforeLoad += OnPaneBeforeLoad;
        }

        Opened += (s, e) =>
        {
            IPlatformHandle? handle = TryGetPlatformHandle();
            if (handle != null)
            {
                X11WindowHelper.ConfigureWindow(handle.Handle);
                X11WindowHelper.CompleteStartupNotification(handle.Handle);
                X11XdndHook.Hook(
                    this,
                    (paths, rootX, rootY, isCopy) =>
                    {
                        Point? clientPoint = (rootX > 0 || rootY > 0)
                            ? ((IRenderRoot) this).PointToClient(new PixelPoint(rootX, rootY))
                            : null;

                        HandleFilesDropped(paths, clientPoint, isCopy);
                        ClearDropTargets();
                    },
                    (rootX, rootY) =>
                    {
                        Point clientPoint = ((IRenderRoot) this).PointToClient(new PixelPoint(rootX, rootY));
                        HighlightDropTarget(clientPoint);
                    },
                    () =>
                    {
                        ClearDropTargets();
                    });
            }
        };
        
        Closed += (s, e) =>
        {
            ViewModel.Dispose();
            GuiInstanceRegistry.Unregister(Environment.ProcessId);
        };

        try
        {
            using Stream iconStream = AssetLoader.Open(new Uri("avares://FileExplorer.Gui/Assets/folder.png"));
            Icon = new WindowIcon(iconStream);
        }
        catch
        {
        }

        if (Environment.IsPrivilegedProcess)
        {
            Title = LocalizationService.Get("WindowTitlePrivileged");
        }

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPath) || e.PropertyName == nameof(MainWindowViewModel.ActivePane))
            {
                ScrollBreadcrumbsToCurrent();
                SyncSidebarSelection(ViewModel.CurrentPath);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsDualPane))
            {
                FilePanesGrid.ColumnDefinitions[0].Width = new(1, GridUnitType.Star);
                FilePanesGrid.ColumnDefinitions[2].Width = ViewModel.IsDualPane ? new(1, GridUnitType.Star) : new(0, GridUnitType.Pixel);
            }
        };

        ScrollBreadcrumbsToCurrent();
        SyncSidebarSelection(ViewModel.CurrentPath);

        SidebarListBox.AddHandler(PointerPressedEvent, OnSidebarPointerPressed, Tunnel);
        SidebarListBox.AddHandler(PointerMovedEvent, OnSidebarPointerMoved, Tunnel);
        SidebarListBox.AddHandler(PointerReleasedEvent, OnSidebarPointerReleased, Tunnel);

        AddHandler(DropEvent, OnAppDragDrop, Tunnel | Bubble, true);
        AddHandler(DragOverEvent, OnAppDragOver, Tunnel | Bubble, true);
        AddHandler(DragLeaveEvent, OnAppDragLeave, Tunnel | Bubble, true);

        Focusable = true;
        Activated += OnWindowActivated;
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, Tunnel);
        AddHandler(KeyDownEvent, OnGlobalKeyDown, Tunnel);
        AddHandler(KeyDownEvent, OnAltChordKeyDown, Tunnel | Bubble, true);
        AddHandler(KeyUpEvent, OnAltKeyUp, Tunnel, true);
        AddHandler(TextInputEvent, OnWindowTextInput, Tunnel);
    }

    private void ScrollBreadcrumbsToCurrent()
    {
        Dispatcher.UIThread.Post(EnsureCurrentBreadcrumbVisible, DispatcherPriority.Background);
    }

    private void EnsureCurrentBreadcrumbVisible()
    {
        int index = ViewModel.Breadcrumbs
            .Select((crumb, i) => crumb.IsCurrent ? i : -1)
            .FirstOrDefault(i => i >= 0, -1);
        (double Left, double Right)? bounds = GetBreadcrumbBounds(index);
        if (bounds == null)
        {
            return;
        }

        double offset = BreadcrumbsScrollViewer.Offset.X;
        double viewport = BreadcrumbsScrollViewer.Viewport.Width;
        if (bounds.Value.Right > offset + viewport)
        {
            offset = bounds.Value.Right - viewport;
        }

        if (bounds.Value.Left < offset)
        {
            offset = bounds.Value.Left;
        }

        BreadcrumbsScrollViewer.Offset = new(Math.Max(0, offset), 0);
        UpdateBreadcrumbScrollButtons();
    }

    private (double Left, double Right)? GetBreadcrumbBounds(int index)
    {
        if (index < 0)
        {
            return null;
        }

        Control? container = BreadcrumbsItemsControl.ContainerFromIndex(index);
        Point? origin = container?.TranslatePoint(new(0, 0), BreadcrumbsItemsControl);
        if (container == null || origin == null)
        {
            return null;
        }

        return (origin.Value.X, origin.Value.X + container.Bounds.Width);
    }

    private void UpdateBreadcrumbScrollButtons()
    {
        double extent = BreadcrumbsScrollViewer.Extent.Width;
        double viewport = BreadcrumbsScrollViewer.Viewport.Width;
        double offset = BreadcrumbsScrollViewer.Offset.X;
        bool overflowing = extent > viewport + 0.5;

        BreadcrumbScrollLeftButton.IsVisible = overflowing;
        BreadcrumbScrollRightButton.IsVisible = overflowing;
        BreadcrumbScrollLeftButton.IsEnabled = offset > 0.5;
        BreadcrumbScrollRightButton.IsEnabled = offset < extent - viewport - 0.5;
    }

    private void OnBreadcrumbsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.X != 0 || e.ViewportDelta.X != 0)
        {
            ScrollBreadcrumbsToCurrent();
            return;
        }

        UpdateBreadcrumbScrollButtons();
    }

    private void OnBreadcrumbScrollLeftClick(object? sender, RoutedEventArgs e)
    {
        double offset = BreadcrumbsScrollViewer.Offset.X;
        for (int i = ViewModel.Breadcrumbs.Count - 1; i >= 0; i--)
        {
            (double Left, double Right)? bounds = GetBreadcrumbBounds(i);
            if (bounds != null && bounds.Value.Left < offset - 0.5)
            {
                BreadcrumbsScrollViewer.Offset = new(Math.Max(0, bounds.Value.Left), 0);
                break;
            }
        }

        e.Handled = true;
    }

    private void OnBreadcrumbScrollRightClick(object? sender, RoutedEventArgs e)
    {
        double viewEnd = BreadcrumbsScrollViewer.Offset.X + BreadcrumbsScrollViewer.Viewport.Width;
        for (int i = 0; i < ViewModel.Breadcrumbs.Count; i++)
        {
            (double Left, double Right)? bounds = GetBreadcrumbBounds(i);
            if (bounds != null && bounds.Value.Right > viewEnd + 0.5)
            {
                BreadcrumbsScrollViewer.Offset = new(bounds.Value.Right - BreadcrumbsScrollViewer.Viewport.Width, 0);
                break;
            }
        }

        e.Handled = true;
    }

    private void OnFileListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ListBox list || _fileListBoxes.Contains(list))
        {
            return;
        }

        _fileListBoxes.Add(list);
        list.AddHandler(PointerPressedEvent, OnFilePointerPressed, Tunnel);
        list.AddHandler(PointerMovedEvent, OnFilePointerMoved, Tunnel);
        list.AddHandler(PointerReleasedEvent, OnFilePointerReleased, Tunnel);
    }

    private static PaneViewModel? GetPane(object? sender)
    {
        return (sender as StyledElement)?.DataContext as PaneViewModel;
    }

    private ListBox? GetListBox(PaneViewModel pane)
    {
        string name = ViewModel.IsGridView ? GridFilesListName : ListFilesListName;
        return _fileListBoxes.FirstOrDefault(l => l.DataContext == pane && l.Name == name);
    }

    private ListBox? GetActiveListBox()
    {
        return GetListBox(ViewModel.ActivePane);
    }

    private static (Canvas Canvas, Border Rect) GetRubberBand(ListBox list)
    {
        Canvas canvas = ((Panel) list.Parent!).Children.OfType<Canvas>().First();
        return (canvas, canvas.Children.OfType<Border>().First());
    }

    private void OnPaneBeforeLoad(PaneViewModel pane)
    {
        foreach (ListBox list in _fileListBoxes.Where(l => l.DataContext == pane))
        {
            list.Selection.Clear();
        }
    }

    private void SyncPaneSelection(PaneViewModel pane)
    {
        ListBox? list = GetListBox(pane);
        if (list == null)
        {
            return;
        }

        pane.SetSelection(GetListSelection(list), list.SelectedItem as FileItemViewModel);
    }

    private static List<FileItemViewModel> GetListSelection(ListBox list)
    {
        return list.Selection.SelectedItems
            .OfType<FileItemViewModel>()
            .Distinct()
            .ToList();
    }

    private static bool IsInListSelection(ListBox list, FileItemViewModel item)
    {
        return list.Selection.SelectedItems.Contains(item);
    }

    private IEnumerable<FileItemViewModel> GetSelectedItems()
    {
        ListBox? activeList = GetActiveListBox();
        List<FileItemViewModel> selection = activeList != null ? GetListSelection(activeList) : new();
        return selection.Count > 0 ? selection : ViewModel.ActivePane.GetSelection();
    }

    private void FocusActiveListBox()
    {
        GetActiveListBox()?.Focus();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoBack();
        FocusActiveListBox();
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoForward();
        FocusActiveListBox();
    }

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoUp();
        FocusActiveListBox();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
        FocusActiveListBox();
    }

    private void OnGoHomeClick(object? sender, RoutedEventArgs e)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home))
        {
            ViewModel.NavigateTo(home);
            FocusActiveListBox();
        }
    }

    private void OnGoRootClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.NavigateTo("/");
        FocusActiveListBox();
    }

    private void OnToggleHiddenClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ShowHiddenFiles = !ViewModel.ShowHiddenFiles;
    }

    private void OnToggleDualPaneClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDualPane();
        FocusActiveListBox();
    }

    private void OnTogglePreviewPaneClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ShowDetailsPane = !ViewModel.ShowDetailsPane;
    }

    private void OnSetGridViewClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SetGridView(true);
    }

    private void OnSetListViewClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SetGridView(false);
    }

    private void OnTogglePathEditingClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.TogglePathEditing();
        if (ViewModel.IsPathEditing)
        {
            PathTextBox.Focus();
            PathTextBox.SelectAll();
        }
    }

    private void OnBreadcrumbBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Button? clickedButton = FindVisualParent<Button>((Visual?) e.Source);
        if (clickedButton != null)
        {
            return;
        }

        ViewModel.IsPathEditing = true;
        PathTextBox.Focus();
        PathTextBox.SelectAll();
    }

    private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        Button? button = sender as Button;
        string? targetPath = button?.Tag as string;
        if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath))
        {
            return;
        }

        ViewModel.NavigateTo(targetPath);
    }

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            PathTextBox.Text = ViewModel.CurrentPath;
            ViewModel.IsPathEditing = false;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        string path = PathTextBox.Text ?? "";
        if (Directory.Exists(path))
        {
            ViewModel.NavigateTo(path);
            ViewModel.IsPathEditing = false;
        }
    }

    private void OnPathLostFocus(object? sender, RoutedEventArgs e)
    {
        string path = PathTextBox.Text ?? "";
        if (Directory.Exists(path) && path != ViewModel.CurrentPath)
        {
            ViewModel.NavigateTo(path);
        }

        ViewModel.IsPathEditing = false;
    }

    private void OnSearchLostFocus(object? sender, RoutedEventArgs e)
    {
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchTextBox.Text = "";
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            FocusActiveListBox();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            FocusActiveListBox();
            e.Handled = true;
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (props.PointerUpdateKind == PointerUpdateKind.XButton1Pressed || props.IsXButton1Pressed)
        {
            ViewModel.GoBack();
            e.Handled = true;
            return;
        }

        if (props.PointerUpdateKind == PointerUpdateKind.XButton2Pressed || props.IsXButton2Pressed)
        {
            ViewModel.GoForward();
            e.Handled = true;
            return;
        }

        Visual? hit = (Visual?) e.Source;
        if (hit == null)
        {
            return;
        }
        
        if (SearchTextBox != null && SearchTextBox.IsFocused)
        {
            bool isInsideSearch = FindVisualParent<TextBox>(hit) == SearchTextBox;
            if (!isInsideSearch)
            {
                GetTopLevel(this)?.FocusManager?.ClearFocus();
            }
        }

        if (ViewModel.IsPathEditing && PathTextBox != null)
        {
            bool isInsidePath = FindVisualParent<TextBox>(hit) == PathTextBox;
            if (!isInsidePath)
            {
                ViewModel.IsPathEditing = false;
            }
        }
    }

    private void OnFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        PaneViewModel? pane = GetPane(sender);
        if (pane == null)
        {
            return;
        }

        SyncPaneSelection(pane);
    }

    private void OnPaneGotFocus(object? sender, GotFocusEventArgs e)
    {
        PaneViewModel? pane = GetPane(sender);
        if (pane == null)
        {
            return;
        }

        ViewModel.ActivePane = pane;
    }

    private bool _isSyncingSidebar;
    private PlaceItem? _contextPlace;

    private void SyncSidebarSelection(string? path)
    {
        if (_isSyncingSidebar)
        {
            return;
        }

        _isSyncingSidebar = true;
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                SidebarListBox.SelectedItem = null;
                return;
            }

            string normPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(normPath))
            {
                normPath = "/";
            }

            if (SidebarListBox.SelectedItem is PlaceItem current && IsPlaceAtPath(current, normPath))
            {
                return;
            }

            SidebarListBox.SelectedItem = ViewModel.Places.FirstOrDefault(p => IsPlaceAtPath(p, normPath));
        }
        catch
        {
            SidebarListBox.SelectedItem = null;
        }
        finally
        {
            _isSyncingSidebar = false;
        }
    }

    private static bool IsPlaceAtPath(PlaceItem item, string normPath)
    {
        if (item.IsHeader || string.IsNullOrEmpty(item.Path))
        {
            return false;
        }

        string itemNorm = Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(itemNorm))
        {
            itemNorm = "/";
        }

        return string.Equals(normPath, itemNorm, StringComparison.OrdinalIgnoreCase);
    }

    private async Task NavigateToPlaceItemAsync(PlaceItem selected)
    {
        if (selected == null || selected.IsHeader || string.IsNullOrEmpty(selected.Path))
        {
            return;
        }

        if (selected.Category == PlaceCategory.Network && !selected.IsMounted)
        {
            await ConnectToNetworkBookmarkAsync(selected);
            return;
        }

        if (selected.Category == PlaceCategory.Devices && !selected.IsMounted)
        {
            await MountDeviceAsync(selected);
            return;
        }

        string placePath = selected.Path;
        if (await Task.Run(() => Directory.Exists(placePath)))
        {
            ViewModel.NavigateTo(selected.Path);

            FocusActiveListBox();
        }
    }

    private async Task MountDeviceAsync(PlaceItem selected)
    {
        string devPath = selected.DevicePath ?? selected.Path;
        if (string.IsNullOrEmpty(devPath))
        {
            return;
        }

        ViewModel.StatusText = LocalizationService.Format("MountingNamed", selected.Name);

        string? mountPath = await ViewModel.MountDeviceAsync(devPath);
        if (!string.IsNullOrEmpty(mountPath) && Directory.Exists(mountPath))
        {
            selected.Path = mountPath;
            selected.IsMounted = true;
            ViewModel.NavigateTo(mountPath);

            FocusActiveListBox();
            ViewModel.LoadPlaces();
        }
    }

    private async void OnPlaceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSidebar)
        {
            return;
        }

        ListBox? listBox = sender as ListBox;
        PlaceItem? selected = listBox?.SelectedItem as PlaceItem;
        if (selected is null || selected.IsHeader)
        {
            return;
        }

        await NavigateToPlaceItemAsync(selected);
    }

    private async void OnSidebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(SidebarListBox).Properties.IsRightButtonPressed)
        {
            ListBoxItem? rightClickedItem = FindVisualParent<ListBoxItem>((Visual?) e.Source);
            if (rightClickedItem?.DataContext is PlaceItem rightPlace)
            {
                _contextPlace = rightPlace;
                _isSyncingSidebar = true;
                SidebarListBox.SelectedItem = rightPlace;
                _isSyncingSidebar = false;
            }

            return;
        }

        if (!e.GetCurrentPoint(SidebarListBox).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Button? button = FindVisualParent<Button>((Visual?) e.Source);
        if (button != null)
        {
            e.Handled = true;
            if (button.DataContext is PlaceItem placeItem)
            {
                if (placeItem.IsNetworkHeader)
                {
                    await OpenConnectToServerDialogAsync();
                    return;
                }

                if (placeItem.CanDismount)
                {
                    await ViewModel.DismountPlaceAsync(placeItem);
                    return;
                }
            }

            return;
        }

        ListBoxItem? item = FindVisualParent<ListBoxItem>((Visual?) e.Source);
        if (item?.DataContext is not PlaceItem place)
        {
            return;
        }

        if (place.IsHeader)
        {
            _draggedSection = place.Category;
            _sidebarDragStart = e.GetPosition(SidebarListBox);
            _isSidebarDragging = false;
            SidebarListBox.SelectedItem = null;
            e.Handled = true;
            return;
        }

        if (SidebarListBox.SelectedItem == place)
        {
            _ = NavigateToPlaceItemAsync(place);
        }

        _draggedPlace = place;
        _sidebarDragStart = e.GetPosition(SidebarListBox);
        _isSidebarDragging = false;
    }

    private void OnSidebarPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedPlace == null && _draggedSection == null)
        {
            return;
        }

        Point current = e.GetPosition(SidebarListBox);
        if (!_isSidebarDragging && (Math.Abs(current.X - _sidebarDragStart.X) > 4 || Math.Abs(current.Y - _sidebarDragStart.Y) > 4))
        {
            _isSidebarDragging = true;
            e.Pointer.Capture(SidebarListBox);
        }

        if (!_isSidebarDragging)
        {
            return;
        }

        Visual? hit = this.GetVisualAt(e.GetPosition(this));
        ListBoxItem? targetItem = FindVisualParent<ListBoxItem>(hit);
        PlaceItem? targetPlace = targetItem?.DataContext as PlaceItem;

        ClearSidebarDropLines();

        if (_draggedSection != null)
        {
            HighlightSectionDropTarget(e, targetItem, targetPlace, _draggedSection.Value);
            return;
        }

        if (_draggedPlace == null || targetItem == null || targetPlace == null || targetPlace == _draggedPlace || targetPlace.IsHeader || targetPlace.Category != _draggedPlace.Category)
        {
            return;
        }

        Point posInItem = e.GetPosition(targetItem);
        bool insertAbove = posInItem.Y < targetItem.Bounds.Height / 2;

        targetPlace.ShowDropLineAbove = insertAbove;
        targetPlace.ShowDropLineBelow = !insertAbove;
    }

    private void OnSidebarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PlaceItem? target = ViewModel.Places.FirstOrDefault(p => p.ShowDropLineAbove || p.ShowDropLineBelow);
        if (_draggedSection != null)
        {
            if (!_isSidebarDragging)
            {
                ViewModel.ToggleCategoryCollapse(_draggedSection.Value);
            }
            else if (target != null)
            {
                ViewModel.MoveSection(_draggedSection.Value, target.Category, target.ShowDropLineAbove);
                SyncSidebarSelection(ViewModel.CurrentPath);
            }
        }
        else if (_isSidebarDragging && _draggedPlace != null && target != null)
        {
            ViewModel.ReorderPlace(_draggedPlace, target, target.ShowDropLineAbove);
        }

        ClearSidebarDropLines();

        _draggedPlace = null;
        _draggedSection = null;
        _isSidebarDragging = false;
        e.Pointer.Capture(null);
    }

    private void ClearSidebarDropLines()
    {
        foreach (PlaceItem p in ViewModel.Places)
        {
            p.ShowDropLineAbove = false;
            p.ShowDropLineBelow = false;
        }
    }

    private void HighlightSectionDropTarget(PointerEventArgs e, ListBoxItem? targetItem, PlaceItem? targetPlace, PlaceCategory section)
    {
        if (targetItem == null || targetPlace == null || targetPlace.Category == section)
        {
            return;
        }

        List<PlaceItem> rows = ViewModel.Places
            .Where(p => p.Category == targetPlace.Category && (p.IsHeader || p.IsVisible))
            .ToList();
        int rowIndex = rows.IndexOf(targetPlace);
        if (rows.Count == 0 || rowIndex < 0)
        {
            return;
        }

        double fraction = e.GetPosition(targetItem).Y / Math.Max(1, targetItem.Bounds.Height);
        bool insertAbove = (rowIndex + fraction) / rows.Count < 0.5;

        if (insertAbove)
        {
            rows[0].ShowDropLineAbove = true;
        }
        else
        {
            rows[^1].ShowDropLineBelow = true;
        }
    }

    private void OnSidebarContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _contextPlace = null;
            SyncSidebarSelection(ViewModel.CurrentPath);
        }, DispatcherPriority.Background);
    }

    private void OnSidebarContextMenuOpening(object? sender, CancelEventArgs e)
    {
        PlaceItem? selected = _contextPlace ?? SidebarListBox.SelectedItem as PlaceItem;
        if (selected == null)
        {
            e.Cancel = true;
            return;
        }

        if (selected.IsHeader)
        {
            if (selected.IsNetworkHeader)
            {
                SidebarOpenMenuItem.IsVisible = false;
                SidebarConnectMenuItem.IsVisible = false;
                SidebarDismountMenuItem.IsVisible = false;
                SidebarConnectToServerMenuItem.IsVisible = true;
                SidebarResetPlacesMenuItem.IsVisible = false;
                SidebarSeparator.IsVisible = false;
                SidebarRemoveBookmarkMenuItem.IsVisible = false;
                SidebarRemoveServerMenuItem.IsVisible = false;
                return;
            }

            if (selected.Category == PlaceCategory.Places)
            {
                SidebarOpenMenuItem.IsVisible = false;
                SidebarConnectMenuItem.IsVisible = false;
                SidebarDismountMenuItem.IsVisible = false;
                SidebarConnectToServerMenuItem.IsVisible = false;
                SidebarResetPlacesMenuItem.IsVisible = true;
                SidebarSeparator.IsVisible = false;
                SidebarRemoveBookmarkMenuItem.IsVisible = false;
                SidebarRemoveServerMenuItem.IsVisible = false;
                return;
            }

            e.Cancel = true;
            return;
        }

        SidebarConnectToServerMenuItem.IsVisible = false;

        switch (selected.Category)
        {
            case PlaceCategory.Places:
                SidebarOpenMenuItem.IsVisible = true;
                SidebarConnectMenuItem.IsVisible = false;
                SidebarDismountMenuItem.IsVisible = false;
                SidebarResetPlacesMenuItem.IsVisible = false;
                SidebarSeparator.IsVisible = false;
                SidebarRemoveBookmarkMenuItem.IsVisible = false;
                SidebarRemoveServerMenuItem.IsVisible = false;
                break;

            case PlaceCategory.Devices:
                SidebarOpenMenuItem.IsVisible = true;
                SidebarConnectMenuItem.IsVisible = false;
                SidebarDismountMenuItem.IsVisible = selected.CanDismount;
                SidebarResetPlacesMenuItem.IsVisible = false;
                SidebarSeparator.IsVisible = false;
                SidebarRemoveBookmarkMenuItem.IsVisible = false;
                SidebarRemoveServerMenuItem.IsVisible = false;
                break;

            case PlaceCategory.Network:
                SidebarResetPlacesMenuItem.IsVisible = false;
                if (selected.IsMounted)
                {
                    SidebarOpenMenuItem.IsVisible = true;
                    SidebarConnectMenuItem.IsVisible = false;
                    SidebarDismountMenuItem.IsVisible = selected.CanDismount;
                    SidebarSeparator.IsVisible = selected.IsNetworkBookmark;
                    SidebarRemoveBookmarkMenuItem.IsVisible = false;
                    SidebarRemoveServerMenuItem.IsVisible = selected.IsNetworkBookmark;
                }
                else
                {
                    SidebarOpenMenuItem.IsVisible = false;
                    SidebarConnectMenuItem.IsVisible = true;
                    SidebarDismountMenuItem.IsVisible = false;
                    SidebarSeparator.IsVisible = true;
                    SidebarRemoveBookmarkMenuItem.IsVisible = false;
                    SidebarRemoveServerMenuItem.IsVisible = true;
                }
                break;

            case PlaceCategory.Bookmarks:
                SidebarOpenMenuItem.IsVisible = true;
                SidebarConnectMenuItem.IsVisible = false;
                SidebarDismountMenuItem.IsVisible = false;
                SidebarResetPlacesMenuItem.IsVisible = false;
                SidebarSeparator.IsVisible = true;
                SidebarRemoveBookmarkMenuItem.IsVisible = true;
                SidebarRemoveServerMenuItem.IsVisible = false;
                break;
        }
    }

    private void OnSidebarResetPlacesClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ResetPlacesOrder();
        SyncSidebarSelection(ViewModel.CurrentPath);
    }

    private async void OnSidebarOpenClick(object? sender, RoutedEventArgs e)
    {
        PlaceItem? selected = GetContextPlaceItem(sender);
        if (selected == null || selected.IsHeader || string.IsNullOrEmpty(selected.Path))
        {
            return;
        }

        if (selected.Category == PlaceCategory.Network && !selected.IsMounted)
        {
            await ConnectToNetworkBookmarkAsync(selected);
            return;
        }

        string selectedPath = selected.Path;
        if (await Task.Run(() => Directory.Exists(selectedPath)))
        {
            ViewModel.NavigateTo(selected.Path);
        }
    }

    private async void OnSidebarConnectClick(object? sender, RoutedEventArgs e)
    {
        PlaceItem? selected = GetContextPlaceItem(sender);
        if (selected == null || selected.Category != PlaceCategory.Network)
        {
            return;
        }

        await ConnectToNetworkBookmarkAsync(selected);
    }

    private void OnSidebarRemoveServerClick(object? sender, RoutedEventArgs e)
    {
        PlaceItem? selected = GetContextPlaceItem(sender);
        if (selected == null || selected.Category != PlaceCategory.Network)
        {
            return;
        }

        string? uri = selected.RemoteUri ?? selected.Path;
        if (!string.IsNullOrEmpty(uri))
        {
            ViewModel.RemoveNetworkBookmark(uri);
        }
    }

    private async Task ConnectToNetworkBookmarkAsync(PlaceItem item)
    {
        string? uri = item.RemoteUri ?? item.Path;
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        MainWindowViewModel vm = ViewModel;
        string? password = await Task.Run(() => vm.GetNetworkPassword(uri));
        if (string.IsNullOrEmpty(password) && !NetworkService.ParseUri(uri).Anonymous)
        {
            InputDialog pwdDialog = new(LocalizationService.Get("AuthenticationRequired"), LocalizationService.Format("EnterPasswordFor", item.Name), "", true);
            password = await pwdDialog.ShowDialog<string?>(this);
            if (password == null)
            {
                return;
            }

            string newPassword = password;
            await Task.Run(() => vm.SetNetworkPassword(uri, newPassword));
        }

        PaneViewModel pane = ViewModel.ActivePane;
        pane.IsLoading = true;
        ViewModel.StatusText = LocalizationService.Get("Connecting");

        try
        {
            (bool success, string mountPath, string error) = await Task.Run(() => vm.MountNetworkBookmarkAsync(uri, password));
            bool mountExists = success && !string.IsNullOrEmpty(mountPath) && await Task.Run(() => Directory.Exists(mountPath));

            if (mountExists)
            {
                ViewModel.LoadPlaces();
                ViewModel.NavigateTo(mountPath);
            }
            else
            {
                ViewModel.StatusText = LocalizationService.Format("ConnectionFailedWithError", error);
                string msg = string.IsNullOrWhiteSpace(error) ? LocalizationService.Get("ConnectionFailedOrTimedOut") : error;
                ConfirmDialog errDialog = new(LocalizationService.Get("ConnectionFailed"), msg);
                await errDialog.ShowDialog(this);
            }
        }
        finally
        {
            pane.IsLoading = false;
        }
    }

    private async void OnMenuExtractHereClick(object? sender, RoutedEventArgs e)
    {
        PaneViewModel pane = ViewModel.ActivePane;
        List<FileItemViewModel> targets = pane.GetSelection()
            .Where(i => i.FileType == FileType.Archive || MainWindowViewModel.IsArchiveFile(i.FullPath))
            .ToList();
        string currentDir = pane.CurrentPath;
        if (targets.Count == 0 || !Directory.Exists(currentDir))
        {
            return;
        }

        ArchiveService archiveService = new();
        pane.IsLoading = true;

        try
        {
            foreach (FileItemViewModel item in targets)
            {
                string? password = null;
                bool isEncrypted = await archiveService.IsEncryptedArchiveAsync(item.FullPath);
                if (isEncrypted)
                {
                    InputDialog pwdDialog = new(LocalizationService.Get("EncryptedArchive"), LocalizationService.Format("EnterPasswordFor", item.Name), "", true);
                    password = await pwdDialog.ShowDialog<string?>(this);
                    if (password == null)
                    {
                        continue;
                    }
                }

                (bool success, string error) = await archiveService.ExtractHereAsync(item.FullPath, currentDir, password);
                if (!success && !string.IsNullOrEmpty(error))
                {
                    ConfirmDialog errDialog = new(LocalizationService.Get("Error"), error);
                    await errDialog.ShowDialog(this);
                    break;
                }
            }
        }
        finally
        {
            pane.IsLoading = false;
            ViewModel.Refresh();
        }
    }

    private async void OnMenuCompressClick(object? sender, RoutedEventArgs e)
    {
        List<string> paths = ViewModel.ActivePane.GetSelection()
            .Select(i => i.FullPath)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }

        CompressDialog dialog = new(paths, ViewModel.CurrentPath);
        string? resultPath = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(resultPath) && File.Exists(resultPath))
        {
            ViewModel.Refresh();
        }
    }
    
    private void OnSidebarRemoveBookmarkClick(object? sender, RoutedEventArgs e)
    {
        PlaceItem? selected = GetContextPlaceItem(sender);
        if (selected == null || selected.Category != PlaceCategory.Bookmarks)
        {
            return;
        }

        ViewModel.RemoveBookmark(selected.Path);
    }

    private async void OnMenuConnectToServerClick(object? sender, RoutedEventArgs e)
    {
        await OpenConnectToServerDialogAsync();
    }

    private async void OnSidebarConnectToServerClick(object? sender, RoutedEventArgs e)
    {
        await OpenConnectToServerDialogAsync();
    }

    private async Task OpenConnectToServerDialogAsync()
    {
        ConnectToServerDialog dialog = new();
        string? mountPath = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(mountPath) && Directory.Exists(mountPath))
        {
            ViewModel.LoadPlaces();
            ViewModel.NavigateTo(mountPath);
        }
    }

    private async void OnSidebarDismountClick(object? sender, RoutedEventArgs e)
    {
        PlaceItem? selected = GetContextPlaceItem(sender);
        if (selected == null || selected.IsHeader || string.IsNullOrEmpty(selected.Path))
        {
            return;
        }

        if (selected.Category != PlaceCategory.Devices && selected.Category != PlaceCategory.Network)
        {
            return;
        }

        await ViewModel.DismountPlaceAsync(selected);
    }

    private async void OnSidebarDismountButtonClick(object? sender, RoutedEventArgs e)
    {
        Button? button = sender as Button;
        PlaceItem? item = button?.DataContext as PlaceItem;
        if (item == null || item.IsHeader || string.IsNullOrEmpty(item.Path))
        {
            return;
        }

        await ViewModel.DismountPlaceAsync(item);
    }

    private PlaceItem? GetContextPlaceItem(object? sender)
    {
        MenuItem? menuItem = sender as MenuItem;
        ContextMenu? menu = menuItem?.Parent as ContextMenu;
        ListBoxItem? lbi = menu?.PlacementTarget as ListBoxItem ?? FindVisualParent<ListBoxItem>(menu?.PlacementTarget);
        if (lbi?.DataContext is PlaceItem place)
        {
            return place;
        }

        return _contextPlace ?? SidebarListBox.SelectedItem as PlaceItem;
    }

    private bool _altChordHandled;

    private static bool IsAltKey(Key key)
    {
        return key == Key.LeftAlt || key == Key.RightAlt;
    }

    private void OnAltChordKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsAltKey(e.Key))
        {
            _altChordHandled = false;
            return;
        }

        if (e.Handled && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && !MainMenu.IsOpen)
        {
            _altChordHandled = true;
        }
    }

    private void OnAltKeyUp(object? sender, KeyEventArgs e)
    {
        if (!IsAltKey(e.Key) || !_altChordHandled)
        {
            return;
        }

        _altChordHandled = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (MainMenu.IsOpen)
            {
                MainMenu.Close();
            }

            FocusActiveSelection();
        }, DispatcherPriority.Input);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IInputElement? focused = FocusManager?.GetFocusedElement();
            if (focused != null && focused != this)
            {
                return;
            }

            FocusActiveSelection();
        }, DispatcherPriority.Input);
    }

    private void FocusActiveSelection()
    {
        ListBox? activeList = GetActiveListBox();
        if (activeList == null)
        {
            return;
        }

        int index = activeList.SelectedIndex;
        Control? container = index >= 0 ? activeList.ContainerFromIndex(index) : null;
        if (container != null)
        {
            container.Focus();
            return;
        }

        activeList.Focus();
    }

    private bool ShouldRedirectArrowKey(IInputElement? focused)
    {
        if (focused is TextBox)
        {
            return false;
        }

        if (focused is not Visual visual || focused == this)
        {
            return true;
        }

        ListBox? list = FindVisualParent<ListBox>(visual);
        if (list != null && (list == SidebarListBox || _fileListBoxes.Contains(list)))
        {
            return false;
        }

        Menu? menu = FindVisualParent<Menu>(visual);
        return menu == null || !menu.IsOpen;
    }

    private bool TryRedirectArrowKey(KeyEventArgs e)
    {
        if (!IsPlainArrowKey(e) || !ShouldRedirectArrowKey(FocusManager?.GetFocusedElement()))
        {
            return false;
        }

        if (TrySelectEdgeItem(e.Key))
        {
            return true;
        }

        FocusActiveSelection();
        IInputElement? target = FocusManager?.GetFocusedElement();
        if (target is not Interactive interactive || ShouldRedirectArrowKey(target))
        {
            return true;
        }

        interactive.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = e.Key,
            KeyModifiers = e.KeyModifiers,
            Source = interactive,
        });
        return true;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (TryRedirectArrowKey(e))
        {
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.Key == Key.Left)
            {
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                FocusActiveListBox();
                ViewModel.GoBack();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right)
            {
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                FocusActiveListBox();
                ViewModel.GoForward();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                FocusActiveListBox();
                ViewModel.GoUp();
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Back && _typeAheadBuffer.Length > 0 && (DateTime.UtcNow - _typeAheadLastKeyTime).TotalMilliseconds <= 1000)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            IInputElement? focusedElement = topLevel?.FocusManager?.GetFocusedElement();
            if (focusedElement is not TextBox)
            {
                _typeAheadBuffer = _typeAheadBuffer[..^1];
                _typeAheadLastKeyTime = DateTime.UtcNow;

                if (_typeAheadBuffer.Length > 0)
                {
                    IList<FileItemViewModel> items = ViewModel.ActivePane.Items;
                    FileItemViewModel? match = items.FirstOrDefault(i => i.Name.StartsWith(_typeAheadBuffer, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        SelectTypeAheadMatch(match);
                    }
                }

                e.Handled = true;
                return;
            }
        }
    }

    private void OnWindowTextInput(object? sender, TextInputEventArgs e)
    {
        if (_potentialFileDrag || _isRubberBanding || _isSidebarDragging || _isResizingCol || ViewModel.IsPathEditing)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        IInputElement? focusedElement = topLevel?.FocusManager?.GetFocusedElement();
        if (focusedElement is TextBox)
        {
            return;
        }

        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        TimeSpan elapsed = now - _typeAheadLastKeyTime;
        _typeAheadLastKeyTime = now;

        IList<FileItemViewModel> items = ViewModel.ActivePane.Items;
        if (items.Count == 0)
        {
            return;
        }

        string candidate = elapsed.TotalMilliseconds > 1000 ? e.Text : (_typeAheadBuffer + e.Text);
        FileItemViewModel? match = items.FirstOrDefault(i => i.Name.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            _typeAheadBuffer = candidate;
            SelectTypeAheadMatch(match);
            e.Handled = true;
            return;
        }

        if (elapsed.TotalMilliseconds <= 1000 && e.Text.Length == 1 && _typeAheadBuffer.Length >= 1 && e.Text.Equals(_typeAheadBuffer[0].ToString(), StringComparison.OrdinalIgnoreCase))
        {
            FileItemViewModel? current = ViewModel.ActivePane.SelectedItem;
            int startIndex = current != null ? items.IndexOf(current) : -1;
            FileItemViewModel? nextMatch = null;

            for (int i = 1; i <= items.Count; i++)
            {
                int index = (startIndex + i) % items.Count;
                if (items[index].Name.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))
                {
                    nextMatch = items[index];
                    break;
                }
            }

            if (nextMatch != null)
            {
                _typeAheadBuffer = e.Text;
                SelectTypeAheadMatch(nextMatch);
                e.Handled = true;
                return;
            }
        }

        _typeAheadBuffer = candidate;
    }

    private void SelectTypeAheadMatch(FileItemViewModel match)
    {
        ListBox? activeList = GetActiveListBox();
        if (activeList == null)
        {
            return;
        }

        PaneViewModel pane = ViewModel.ActivePane;
        int index = pane.Items.IndexOf(match);

        activeList.Selection.Clear();
        if (index >= 0)
        {
            activeList.Selection.Select(index);
        }

        activeList.SelectedItem = match;
        activeList.ScrollIntoView(match);
        pane.SelectedItem = match;
        SyncPaneSelection(pane);

        activeList.Focus();
        if (index >= 0)
        {
            Control? container = activeList.ContainerFromIndex(index);
            container?.Focus();
        }
    }

    private void OnFilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual visualHit && (visualHit is ScrollBar || FindVisualParent<ScrollBar>(visualHit) != null)) return;

        if (sender is not ListBox { DataContext: PaneViewModel pane } activeList)
        {
            return;
        }

        ViewModel.ActivePane = pane;
        (Canvas canvas, Border rect) = GetRubberBand(activeList);
        PointerPoint point = e.GetCurrentPoint(activeList);
        ListBoxItem? clickedItem = FindVisualParent<ListBoxItem>((Visual?) e.Source);
        
        if (point.Properties.IsRightButtonPressed)
        {
            if (clickedItem?.DataContext is FileItemViewModel fileItem)
            {
                if (!IsInListSelection(activeList, fileItem))
                {
                    activeList.Selection.Clear();
                    activeList.SelectedItem = fileItem;
                }
            }
            else
            {
                activeList.Selection.Clear();
                ViewModel.ActivePane.SelectedItem = null;
            }

            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            if (clickedItem?.DataContext is FileItemViewModel fileItem)
            {
                if (IsInListSelection(activeList, fileItem) &&
                    !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    _draggedFile = fileItem;
                    _fileDragStart = e.GetPosition(this);
                    _potentialFileDrag = true;
                    _clickedAlreadySelectedItem = fileItem;
                    clickedItem.Focus();
                    e.Handled = true;

                    return;
                }

                _clickedAlreadySelectedItem = null;
                _potentialFileDrag = false;
                _draggedFile = null;

                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    activeList.Selection.Clear();
                    activeList.SelectedItem = fileItem;

                    _draggedFile = fileItem;
                    _fileDragStart = e.GetPosition(this);
                    _potentialFileDrag = true;
                }
            }
            else
            {
                activeList.Focus();
                _clickedAlreadySelectedItem = null;
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    activeList.Selection.Clear();
                    ViewModel.ActivePane.SelectedItem = null;
                }

                _isRubberBanding = true;
                Point startPt = e.GetPosition(canvas);
                double maxX = Math.Max(0, canvas.Bounds.Width);
                double maxY = Math.Max(0, canvas.Bounds.Height);
                _rubberBandStart = new(Math.Clamp(startPt.X, 0, maxX), Math.Clamp(startPt.Y, 0, maxY));

                Canvas.SetLeft(rect, _rubberBandStart.X);
                Canvas.SetTop(rect, _rubberBandStart.Y);
                rect.Width = 0;
                rect.Height = 0;
                rect.IsVisible = true;

                e.Pointer.Capture(activeList);
            }
        }
    }

    private async void OnFilePointerMoved(object? sender, PointerEventArgs e)
    {
        ListBox activeList = (ListBox) sender!;
        (Canvas canvas, Border rect) = GetRubberBand(activeList);

        if (_isRubberBanding)
        {
            Point curr = e.GetPosition(canvas);
            double maxX = Math.Max(0, canvas.Bounds.Width);
            double maxY = Math.Max(0, canvas.Bounds.Height);
            double clampedStartX = Math.Clamp(_rubberBandStart.X, 0, maxX);
            double clampedStartY = Math.Clamp(_rubberBandStart.Y, 0, maxY);
            double clampedCurrX = Math.Clamp(curr.X, 0, maxX);
            double clampedCurrY = Math.Clamp(curr.Y, 0, maxY);

            double x = Math.Min(clampedStartX, clampedCurrX);
            double y = Math.Min(clampedStartY, clampedCurrY);
            double w = Math.Abs(clampedCurrX - clampedStartX);
            double h = Math.Abs(clampedCurrY - clampedStartY);

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = w;
            rect.Height = h;

            Rect selBounds = new(x, y, w, h);

            foreach (object? obj in activeList.Items)
            {
                if (obj is not FileItemViewModel fileItem)
                {
                    continue;
                }

                ListBoxItem? container = activeList.ContainerFromItem(fileItem) as ListBoxItem;
                if (container == null)
                {
                    continue;
                }

                Point pt = container.TranslatePoint(new(0, 0), canvas) ?? new();
                Rect itemBounds = new(pt.X, pt.Y, container.Bounds.Width, container.Bounds.Height);

                if (selBounds.Intersects(itemBounds))
                {
                    if (!IsInListSelection(activeList, fileItem))
                    {
                        activeList.Selection.Select(activeList.IndexFromContainer(container));
                    }
                }
                else
                {
                    if (IsInListSelection(activeList, fileItem))
                    {
                        activeList.Selection.Deselect(activeList.IndexFromContainer(container));
                    }
                }
            }

            return;
        }

        if (_potentialFileDrag && _draggedFile != null)
        {
            Point curr = e.GetPosition(this);
            if (Math.Abs(curr.X - _fileDragStart.X) > 6 || Math.Abs(curr.Y - _fileDragStart.Y) > 6)
            {
                _potentialFileDrag = false;
                _clickedAlreadySelectedItem = null;

                List<string> paths = GetSelectedItems().Select(i => i.FullPath).ToList();
                if (paths.Count == 0 || !paths.Contains(_draggedFile.FullPath))
                {
                    paths.Clear();
                    paths.Add(_draggedFile.FullPath);
                }

                FileItemViewModel draggedItem = _draggedFile;
                _draggedFile = null;

                if (OperatingSystem.IsLinux())
                {
                    X11XdndHook.StartDrag(this, paths, draggedItem);
                    return;
                }

                DataObject data = new();
                List<IStorageItem> storageItems = new();
                foreach (string path in paths)
                {
                    IStorageItem? item = Directory.Exists(path)
                        ? await StorageProvider.TryGetFolderFromPathAsync(path)
                        : await StorageProvider.TryGetFileFromPathAsync(path);
                    if (item != null)
                    {
                        storageItems.Add(item);
                    }
                }

                if (storageItems.Count > 0)
                {
                    data.Set(DataFormats.Files, storageItems);
                }
                else
                {
                    data.Set(DataFormats.Files, paths);
                }

                string uriList = string.Join("\r\n", paths.Select(p => new Uri(p, UriKind.Absolute).AbsoluteUri)) + "\r\n";
                data.Set("text/uri-list", uriList);
                data.Set(DataFormats.Text, string.Join(Environment.NewLine, paths));

                try
                {
                    await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
                }
                finally
                {
                    ClearDropTargets();
                }
            }
        }
    }

    private void OnFilePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ListBox activeList = (ListBox) sender!;
        if (_isRubberBanding)
        {
            _isRubberBanding = false;
            GetRubberBand(activeList).Rect.IsVisible = false;
            e.Pointer.Capture(null);
            return;
        }

        if (_clickedAlreadySelectedItem != null)
        {
            activeList.Selection.Clear();
            activeList.SelectedItem = _clickedAlreadySelectedItem;
            _clickedAlreadySelectedItem = null;
        }

        ClearDropTargets();
        _potentialFileDrag = false;
        _draggedFile = null;

        if (OperatingSystem.IsLinux() && X11XdndHook.IsDragActive)
        {
            X11XdndHook.DropDrag();
        }
    }

    private void OnAppDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropTargets();
    }

    private bool IsInvalidDropDestination(string targetPath, IEnumerable<string>? sourcePaths = null)
    {
        if (string.IsNullOrEmpty(targetPath))
        {
            return true;
        }

        IEnumerable<string> sources = sourcePaths ?? X11XdndHook.ActiveDragPaths ?? GetSelectedItems().Select(i => i.FullPath);
        string normTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar);

        foreach (string src in sources)
        {
            if (string.IsNullOrEmpty(src))
            {
                continue;
            }

            string normSrc = Path.GetFullPath(src).TrimEnd(Path.DirectorySeparatorChar);

            if (string.Equals(normTarget, normSrc, StringComparison.OrdinalIgnoreCase) ||
                normTarget.StartsWith(normSrc + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void HighlightDropTarget(Point point)
    {
        ClearDropTargets();
        Visual? hit = this.GetVisualAt(point);

        Button? hoverBreadcrumb = FindVisualParent<Button>(hit);
        if (hoverBreadcrumb?.Tag is string bcPath && Directory.Exists(bcPath))
        {
            if (!string.Equals(bcPath, ViewModel.CurrentPath, StringComparison.OrdinalIgnoreCase))
            {
                if (IsInvalidDropDestination(bcPath))
                {
                    return;
                }

                _hoveredBreadcrumbButton = hoverBreadcrumb;
                _hoveredBreadcrumbPath = bcPath;
                hoverBreadcrumb.Background = Brush.Parse("#2A4B7C");
                return;
            }
        }

        ListBoxItem? hoverSidebar = FindVisualParent<ListBoxItem>(hit);
        if (hoverSidebar?.DataContext is PlaceItem place && !place.IsHeader && Directory.Exists(place.Path))
        {
            if (IsInvalidDropDestination(place.Path))
            {
                return;
            }

            place.IsDropTarget = true;
            return;
        }

        ListBoxItem? hoverFile = FindVisualParent<ListBoxItem>(hit);
        if (hoverFile?.DataContext is FileItemViewModel targetFolder && targetFolder.IsDirectory && IsOverItemDropZone(hit))
        {
            if (IsInvalidDropDestination(targetFolder.FullPath))
            {
                return;
            }

            targetFolder.IsDropTarget = true;
        }
    }

    private void OnAppDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy | DragDropEffects.Move;
        e.Handled = true;

        HighlightDropTarget(e.GetPosition(this));
    }

    public void HandleFilesDropped(List<string> paths, Point? windowPosition = null, bool isCopy = false)
    {
        if (paths.Count == 0)
        {
            return;
        }

        _ = Logger.LogI($"Dropped {paths.Count} items (copy: {isCopy})");

        string targetDir = ViewModel.CurrentPath;
        if (windowPosition.HasValue)
        {
            Visual? hit = this.GetVisualAt(windowPosition.Value);
            Button? hoverBreadcrumb = FindVisualParent<Button>(hit);
            if (hoverBreadcrumb?.Tag is string bcPath && Directory.Exists(bcPath) && !IsInvalidDropDestination(bcPath, paths))
            {
                targetDir = bcPath;
            }
            else
            {
                ListBoxItem? hoverSidebar = FindVisualParent<ListBoxItem>(hit);
                if (hoverSidebar?.DataContext is PlaceItem place && !place.IsHeader && Directory.Exists(place.Path) && !IsInvalidDropDestination(place.Path, paths))
                {
                    targetDir = place.Path;
                }
                else
                {
                    ListBoxItem? hoverFile = FindVisualParent<ListBoxItem>(hit);
                    if (hoverFile?.DataContext is FileItemViewModel targetFolder && targetFolder.IsDirectory && IsOverItemDropZone(hit) && !IsInvalidDropDestination(targetFolder.FullPath, paths))
                    {
                        targetDir = targetFolder.FullPath;
                    }
                    else if (GetPane(FindVisualParent<ListBox>(hit)) is PaneViewModel hoverPane)
                    {
                        targetDir = hoverPane.CurrentPath;
                    }
                }
            }
        }

        ClearDropTargets();
        ViewModel.TransferItemsTo(paths, targetDir, isCopy);
    }

    private void OnAppDragDrop(object? sender, DragEventArgs e)
    {
        List<string> paths = ExtractDroppedPaths(e.Data);
        if (paths.Count == 0)
        {
            return;
        }

        bool isCopy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        HandleFilesDropped(paths, e.GetPosition(this), isCopy);
        e.Handled = true;
    }

    private void OnCancelOperationClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.CancelOperation();
    }

    private static List<string> ExtractDroppedPaths(IDataObject data)
    {
        List<string> paths = new();

        object? filesObj = data.Get(DataFormats.Files);
        if (filesObj is IEnumerable<string> stringPaths)
        {
            paths.AddRange(stringPaths);
        }
        else if (filesObj is IEnumerable<IStorageItem> storageItems)
        {
            foreach (IStorageItem item in storageItems)
            {
                paths.Add(item.Path.LocalPath);
            }
        }

        if (paths.Count > 0)
        {
            return paths;
        }

        string[] formats = ["text/uri-list", "x-special/gnome-copied-files", "text/plain", "STRING", "UTF8_STRING"];
        foreach (string format in formats)
        {
            object? val = data.Get(format);
            string? text = val switch
            {
                string s => s,
                byte[] b => Encoding.UTF8.GetString(b),
                _ => null,
            };

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line == "copy" || line == "cut")
                {
                    continue;
                }

                if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri))
                    {
                        paths.Add(uri.LocalPath);
                    }
                }
                else if (File.Exists(line) || Directory.Exists(line))
                {
                    paths.Add(line);
                }
            }

            if (paths.Count > 0)
            {
                break;
            }
        }

        return paths;
    }

    private void ClearDropTargets()
    {
        if (_hoveredBreadcrumbButton != null)
        {
            _hoveredBreadcrumbButton.ClearValue(BackgroundProperty);
            _hoveredBreadcrumbButton = null;
            _hoveredBreadcrumbPath = null;
        }

        foreach (PlaceItem p in ViewModel.Places)
        {
            p.IsDropTarget = false;
        }

        foreach (FileItemViewModel f in ViewModel.AllItems)
        {
            f.IsDropTarget = false;
        }
    }

    private static bool IsOverItemDropZone(Visual? visual)
    {
        while (visual != null && visual is not ListBoxItem)
        {
            if (visual.Classes.Contains("drop-zone"))
            {
                return true;
            }

            visual = visual.GetVisualParent();
        }

        return false;
    }

    private static T? FindVisualParent<T>(Visual? visual) where T : Visual
    {
        while (visual != null)
        {
            if (visual is T typed)
            {
                return typed;
            }

            visual = visual.GetVisualParent();
        }

        return null;
    }

    private void OnFileContextMenuOpening(object? sender, CancelEventArgs e)
    {
        MenuItem? colorMenu = (sender as ContextMenu)?.Items
            .OfType<MenuItem>()
            .FirstOrDefault(m => m.Name == "FolderColorMenuItem");
        if (colorMenu == null || colorMenu.ItemsSource != null)
        {
            return;
        }

        PopulateFolderColorMenu(colorMenu);
    }

    private void PopulateFolderColorMenu(MenuItem menu)
    {
        List<object> items = new();
        foreach ((string name, string hex) in FolderColorService.Presets)
        {
            MenuItem item = new()
            {
                Header = LocalizationService.Get($"Color{name}"),
                Tag = hex,
                Icon = new Border
                {
                    Width = 14,
                    Height = 14,
                    CornerRadius = new(7),
                    Background = new SolidColorBrush(Color.Parse(hex)),
                },
            };
            item.Click += OnFolderColorPresetClick;
            items.Add(item);
        }

        items.Add(new Separator());

        MenuItem custom = new() { Header = LocalizationService.Get("CustomColor") };
        custom.Click += OnFolderColorCustomClick;
        items.Add(custom);

        MenuItem reset = new() { Header = LocalizationService.Get("DefaultColor") };
        reset.Click += (_, _) => ApplyFolderColor(null);
        items.Add(reset);

        menu.ItemsSource = items;
    }

    private void OnFolderColorPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string hex })
        {
            ApplyFolderColor(hex);
        }
    }

    private async void OnFolderColorCustomClick(object? sender, RoutedEventArgs e)
    {
        List<FileItemViewModel> folders = GetSelectedItems()
            .Where(i => i.IsDirectory)
            .ToList();
        if (folders.Count == 0)
        {
            return;
        }

        FolderColorDialog dialog = new(FolderColorService.Get(folders[0].FullPath));
        string? hex = await dialog.ShowDialog<string?>(this);
        if (hex == null)
        {
            return;
        }

        ApplyFolderColor(hex, folders);
    }

    private void ApplyFolderColor(string? hex, List<FileItemViewModel>? folders = null)
    {
        folders ??= GetSelectedItems()
            .Where(i => i.IsDirectory)
            .ToList();
        if (folders.Count == 0)
        {
            return;
        }

        FolderColorService.Set(folders.Select(f => f.FullPath), hex);

        HashSet<string> paths = folders
            .Select(f => f.FullPath)
            .ToHashSet();
        foreach (FileItemViewModel item in ViewModel.AllItems)
        {
            if (paths.Contains(item.FullPath))
            {
                item.RefreshIcon();
            }
        }
    }

    private Task<ConflictResolution> ResolveConflictAsync(TransferConflict conflict)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            ConflictDialog dialog = new(conflict);
            ConflictResolution? resolution = await dialog.ShowDialog<ConflictResolution?>(this);
            return resolution ?? new ConflictResolution(ConflictAction.Cancel);
        });
    }

    private async void OnScriptOpenRequested(string path)
    {
        ScriptPromptDialog dialog = new(path);
        ScriptAction action = await dialog.ShowDialog<ScriptAction>(this);

        switch (action)
        {
            case ScriptAction.Run:
                ViewModel.RunScript(path, false);
                break;

            case ScriptAction.RunInTerminal:
                ViewModel.RunScript(path, true);
                break;

            case ScriptAction.ShowContent:
                ViewModel.ShowFileContent(path);
                break;
        }
    }

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        PaneViewModel? pane = GetPane(sender);
        if (pane == null)
        {
            return;
        }

        ViewModel.ActivePane = pane;
        ViewModel.OpenEntry(pane.SelectedItem);
        FocusActiveListBox();
    }

    private static bool IsPlainArrowKey(KeyEventArgs e)
    {
        bool hasModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        return !hasModifier && (e.Key == Key.Down || e.Key == Key.Up || e.Key == Key.Left || e.Key == Key.Right);
    }

    private bool TrySelectEdgeItem(Key key)
    {
        ListBox? activeList = GetActiveListBox();
        PaneViewModel pane = ViewModel.ActivePane;
        if (activeList == null || pane.Items.Count == 0 || activeList.SelectedItem != null)
        {
            return false;
        }

        int targetIndex = (key == Key.Up || key == Key.Left) ? pane.Items.Count - 1 : 0;
        FileItemViewModel targetItem = pane.Items[targetIndex];
        activeList.SelectedIndex = targetIndex;
        activeList.SelectedItem = targetItem;
        activeList.ScrollIntoView(targetItem);
        pane.SelectedItem = targetItem;
        SyncPaneSelection(pane);

        activeList.ContainerFromIndex(targetIndex)?.Focus();
        return true;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ViewModel.IsPickerMode)
            {
                ViewModel.CancelPicker();
                e.Handled = true;
                return;
            }

            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            IInputElement? focusedElement = topLevel?.FocusManager?.GetFocusedElement();
            if (focusedElement is TextBox)
            {
                return;
            }

            ClearActiveSelection();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            IInputElement? focusedElement = topLevel?.FocusManager?.GetFocusedElement();
            if (focusedElement is not TextBox)
            {
                FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
                activeSelected ??= GetSelectedItems().FirstOrDefault();
                if (activeSelected != null)
                {
                    ViewModel.OpenEntry(activeSelected);
                    FocusActiveListBox();
                    e.Handled = true;
                    return;
                }
            }
        }

        if (IsPlainArrowKey(e))
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            IInputElement? focusedElement = topLevel?.FocusManager?.GetFocusedElement();
            if (focusedElement is not TextBox)
            {
                if (TrySelectEdgeItem(e.Key))
                {
                    e.Handled = true;
                    return;
                }

                ListBox? activeList = GetActiveListBox();
                if (activeList?.SelectedItem != null && focusedElement is not ListBoxItem)
                {
                    int currentIndex = activeList.SelectedIndex;
                    if (currentIndex >= 0)
                    {
                        activeList.ContainerFromIndex(currentIndex)?.Focus();
                    }
                    else
                    {
                        activeList.Focus();
                    }
                }
            }
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.Key == Key.Left)
            {
                ViewModel.GoBack();
                FocusActiveListBox();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right)
            {
                ViewModel.GoForward();
                FocusActiveListBox();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                ViewModel.GoUp();
                FocusActiveListBox();
                e.Handled = true;
                return;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.W)
            {
                if (ViewModel.IsDualPane)
                {
                    ViewModel.CloseActivePane();
                    FocusActiveListBox();
                }
                else
                {
                    Close();
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.F)
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.K)
            {
                OnMenuConnectToServerClick(sender, e);
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.N)
            {
                OnNewFolderClick(sender, e);
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.N)
            {
                OnNewFileClick(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.N)
            {
                OnMenuNewWindowClick(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.L)
            {
                OnTogglePathEditingClick(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.H)
            {
                OnToggleHiddenClick(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.A)
            {
                OnSelectAllClick(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C)
            {
                ViewModel.CopySelected(GetSelectedItems());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.X)
            {
                ViewModel.CutSelected(GetSelectedItems());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V)
            {
                ViewModel.Paste();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D)
            {
                ViewModel.DuplicateSelected(GetSelectedItems());
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.F3)
        {
            OnToggleDualPaneClick(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6)
        {
            SwitchActivePane();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            OnRefreshClick(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            FileItemViewModel? selected = ViewModel.ActivePane.SelectedItem;
            if (selected != null)
            {
                OnMenuRenameClick(sender, e);
                e.Handled = true;
                return;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.Enter)
        {
            OnMenuPropertiesClick(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            bool permanent = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            await ConfirmAndDeleteAsync(GetSelectedItems(), permanent);
            e.Handled = true;
            return;
        }
    }

    private void SwitchActivePane()
    {
        if (!ViewModel.IsDualPane)
        {
            return;
        }

        ViewModel.ActivePane = ViewModel.ActivePane == ViewModel.Pane1 ? ViewModel.Pane2 : ViewModel.Pane1;
        ListBox? activeList = GetActiveListBox();
        if (activeList == null)
        {
            return;
        }

        int index = activeList.SelectedIndex;
        Control? container = index >= 0 ? activeList.ContainerFromIndex(index) : null;
        if (container != null)
        {
            container.Focus();
            return;
        }

        activeList.Focus();
    }

    private void ClearActiveSelection()
    {
        ListBox? activeList = GetActiveListBox();
        if (activeList != null)
        {
            activeList.Selection.Clear();
            activeList.SelectedItem = null;
        }

        ViewModel.ActivePane.ClearSelection();
    }

    private async void OnFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearActiveSelection();
            e.Handled = true;
            return;
        }

        if (IsPlainArrowKey(e) && TrySelectEdgeItem(e.Key))
        {
            e.Handled = true;
            return;
        }

        FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
        activeSelected ??= GetSelectedItems().FirstOrDefault();

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            ViewModel.OpenEntry(activeSelected);
            FocusActiveListBox();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            ViewModel.GoUp();
            FocusActiveListBox();
            e.Handled = true;
            return;
        }
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        GetActiveListBox()?.SelectAll();
    }

    private void OnMenuNewWindowClick(object? sender, RoutedEventArgs e)
    {
        string? procPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(procPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = procPath,
                UseShellExecute = false,
            });
        }
    }

    private async void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        InputDialog dialog = new(LocalizationService.Get("NewFolder"), LocalizationService.Get("FolderNamePrompt"), LocalizationService.Get("NewFolderDefaultName"));
        string? result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            ViewModel.CreateFolder(result);
        }
        else
        {
            await Logger.LogI("New folder canceled");
        }
    }

    private async void OnNewFileClick(object? sender, RoutedEventArgs e)
    {
        InputDialog dialog = new(LocalizationService.Get("NewFile"), LocalizationService.Get("FileNamePrompt"), LocalizationService.Get("NewFileDefaultName"));
        string? result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            ViewModel.CreateFile(result);
        }
        else
        {
            await Logger.LogI("New file canceled");
        }
    }

    private void OnOpenTerminalClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.OpenTerminal();
    }

    private void OnEmptyTrashClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.EmptyTrash();
    }

    private void OnRecoverClick(object? sender, RoutedEventArgs e)
    {
        PaneViewModel? pane = GetPane(sender);
        if (pane == null)
        {
            return;
        }

        ViewModel.RestoreSelectedFromTrash(pane);
    }

    private void OnMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
        ViewModel.OpenEntry(activeSelected);
    }

    private async void OnMenuOpenWithClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
        if (activeSelected == null || activeSelected.IsDirectory)
        {
            return;
        }

        string ext = Path.GetExtension(activeSelected.FullPath);
        OpenWithDialog dialog = new(ext);
        OpenWithResult? result = await dialog.ShowDialog<OpenWithResult?>(this);
        if (result != null && !string.IsNullOrWhiteSpace(result.Program))
        {
            ViewModel.OpenWith(activeSelected, result.Program, result.Always);
        }
    }

    private void OnMenuOpenAsRootClick(object? sender, RoutedEventArgs e)
    {
        string targetDir = ViewModel.CurrentPath;
        FileItemViewModel? selected = ViewModel.ActivePane.SelectedItem;
        if (selected != null && selected.IsDirectory)
        {
            targetDir = selected.FullPath;
        }

        ViewModel.OpenAsRoot(targetDir);
    }

    private void OnMenuOpenTerminalClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
        string currentDir = ViewModel.CurrentPath;
        string? path = activeSelected?.FullPath ?? currentDir;
        string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        ViewModel.OpenTerminal(dir);
    }

    private void OnMenuCutClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.CutSelected(GetSelectedItems());
    }

    private void OnMenuCopyClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.CopySelected(GetSelectedItems());
    }

    private void OnMenuPasteClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Paste();
    }

    private void OnMenuDuplicateClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.DuplicateSelected(GetSelectedItems());
    }

    private async void OnMenuRenameClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
        if (activeSelected is null)
        {
            return;
        }

        InputDialog dialog = new(LocalizationService.Get("RenameTitle"), LocalizationService.Get("NewNamePrompt"), activeSelected.Name);
        string? result = await dialog.ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(result))
        {
            ViewModel.RenameSelected(activeSelected, result);
        }
        else
        {
            await Logger.LogI("Rename canceled");
        }
    }

    private void OnMenuTrashClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.DeleteSelected(GetSelectedItems(), permanent: false);
    }

    private async void OnMenuDeletePermanentClick(object? sender, RoutedEventArgs e)
    {
        await ConfirmAndDeleteAsync(GetSelectedItems(), permanent: true);
    }

    private async Task ConfirmAndDeleteAsync(IEnumerable<FileItemViewModel>? items, bool permanent)
    {
        List<FileItemViewModel> targets = (items ?? GetSelectedItems()).ToList();
        if (targets.Count == 0)
        {
            FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
            if (activeSelected != null)
            {
                targets.Add(activeSelected);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        if (permanent)
        {
            string message = targets.Count == 1
                ? LocalizationService.Format("ConfirmDeleteNamed", targets[0].Name)
                : LocalizationService.Format("ConfirmDeleteCount", targets.Count);

            ConfirmDialog dialog = new(LocalizationService.Get("PermanentDeleteTitle"), message, LocalizationService.Get("Delete"), true, true);
            bool? confirmed = await dialog.ShowDialog<bool?>(this);
            if (confirmed != true)
            {
                await Logger.LogI("Permanent delete canceled");
                return;
            }
        }

        ViewModel.DeleteSelected(targets, permanent);
    }

    private void OnMenuBookmarkClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
        string currentDir = ViewModel.CurrentPath;
        string path = activeSelected?.FullPath ?? currentDir;
        if (Directory.Exists(path))
        {
            ViewModel.ToggleBookmark(path);
        }
    }

    private async void OnMenuPropertiesClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane.SelectedItem;
        if (activeSelected != null)
        {
            PropertiesDialog dialog = new(activeSelected);
            await dialog.ShowDialog(this);
            return;
        }

        string currentDir = ViewModel.CurrentPath;
        if (Directory.Exists(currentDir))
        {
            string folderIcon = IconService.GetForFolder(currentDir);
            
            FileItemViewModel folderItem = new()
            {
                Entry = new FolderEntry
                {
                    FullName = Path.GetFileName(currentDir.TrimEnd('/')),
                    FullPath = currentDir
                },
                Name = Path.GetFileName(currentDir.TrimEnd('/')),
                FullPath = currentDir,
                IsDirectory = true,
                SizeString = "-",
                TypeString = LocalizationService.Get("FolderType"),
                ModifiedString = Directory.GetLastWriteTime(currentDir).ToString("g"),
                IsHidden = false,
                IsAccessDenied = false,
                Icon = IconService.Get(folderIcon),
            };
            
            PropertiesDialog dialog = new(folderItem);
            await dialog.ShowDialog(this);
        }
    }

    private void OnMenuCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnMenuShortcutsClick(object? sender, RoutedEventArgs e)
    {
        InputDialog dialog = new(LocalizationService.Get("Shortcuts"), LocalizationService.Get("ShortcutsText"), "");
        await dialog.ShowDialog<string?>(this);
    }

    private void OnColResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _isResizingCol = true;
        _resizingCol = border.Tag?.ToString();
        _resizeStartX = e.GetPosition(this).X;
        _initColNameWidth = ViewModel.ColNameWidth;
        _initColSizeWidth = ViewModel.ColSizeWidth;
        _initColTypeWidth = ViewModel.ColTypeWidth;
        _resizeHeaderGrid = FindVisualParent<Grid>(border);

        e.Pointer.Capture(border);
        e.Handled = true;
    }

    private void OnColResizePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizingCol || _resizingCol == null) return;

        double currentX = e.GetPosition(this).X;
        double delta = currentX - _resizeStartX;
        double containerWidth = _resizeHeaderGrid?.Bounds.Width ?? double.MaxValue;

        if (_resizingCol == "Name")
        {
            double maxName = Math.Max(100, containerWidth - ViewModel.ColSizeWidth - ViewModel.ColTypeWidth - 110);
            ViewModel.ColNameWidth = Math.Clamp(_initColNameWidth + delta, 100, maxName);
        }
        else if (_resizingCol == "Size")
        {
            double maxSize = Math.Max(50, containerWidth - ViewModel.ColNameWidth - ViewModel.ColTypeWidth - 110);
            ViewModel.ColSizeWidth = Math.Clamp(_initColSizeWidth + delta, 50, maxSize);
        }
        else if (_resizingCol == "Type")
        {
            double maxType = Math.Max(50, containerWidth - ViewModel.ColNameWidth - ViewModel.ColSizeWidth - 110);
            ViewModel.ColTypeWidth = Math.Clamp(_initColTypeWidth + delta, 50, maxType);
        }

        e.Handled = true;
    }

    private void OnColResizePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isResizingCol) return;

        _isResizingCol = false;
        _resizingCol = null;
        _resizeHeaderGrid = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnColResizePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isResizingCol = false;
        _resizingCol = null;
        _resizeHeaderGrid = null;
    }

    public async Task<FilePickerResult> ShowPickerAsync(Window? owner = null)
    {
        if (owner != null)
        {
            await ShowDialog(owner);
        }
        else
        {
            TaskCompletionSource<FilePickerResult> tcs = new();
            Closed += (_, _) => tcs.TrySetResult(PickerResult ?? FilePickerResult.Cancelled());
            Show();
            return await tcs.Task;
        }

        return PickerResult ?? FilePickerResult.Cancelled();
    }

    private void OnPickerCompleted(FilePickerResult result)
    {
        PickerResult = result;
        Close(result);
    }

    private void OnPickerCancelClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.CancelPicker();
    }

    private void OnPickerConfirmClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmCurrentPicker();
    }

    private void OnPickerTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.ConfirmCurrentPicker();
            e.Handled = true;
        }
    }
}
