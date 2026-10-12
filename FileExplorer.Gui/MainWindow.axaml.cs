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
            Title = pickerOptions.Title ?? (pickerOptions.Mode == PickerMode.Folder ? "Select Folder" : "Select File");
            ViewModel.PickerCompleted += OnPickerCompleted;
        }

        Closing += (_, _) =>
        {
            PickerResult ??= FilePickerResult.Cancelled();
        };

        ViewModel.BeforeFolderLoad += () =>
        {
            GridFilesListBox.Selection.Clear();
            ListFilesListBox.Selection.Clear();
            Pane2GridFilesListBox.Selection.Clear();
            Pane2ListFilesListBox.Selection.Clear();
        };

        Opened += (s, e) =>
        {
            IPlatformHandle? handle = TryGetPlatformHandle();
            if (handle != null)
            {
                X11WindowHelper.ConfigureWindow(handle.Handle);
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
            Title = "Ferret (Privileged)";
        }

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.CurrentPath) || e.PropertyName == nameof(MainWindowViewModel.ActivePane))
            {
                ScrollBreadcrumbsToEnd();
                SyncSidebarSelection(ViewModel.CurrentPath);
            }
            else if (e.PropertyName == nameof(MainWindowViewModel.IsDualPane))
            {
                if (ViewModel.IsDualPane)
                {
                    FilePanesGrid.ColumnDefinitions[0].Width = new(1, GridUnitType.Star);
                    FilePanesGrid.ColumnDefinitions[2].Width = new(1, GridUnitType.Star);
                }
                else
                {
                    FilePanesGrid.ColumnDefinitions[0].Width = new(1, GridUnitType.Star);
                    FilePanesGrid.ColumnDefinitions[2].Width = new(0, GridUnitType.Pixel);
                }
            }
        };

        ScrollBreadcrumbsToEnd();
        SyncSidebarSelection(ViewModel.CurrentPath);

        SidebarListBox.AddHandler(PointerPressedEvent, OnSidebarPointerPressed, Tunnel);
        SidebarListBox.AddHandler(PointerMovedEvent, OnSidebarPointerMoved, Tunnel);
        SidebarListBox.AddHandler(PointerReleasedEvent, OnSidebarPointerReleased, Tunnel);

        GridFilesListBox.AddHandler(PointerPressedEvent, OnFilePointerPressed, Tunnel);
        GridFilesListBox.AddHandler(PointerMovedEvent, OnFilePointerMoved, Tunnel);
        GridFilesListBox.AddHandler(PointerReleasedEvent, OnFilePointerReleased, Tunnel);

        ListFilesListBox.AddHandler(PointerPressedEvent, OnFilePointerPressed, Tunnel);
        ListFilesListBox.AddHandler(PointerMovedEvent, OnFilePointerMoved, Tunnel);
        ListFilesListBox.AddHandler(PointerReleasedEvent, OnFilePointerReleased, Tunnel);

        Pane2GridFilesListBox.AddHandler(PointerPressedEvent, OnFilePointerPressed, Tunnel);
        Pane2GridFilesListBox.AddHandler(PointerMovedEvent, OnFilePointerMoved, Tunnel);
        Pane2GridFilesListBox.AddHandler(PointerReleasedEvent, OnFilePointerReleased, Tunnel);

        Pane2ListFilesListBox.AddHandler(PointerPressedEvent, OnFilePointerPressed, Tunnel);
        Pane2ListFilesListBox.AddHandler(PointerMovedEvent, OnFilePointerMoved, Tunnel);
        Pane2ListFilesListBox.AddHandler(PointerReleasedEvent, OnFilePointerReleased, Tunnel);

        AddHandler(DropEvent, OnAppDragDrop, Tunnel | Bubble, true);
        AddHandler(DragOverEvent, OnAppDragOver, Tunnel | Bubble, true);
        AddHandler(DragLeaveEvent, OnAppDragLeave, Tunnel | Bubble, true);

        Focusable = true;
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, Tunnel);
        AddHandler(KeyDownEvent, OnGlobalKeyDown, Tunnel);
        AddHandler(TextInputEvent, OnWindowTextInput, Tunnel);
    }

    private void ScrollBreadcrumbsToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (BreadcrumbsScrollViewer != null)
            {
                BreadcrumbsScrollViewer.Offset = new(double.MaxValue, 0);
            }
        }, DispatcherPriority.Loaded);
    }

    private ListBox GetActiveListBox()
    {
        if (ViewModel.ActivePane == 2)
        {
            return ViewModel.IsGridView ? Pane2GridFilesListBox : Pane2ListFilesListBox;
        }

        return ViewModel.IsGridView ? GridFilesListBox : ListFilesListBox;
    }

    private (Canvas Canvas, Border Rect) GetActiveRubberBand()
    {
        if (ViewModel.ActivePane == 2)
        {
            return (SelectionCanvas2, RubberBandRect2);
        }

        return (SelectionCanvas1, RubberBandRect1);
    }

    private IEnumerable<FileItemViewModel> GetSelectedItems()
    {
        ListBox activeList = GetActiveListBox();
        if (activeList.SelectedItems is null || activeList.SelectedItems.Count == 0)
        {
            FileItemViewModel? selected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
            if (selected != null)
            {
                return [selected];
            }

            return Enumerable.Empty<FileItemViewModel>();
        }

        return activeList.SelectedItems.Cast<FileItemViewModel>();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoBack();
    }

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoForward();
    }

    private void OnUpClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoUp();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
    }

    private void OnGoHomeClick(object? sender, RoutedEventArgs e)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Directory.Exists(home))
        {
            ViewModel.NavigateTo(home);
        }
    }

    private void OnGoRootClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.NavigateTo("/");
    }

    private void OnToggleHiddenClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ShowHiddenFiles = !ViewModel.ShowHiddenFiles;
    }

    private void OnToggleDualPaneClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDualPane();
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

    private void OnPane2BreadcrumbBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Button? clickedButton = FindVisualParent<Button>((Visual?) e.Source);
        if (clickedButton != null)
        {
            return;
        }

        ViewModel.ActivePane = 2;
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
            GetActiveListBox().Focus();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            GetActiveListBox().Focus();
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
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        List<FileItemViewModel> selected = GetSelectedItems().ToList();
        ViewModel.SetSelectedItems(selected);
    }

    private void OnPane1GotFocus(object? sender, GotFocusEventArgs e)
    {
        ViewModel.ActivePane = 1;
    }

    private void OnPane2GotFocus(object? sender, GotFocusEventArgs e)
    {
        ViewModel.ActivePane = 2;
    }

    private void OnPane2BackClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoBackPane2();
    }

    private void OnPane2ForwardClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoForwardPane2();
    }

    private void OnPane2UpClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.GoUpPane2();
    }

    private void OnPane2BreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        Button? button = sender as Button;
        string? targetPath = button?.Tag as string;
        if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath))
        {
            return;
        }

        ViewModel.NavigatePane2To(targetPath);
    }

    private void OnPane2FileDoubleTapped(object? sender, TappedEventArgs e)
    {
        ViewModel.ActivePane = 2;
        ViewModel.OpenEntry(ViewModel.Pane2SelectedItem);
    }

    private bool _isSyncingSidebar;

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

            PlaceItem? match = null;
            foreach (PlaceItem item in ViewModel.Places)
            {
                if (item.IsHeader || string.IsNullOrEmpty(item.Path))
                {
                    continue;
                }

                string itemNorm = Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar);
                if (string.IsNullOrEmpty(itemNorm))
                {
                    itemNorm = "/";
                }

                if (string.Equals(normPath, itemNorm, StringComparison.OrdinalIgnoreCase))
                {
                    match = item;
                    break;
                }
            }

            SidebarListBox.SelectedItem = match;
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

        if (Directory.Exists(selected.Path))
        {
            if (ViewModel.ActivePane == 2)
            {
                ViewModel.NavigatePane2To(selected.Path);
            }
            else
            {
                ViewModel.NavigateTo(selected.Path);
            }
        }
    }

    private async Task MountDeviceAsync(PlaceItem selected)
    {
        string devPath = selected.DevicePath ?? selected.Path;
        if (string.IsNullOrEmpty(devPath))
        {
            return;
        }

        ViewModel.StatusText = $"{LocalizationService.Get("Mounting")} {selected.Name}...";

        string? mountPath = await ViewModel.MountDeviceAsync(devPath);
        if (!string.IsNullOrEmpty(mountPath) && Directory.Exists(mountPath))
        {
            selected.Path = mountPath;
            selected.IsMounted = true;
            if (ViewModel.ActivePane == 2)
            {
                ViewModel.NavigatePane2To(mountPath);
            }
            else
            {
                ViewModel.NavigateTo(mountPath);
            }

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
                SidebarListBox.SelectedItem = rightPlace;
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
        if (item?.DataContext is not PlaceItem place || place.IsHeader)
        {
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
        if (_draggedPlace == null)
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

        foreach (PlaceItem p in ViewModel.Places)
        {
            p.ShowDropLineAbove = false;
            p.ShowDropLineBelow = false;
        }

        if (targetItem == null || targetPlace == null || targetPlace == _draggedPlace || targetPlace.IsHeader || targetPlace.Category != _draggedPlace.Category)
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
        if (_isSidebarDragging && _draggedPlace != null)
        {
            PlaceItem? target = ViewModel.Places.FirstOrDefault(p => p.ShowDropLineAbove || p.ShowDropLineBelow);
            if (target != null)
            {
                ViewModel.ReorderPlace(_draggedPlace, target, target.ShowDropLineAbove);
            }
        }

        foreach (PlaceItem p in ViewModel.Places)
        {
            p.ShowDropLineAbove = false;
            p.ShowDropLineBelow = false;
        }

        _draggedPlace = null;
        _isSidebarDragging = false;
        e.Pointer.Capture(null);
    }

    private void OnSidebarContextMenuOpening(object? sender, CancelEventArgs e)
    {
        PlaceItem? selected = SidebarListBox.SelectedItem as PlaceItem;
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

        if (Directory.Exists(selected.Path))
        {
            if (ViewModel.ActivePane == 2)
            {
                ViewModel.NavigatePane2To(selected.Path);
            }
            else
            {
                ViewModel.NavigateTo(selected.Path);
            }
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

        string? password = ViewModel.GetNetworkPassword(uri);
        if (string.IsNullOrEmpty(password) && !NetworkService.ParseUri(uri).Anonymous)
        {
            InputDialog pwdDialog = new("Authentication Required", $"Enter password for {item.Name}:", "", true);
            password = await pwdDialog.ShowDialog<string?>(this);
            if (password == null)
            {
                return;
            }

            ViewModel.SetNetworkPassword(uri, password);
        }

        if (ViewModel.ActivePane == 2)
        {
            ViewModel.IsPane2Loading = true;
        }
        else
        {
            ViewModel.IsPane1Loading = true;
        }

        ViewModel.StatusText = LocalizationService.Get("Connecting");

        try
        {
            (bool success, string mountPath, string error) = await ViewModel.MountNetworkBookmarkAsync(uri, password);

            if (success && !string.IsNullOrEmpty(mountPath) && Directory.Exists(mountPath))
            {
                ViewModel.LoadPlaces();
                if (ViewModel.ActivePane == 2)
                {
                    ViewModel.NavigatePane2To(mountPath);
                }
                else
                {
                    ViewModel.NavigateTo(mountPath);
                }
            }
            else
            {
                ViewModel.StatusText = $"Connection failed: {error}";
                string msg = string.IsNullOrWhiteSpace(error) ? "Connection failed or timed out." : error;
                ConfirmDialog errDialog = new("Connection Failed", msg);
                await errDialog.ShowDialog(this);
            }
        }
        finally
        {
            if (ViewModel.ActivePane == 2)
            {
                ViewModel.IsPane2Loading = false;
            }
            else
            {
                ViewModel.IsPane1Loading = false;
            }
        }
    }

    private async void OnMenuExtractHereClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<FileItemViewModel> selectedItems = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItems : ViewModel.SelectedItems;
        if (selectedItems.Count == 0 && (ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem) is FileItemViewModel single)
        {
            selectedItems = [single];
        }

        List<FileItemViewModel> targets = selectedItems.Where(i => i.FileType == FileType.Archive || MainWindowViewModel.IsArchiveFile(i.FullPath)).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        string currentDir = ViewModel.ActivePane == 2 ? ViewModel.Pane2Path : ViewModel.CurrentPath;
        if (string.IsNullOrEmpty(currentDir) || !Directory.Exists(currentDir))
        {
            return;
        }

        ArchiveService archiveService = new();

        if (ViewModel.ActivePane == 2)
        {
            ViewModel.IsPane2Loading = true;
        }
        else
        {
            ViewModel.IsPane1Loading = true;
        }

        try
        {
            foreach (FileItemViewModel item in targets)
            {
                string? password = null;
                bool isEncrypted = await archiveService.IsEncryptedArchiveAsync(item.FullPath);
                if (isEncrypted)
                {
                    InputDialog pwdDialog = new("Encrypted Archive", $"Enter password for {item.Name}:", "", true);
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
            if (ViewModel.ActivePane == 2)
            {
                ViewModel.IsPane2Loading = false;
            }
            else
            {
                ViewModel.IsPane1Loading = false;
            }

            ViewModel.Refresh();
        }
    }

    private async void OnMenuCompressClick(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<FileItemViewModel> selectedItems = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItems : ViewModel.SelectedItems;
        if (selectedItems.Count == 0 && (ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem) is FileItemViewModel single)
        {
            selectedItems = [single];
        }

        if (selectedItems.Count == 0)
        {
            return;
        }

        List<string> paths = selectedItems.Select(i => i.FullPath).ToList();
        string currentDir = ViewModel.ActivePane == 2 ? ViewModel.Pane2Path : ViewModel.CurrentPath;

        CompressDialog dialog = new(paths, currentDir);
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
            if (ViewModel.ActivePane == 2)
            {
                ViewModel.NavigatePane2To(mountPath);
            }
            else
            {
                ViewModel.NavigateTo(mountPath);
            }
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

        return SidebarListBox.SelectedItem as PlaceItem;
    }

    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.Key == Key.Left)
            {
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                GetActiveListBox().Focus();
                ViewModel.GoBack();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right)
            {
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                GetActiveListBox().Focus();
                ViewModel.GoForward();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                GetActiveListBox().Focus();
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
                    IList<FileItemViewModel> items = ViewModel.ActivePane == 2 ? ViewModel.Pane2Items : ViewModel.Items;
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

        IList<FileItemViewModel> items = ViewModel.ActivePane == 2 ? ViewModel.Pane2Items : ViewModel.Items;
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
            FileItemViewModel? current = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
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
        ListBox activeList = GetActiveListBox();
        IList<FileItemViewModel> items = ViewModel.ActivePane == 2 ? ViewModel.Pane2Items : ViewModel.Items;
        int index = items.IndexOf(match);

        activeList.Selection.Clear();
        if (index >= 0)
        {
            activeList.Selection.Select(index);
        }

        activeList.SelectedItem = match;
        activeList.ScrollIntoView(match);

        if (ViewModel.ActivePane == 2)
        {
            ViewModel.Pane2SelectedItem = match;
        }
        else
        {
            ViewModel.SelectedItem = match;
        }

        UpdateSelectionState();

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

        ListBox activeList = (ListBox) sender!;
        ViewModel.ActivePane = (activeList == Pane2GridFilesListBox || activeList == Pane2ListFilesListBox) ? 2 : 1;
        (Canvas canvas, Border rect) = GetActiveRubberBand();
        PointerPoint point = e.GetCurrentPoint(activeList);
        ListBoxItem? clickedItem = FindVisualParent<ListBoxItem>((Visual?) e.Source);
        
        if (point.Properties.IsRightButtonPressed)
        {
            if (clickedItem?.DataContext is FileItemViewModel fileItem)
            {
                if (activeList.SelectedItems != null && !activeList.SelectedItems.Contains(fileItem))
                {
                    activeList.SelectedItems.Clear();
                    activeList.SelectedItem = fileItem;
                }
            }
            else
            {
                activeList.SelectedItems?.Clear();
                if (ViewModel.ActivePane == 2)
                {
                    ViewModel.Pane2SelectedItem = null;
                }
                else
                {
                    ViewModel.SelectedItem = null;
                }
            }

            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            if (clickedItem?.DataContext is FileItemViewModel fileItem)
            {
                if (activeList.SelectedItems != null && activeList.SelectedItems.Contains(fileItem) &&
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
                    activeList.SelectedItems?.Clear();
                    activeList.SelectedItem = fileItem;
                }
            }
            else
            {
                activeList.Focus();
                _clickedAlreadySelectedItem = null;
                if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    activeList.SelectedItems?.Clear();
                    if (ViewModel.ActivePane == 2)
                    {
                        ViewModel.Pane2SelectedItem = null;
                    }
                    else
                    {
                        ViewModel.SelectedItem = null;
                    }
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
        (Canvas canvas, Border rect) = GetActiveRubberBand();

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
                    if (activeList.SelectedItems != null && !activeList.SelectedItems.Contains(fileItem))
                    {
                        activeList.SelectedItems.Add(fileItem);
                    }
                }
                else
                {
                    if (activeList.SelectedItems != null && activeList.SelectedItems.Contains(fileItem))
                    {
                        activeList.SelectedItems.Remove(fileItem);
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
        (Canvas canvas, Border rect) = GetActiveRubberBand();

        if (_isRubberBanding)
        {
            _isRubberBanding = false;
            rect.IsVisible = false;
            e.Pointer.Capture(null);
            return;
        }

        if (_clickedAlreadySelectedItem != null)
        {
            ListBox activeList = (ListBox) sender!;
            activeList.SelectedItems?.Clear();
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
        if (hoverFile?.DataContext is FileItemViewModel targetFolder && targetFolder.IsDirectory)
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
                    if (hoverFile?.DataContext is FileItemViewModel targetFolder && targetFolder.IsDirectory && !IsInvalidDropDestination(targetFolder.FullPath, paths))
                    {
                        targetDir = targetFolder.FullPath;
                    }
                    else
                    {
                        ListBox? hoverList = FindVisualParent<ListBox>(hit);
                        if (hoverList == Pane2GridFilesListBox || hoverList == Pane2ListFilesListBox)
                        {
                            targetDir = ViewModel.Pane2Path;
                        }
                        else if (hoverList == GridFilesListBox || hoverList == ListFilesListBox)
                        {
                            targetDir = ViewModel.Pane1Path;
                        }
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
            _hoveredBreadcrumbButton.Background = Brushes.Transparent;
            _hoveredBreadcrumbButton = null;
            _hoveredBreadcrumbPath = null;
        }

        foreach (PlaceItem p in ViewModel.Places)
        {
            p.IsDropTarget = false;
        }

        foreach (FileItemViewModel f in ViewModel.Items)
        {
            f.IsDropTarget = false;
        }

        foreach (FileItemViewModel f in ViewModel.Pane2Items)
        {
            f.IsDropTarget = false;
        }
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

    private void OnFileDoubleTapped(object? sender, TappedEventArgs e)
    {
        ViewModel.ActivePane = 1;
        ViewModel.OpenEntry(ViewModel.SelectedItem);
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

        if (e.Key == Key.Enter)
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            IInputElement? focusedElement = topLevel?.FocusManager?.GetFocusedElement();
            if (focusedElement is not TextBox)
            {
                FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
                activeSelected ??= GetSelectedItems().FirstOrDefault();
                if (activeSelected != null)
                {
                    ViewModel.OpenEntry(activeSelected);
                    e.Handled = true;
                    return;
                }
            }
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            if (e.Key == Key.Left)
            {
                ViewModel.GoBack();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Right)
            {
                ViewModel.GoForward();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                ViewModel.GoUp();
                e.Handled = true;
                return;
            }
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
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

        if (e.Key == Key.F5)
        {
            OnRefreshClick(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            FileItemViewModel? selected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
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

    private void ClearActiveSelection()
    {
        ListBox activeList = GetActiveListBox();
        activeList.Selection.Clear();
        activeList.SelectedItems?.Clear();
        activeList.SelectedItem = null;
        ViewModel.ClearSelection();
    }

    private async void OnFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearActiveSelection();
            e.Handled = true;
            return;
        }

        FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
        activeSelected ??= GetSelectedItems().FirstOrDefault();

        if (e.Key == Key.Enter)
        {
            ViewModel.OpenEntry(activeSelected);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            if (ViewModel.ActivePane == 2)
            {
                ViewModel.GoUpPane2();
            }
            else
            {
                ViewModel.GoUp();
            }

            e.Handled = true;
            return;
        }
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        ListBox activeList = GetActiveListBox();
        activeList.SelectAll();
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
        InputDialog dialog = new("New Folder", "Folder name:", "New Folder");
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
        InputDialog dialog = new("New File", "File name:", "New File.txt");
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

    private void OnMenuOpenClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
        ViewModel.OpenEntry(activeSelected);
    }

    private async void OnMenuOpenWithClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
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
        string targetDir = ViewModel.ActivePane == 2 ? ViewModel.Pane2Path : ViewModel.CurrentPath;
        FileItemViewModel? selected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
        if (selected != null && selected.IsDirectory)
        {
            targetDir = selected.FullPath;
        }

        ViewModel.OpenAsRoot(targetDir);
    }

    private void OnMenuOpenTerminalClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
        string currentDir = ViewModel.ActivePane == 2 ? ViewModel.Pane2Path : ViewModel.CurrentPath;
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
        FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
        if (activeSelected is null)
        {
            return;
        }

        InputDialog dialog = new("Rename", "New name:", activeSelected.Name);
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
            FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
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
                ? $"Are you sure you want to permanently delete \"{targets[0].Name}\"?"
                : $"Are you sure you want to permanently delete {targets.Count} items?";

            ConfirmDialog dialog = new("Permanent Delete", message, "Delete", true, true);
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
        FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
        string currentDir = ViewModel.ActivePane == 2 ? ViewModel.Pane2Path : ViewModel.CurrentPath;
        string path = activeSelected?.FullPath ?? currentDir;
        if (Directory.Exists(path))
        {
            ViewModel.ToggleBookmark(path);
        }
    }

    private async void OnMenuPropertiesClick(object? sender, RoutedEventArgs e)
    {
        FileItemViewModel? activeSelected = ViewModel.ActivePane == 2 ? ViewModel.Pane2SelectedItem : ViewModel.SelectedItem;
        if (activeSelected != null)
        {
            PropertiesDialog dialog = new(activeSelected);
            await dialog.ShowDialog(this);
            return;
        }

        string currentDir = ViewModel.ActivePane == 2 ? ViewModel.Pane2Path : ViewModel.CurrentPath;
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
                TypeString = "Folder",
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
        InputDialog dialog = new("Keyboard Shortcuts", "F3: Dual Pane\nCtrl+F: Search\nCtrl+L: Location\nCtrl+Shift+N: New Folder\nCtrl+Alt+N: New File\nCtrl+H: Hidden files\nF5 / Ctrl+R: Refresh\nAlt+Enter: Properties\nDelete: Trash\nShift+Delete: Permanent delete", "");
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
