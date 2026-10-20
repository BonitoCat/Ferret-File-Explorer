using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Models;
using FileExplorer.Core.Services;
using FileExplorer.Core.States;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Models;
using FileExplorer.Gui.Services;
using LoggerLib;

namespace FileExplorer.Gui.ViewModels;

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly FileSystemService _fileService = new();
    private readonly FileOperationService _operationService = new();
    private readonly FileExecutionService _executionService = new();
    private readonly BookmarkService _bookmarkService = new();
    private readonly NetworkService _networkService = new();
    private readonly NetworkBookmarkService _networkBookmarkService = new();
    private readonly string _associationsPath;

    private string _searchQuery = "";
    private bool _showHiddenFiles = true;
    private string _statusText = "";
    private bool _isGridView = true;
    private bool _isDualPane;
    private double _colNameWidth = 260;
    private double _colSizeWidth = 110;
    private double _colTypeWidth = 110;
    private double _colModifiedWidth = 140;
    public event Action<string>? ScriptOpenRequested;
    public event Action<NetworkConnectionInfo>? NetworkLoginRequested;
    public event Action<PaneViewModel, string, bool>? NetworkServerLoginRequested;
    public event Action<string>? AccessDenied;
    public event Action<string>? DirectoryAccessDenied;
    public event Action<string, IReadOnlyList<string>>? TransferCompleted;
    public Func<TransferConflict, Task<ConflictResolution>>? ConflictResolver { get; set; }
    private bool _isPathEditing;
    private PaneViewModel _activePane;
    private OperationProgressViewModel? _selectedOperation;
    private int _externalLoadingCount;
    private int _addonErrorCount;


    public bool IsGridView
    {
        get => _isGridView;
        set
        {
            if (SetProperty(ref _isGridView, value))
            {
                OnPropertyChanged(nameof(IsListView));
            }
        }
    }

    public bool IsListView
    {
        get => !_isGridView;
        set
        {
            if (value != !_isGridView)
            {
                IsGridView = !value;
            }
        }
    }

    public double ColNameWidth
    {
        get => _colNameWidth;
        set => SetProperty(ref _colNameWidth, value);
    }

    public double ColSizeWidth
    {
        get => _colSizeWidth;
        set => SetProperty(ref _colSizeWidth, value);
    }

    public double ColTypeWidth
    {
        get => _colTypeWidth;
        set => SetProperty(ref _colTypeWidth, value);
    }

    public double ColModifiedWidth
    {
        get => _colModifiedWidth;
        set => SetProperty(ref _colModifiedWidth, value);
    }

    public ObservableCollection<OperationProgressViewModel> Operations { get; } = new();

    public ObservableCollection<AddonStatusItemViewModel> AddonStatusItems { get; } = new();

    public OperationProgressViewModel? SelectedOperation
    {
        get => _selectedOperation;
        set
        {
            if (!SetProperty(ref _selectedOperation, value))
            {
                return;
            }

            foreach (OperationProgressViewModel operation in Operations)
            {
                operation.IsSelected = operation == value;
            }
        }
    }

    public bool IsOperationInProgress => Operations.Count > 0;

    public bool HasMultipleOperations => Operations.Count > 1;

    public string OperationPosition => SelectedOperation == null ? "" : $"{Operations.IndexOf(SelectedOperation) + 1} / {Operations.Count}";

    public int AddonErrorCount
    {
        get => _addonErrorCount;
        set
        {
            if (SetProperty(ref _addonErrorCount, value))
            {
                OnPropertyChanged(nameof(HasAddonErrors));
            }
        }
    }

    public bool HasAddonErrors => AddonErrorCount > 0;

    public void AddOperation(OperationProgressViewModel operation)
    {
        Operations.Add(operation);
        SelectedOperation = operation;
        NotifyOperationsChanged();
    }

    public void RemoveOperation(OperationProgressViewModel operation)
    {
        int index = Operations.IndexOf(operation);
        if (index < 0)
        {
            return;
        }

        Operations.RemoveAt(index);
        if (SelectedOperation == operation)
        {
            SelectedOperation = Operations.Count == 0 ? null : Operations[Math.Min(index, Operations.Count - 1)];
        }

        NotifyOperationsChanged();
    }

    public void CycleOperation(int offset)
    {
        if (Operations.Count == 0)
        {
            return;
        }

        int index = SelectedOperation == null ? 0 : Operations.IndexOf(SelectedOperation);
        SelectedOperation = Operations[((index + offset) % Operations.Count + Operations.Count) % Operations.Count];
        OnPropertyChanged(nameof(OperationPosition));
    }

    public void SelectOperation(OperationProgressViewModel operation)
    {
        if (!Operations.Contains(operation))
        {
            return;
        }

        SelectedOperation = operation;
        OnPropertyChanged(nameof(OperationPosition));
    }

    public void SetExternalLoading(bool loading)
    {
        _externalLoadingCount = Math.Max(0, _externalLoadingCount + (loading ? 1 : -1));
        foreach (PaneViewModel pane in Panes)
        {
            pane.IsExternalLoading = _externalLoadingCount > 0;
        }
    }

    private void NotifyOperationsChanged()
    {
        OnPropertyChanged(nameof(IsOperationInProgress));
        OnPropertyChanged(nameof(HasMultipleOperations));
        OnPropertyChanged(nameof(OperationPosition));
    }

    public PaneViewModel Pane1 { get; }
    public PaneViewModel Pane2 { get; }
    public IEnumerable<PaneViewModel> Panes => [Pane1, Pane2];

    private static readonly TimeSpan TransferSettleTimeout = TimeSpan.FromMinutes(10);
    public IEnumerable<FileItemViewModel> AllItems => Pane1.Items.Concat(Pane2.Items);

    public PaneViewModel ActivePane
    {
        get => _activePane;
        set
        {
            if (_activePane == value)
            {
                return;
            }

            _activePane.IsActive = false;
            _activePane = value;
            _activePane.IsActive = true;
            OnPropertyChanged(nameof(ActivePane));
            OnActivePathChanged();

            if (!string.IsNullOrEmpty(SearchQuery))
            {
                value.Reload(SearchQuery);
            }
        }
    }

    public bool IsDualPane
    {
        get => _isDualPane;
        set
        {
            if (_isDualPane == value)
            {
                return;
            }

            if (!value)
            {
                ActivePane = Pane1;
                Pane2.Close();
            }

            _isDualPane = value;
            OnPropertyChanged(nameof(IsDualPane));
            OnPropertyChanged(nameof(SecondaryPaneMinWidth));

            if (value && !Pane2.IsOpen)
            {
                Pane2.NavigateTo(Pane1.CurrentPath);
            }
        }
    }

    public double SecondaryPaneMinWidth => IsDualPane ? 200 : 0;

    public bool IsPathEditing
    {
        get => _isPathEditing;
        set => SetProperty(ref _isPathEditing, value);
    }

    public static bool IsArchiveFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".zip" || ext == ".7z" || ext == ".tar" || ext == ".gz" || ext == ".xz" || ext == ".bz2" || ext == ".zst" || ext == ".rar" || ext == ".deb" || ext == ".tgz";
    }

    public void ResetPlacesOrder()
    {
        _sidebarOrder.ResetLayout();
        _sectionOrder = new(DefaultSectionOrder);
        LoadPlaces();
    }

    public ObservableCollection<PlaceItem> Places { get; } = new();
    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();

    public string CurrentPath => ActivePane.CurrentPath;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ActivePane.Reload(value);
            }
        }
    }

    public bool ShowHiddenFiles
    {
        get => _showHiddenFiles;
        set
        {
            if (SetProperty(ref _showHiddenFiles, value))
            {
                Logger.LogI($"Toggled hidden files: {value}");
                ReloadPanes();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool CanGoBack => ActivePane.CanGoBack;
    public bool CanGoForward => ActivePane.CanGoForward;
    public bool CanGoUp => ActivePane.CanGoUp;

    public FilePickerOptions? PickerOptions { get; }

    public bool IsPickerMode => PickerOptions != null && PickerOptions.Mode != PickerMode.None;
    public bool IsPrivilegedProcess => Environment.IsPrivilegedProcess || Environment.UserName == "root";

    public PickerMode PickerMode => PickerOptions?.Mode ?? PickerMode.None;

    public string PickerTargetLabel => PickerMode switch
    {
        PickerMode.Folder => LocalizationService.Get("PickerFolderLabel"),
        PickerMode.Save => LocalizationService.Get("PickerNameLabel"),
        _ => LocalizationService.Get("PickerFileLabel"),
    };

    public string PickerActionButtonText => PickerOptions?.ActionButtonText ?? PickerMode switch
    {
        PickerMode.Folder => LocalizationService.Get("SelectFolder"),
        PickerMode.Save => LocalizationService.Get("Save"),
        _ => LocalizationService.Get("Open"),
    };

    public IReadOnlyList<PickerFilter> PickerFilters => PickerOptions?.Filters ?? [];

    public bool HasPickerFilters => PickerFilters.Count > 0;

    private PickerFilter? _selectedPickerFilter;
    private List<Regex> _pickerFilterPatterns = new();

    public PickerFilter? SelectedPickerFilter
    {
        get => _selectedPickerFilter;
        set
        {
            if (!SetProperty(ref _selectedPickerFilter, value))
            {
                return;
            }

            _pickerFilterPatterns = value == null ? new() : value.Patterns.Select(GlobToRegex).ToList();
            ReloadPanes();
        }
    }

    public Func<string, Task<bool>>? OverwriteConfirmer { get; set; }

    private string _pickerSelectedName = string.Empty;

    public string PickerSelectedName
    {
        get => _pickerSelectedName;
        set => SetProperty(ref _pickerSelectedName, value);
    }

    public event Action<FilePickerResult>? PickerCompleted;

    public MainWindowViewModel() : this(null, null)
    {
    }

    public MainWindowViewModel(string? initialPath) : this(initialPath, null)
    {
    }

    public MainWindowViewModel(string? initialPath, FilePickerOptions? pickerOptions)
    {
        PickerOptions = pickerOptions;
        _selectedPickerFilter = pickerOptions?.Filters.FirstOrDefault();
        _pickerFilterPatterns = _selectedPickerFilter?.Patterns.Select(GlobToRegex).ToList() ?? new();
        _pickerSelectedName = pickerOptions?.InitialName ?? "";

        Pane1 = CreatePane();
        Pane2 = CreatePane();
        _activePane = Pane1;
        Pane1.IsActive = true;

        string configDir = AppPaths.DataDir;
        _associationsPath = Path.Combine(configDir, "associations.json");
        LoadSectionOrder();
        Task.Run(() =>
        {
            try
            {
                AppPaths.EnsureDir(configDir);
                MigrateLegacyAssociations();
            }
            catch
            {
            }
        });

        string startPath = LocationResolver.Resolve(initialPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        string? pendingNetworkPath = null;
        if (IsNetworkUri(startPath) && !NetworkBrowseService.IsBrowseLocation(startPath))
        {
            pendingNetworkPath = startPath;
            startPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrEmpty(startPath) || !PaneViewModel.IsNavigable(startPath))
        {
            startPath = "/";
        }

        _executionService.OnOpenTerminalForExecutable = path => RunInTerminal(path, [path]);

        LoadPlaces();
        StartDeviceMonitoring();
        Pane1.NavigateTo(startPath);

        if (pendingNetworkPath != null)
        {
            NavigateTo(pendingNetworkPath);
        }
    }

    private PaneViewModel CreatePane()
    {
        PaneViewModel pane = new(_fileService, () => ShowHiddenFiles);
        if (PickerOptions?.Filters.Count > 0)
        {
            pane.FileFilter = MatchesPickerFilter;
        }

        pane.PropertyChanged += OnPanePropertyChanged;
        pane.ContentChanged += OnPaneContentChanged;
        pane.SelectionChanged += OnPaneContentChanged;
        pane.LoadFailed += OnPaneLoadFailed;
        pane.LoginRequired += OnPaneLoginRequired;
        return pane;
    }

    private void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender != ActivePane)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(PaneViewModel.CurrentPath):
                OnActivePathChanged();
                break;

            case nameof(PaneViewModel.IsLoading):
                UpdateStatus();
                break;

            case nameof(PaneViewModel.SelectedItem):
                OnActiveSelectedItemChanged();
                break;
        }
    }

    private void OnPaneLoginRequired(PaneViewModel pane, string path, bool rejected)
    {
        NetworkServerLoginRequested?.Invoke(pane, path, rejected);
    }

    private void OnPaneLoadFailed(PaneViewModel pane, string error)
    {
        if (pane != ActivePane)
        {
            return;
        }

        StatusText = LocalizationService.Format("NetworkBrowseFailed", error);
    }

    private void OnPaneContentChanged(PaneViewModel pane)
    {
        if (pane == ActivePane)
        {
            UpdateStatus();
        }
    }

    private void OnActivePathChanged()
    {
        UpdateActiveBreadcrumbs();
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        UpdateStatus();
        GuiInstanceRegistry.Update(Environment.ProcessId, CurrentPath);
    }

    private void OnActiveSelectedItemChanged()
    {
        FileItemViewModel? selected = ActivePane.SelectedItem;

        if (!IsPickerMode || selected == null)
        {
            return;
        }

        if (((PickerMode is PickerMode.File or PickerMode.Save) && !selected.IsDirectory) || (PickerMode == PickerMode.Folder && selected.IsDirectory))
        {
            PickerSelectedName = selected.Name;
        }
    }

    public void UpdateActiveBreadcrumbs()
    {
        string current = CurrentPath;
        string? trail = ActivePane.BreadcrumbTrail;
        if (trail == null || !IsSameOrAncestor(current, trail) || !Directory.Exists(trail))
        {
            trail = current;
        }

        List<BreadcrumbItem> crumbs = BuildBreadcrumbs(trail);
        if (!crumbs.Any(c => IsSamePath(c.FullPath, current)))
        {
            trail = current;
            crumbs = BuildBreadcrumbs(trail);
        }

        foreach (BreadcrumbItem crumb in crumbs)
        {
            crumb.IsCurrent = IsSamePath(crumb.FullPath, current);
        }

        ActivePane.BreadcrumbTrail = trail;

        Breadcrumbs.Clear();
        foreach (BreadcrumbItem crumb in crumbs)
        {
            Breadcrumbs.Add(crumb);
        }
    }

    private void ResetActiveBreadcrumbTrail()
    {
        ActivePane.BreadcrumbTrail = null;
    }

    private static bool IsSamePath(string a, string b)
    {
        return string.Equals(NormalizeTrailPath(a), NormalizeTrailPath(b), StringComparison.Ordinal);
    }

    private static bool IsSameOrAncestor(string ancestor, string path)
    {
        string normAncestor = NormalizeTrailPath(ancestor);
        string normPath = NormalizeTrailPath(path);
        if (normAncestor == normPath)
        {
            return true;
        }

        string prefix = normAncestor == "/" ? "/" : normAncestor + "/";
        return normPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string NormalizeTrailPath(string path)
    {
        string trimmed = path.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" : trimmed;
    }

    public void SetGridView(bool grid)
    {
        IsGridView = grid;
        Logger.LogI($"Set grid view: {grid}");
    }

    public void ToggleDualPane()
    {
        if (IsDualPane && ActivePane == Pane2 && Pane2.IsOpen)
        {
            Pane1.TakeOver(Pane2);
        }

        IsDualPane = !IsDualPane;
        Logger.LogI($"Toggled dual pane: {IsDualPane}");
    }

    public void CloseActivePane()
    {
        if (!IsDualPane)
        {
            return;
        }

        if (ActivePane == Pane2 || !Pane2.IsOpen)
        {
            IsDualPane = false;
            Logger.LogI("Closed secondary pane");
            return;
        }

        Pane1.TakeOver(Pane2);
        IsDualPane = false;

        Logger.LogI("Closed primary pane, promoted secondary pane");
    }

    public void TogglePathEditing()
    {
        IsPathEditing = !IsPathEditing;
    }

    private readonly HashSet<PlaceCategory> _collapsedCategories = new();
    private static readonly PlaceCategory[] DefaultSectionOrder = [PlaceCategory.Places, PlaceCategory.Bookmarks, PlaceCategory.Devices, PlaceCategory.Network, PlaceCategory.Addons];
    private readonly HashSet<string> _collapsedAddonSections = new(StringComparer.Ordinal);
    private readonly SidebarOrderService _sidebarOrder = new();
    private List<PlaceCategory> _sectionOrder = new(DefaultSectionOrder);

    private void LoadSectionOrder()
    {
        _sectionOrder = _sidebarOrder.GetSectionOrder()
            .Select(k => (PlaceCategory) k)
            .ToList();
    }

    private void SaveSectionOrder()
    {
        _sidebarOrder.SetSectionOrder(_sectionOrder.Select(c => (PlaceKind) c));
    }

    private void SaveItemOrder(PlaceCategory category)
    {
        _sidebarOrder.SetItemOrder((PlaceKind) category, Places
            .Where(p => p.Category == category && !p.IsHeader)
            .Select(GetPlaceKey));
    }

    private static string GetPlaceKey(PlaceItem place)
    {
        return SidebarOrderService.GetKey(place.Path, place.RemoteUri, place.DevicePath);
    }

    public void MoveSection(PlaceCategory source, PlaceCategory target, bool insertAbove)
    {
        if (source == target)
        {
            return;
        }

        _sectionOrder.Remove(source);
        int targetIndex = _sectionOrder.IndexOf(target);
        _sectionOrder.Insert(insertAbove ? targetIndex : targetIndex + 1, source);

        SaveSectionOrder();
        ApplySectionOrder();
        Logger.LogI($"Moved sidebar section {source} {(insertAbove ? "above" : "below")} {target}");
    }

    public void SetAddonPlaces(IReadOnlyList<AddonPlaceEntry> entries)
    {
        List<PlaceItem> items = new();
        int index = 0;
        foreach (IGrouping<string, AddonPlaceEntry> section in entries.GroupBy(e => e.SectionKey))
        {
            bool isCollapsed = _collapsedAddonSections.Contains(section.Key);
            items.Add(new PlaceItem
            {
                Name = section.First().Section,
                Path = "",
                IsHeader = true,
                Category = PlaceCategory.Addons,
                SectionKey = section.Key,
                SortIndex = index++,
                IsCollapsed = isCollapsed,
            });

            foreach (AddonPlaceEntry entry in section)
            {
                items.Add(new PlaceItem
                {
                    Name = entry.Name,
                    Path = entry.Path,
                    Icon = entry.Icon,
                    Category = PlaceCategory.Addons,
                    SectionKey = section.Key,
                    SortIndex = index++,
                    IsVisible = !isCollapsed,
                });
            }
        }

        SyncPlaces(Places
            .Where(p => p.Category != PlaceCategory.Addons)
            .Concat(items)
            .ToList());
    }

    public void ToggleAddonSectionCollapse(string sectionKey)
    {
        if (!_collapsedAddonSections.Remove(sectionKey))
        {
            _collapsedAddonSections.Add(sectionKey);
        }

        bool isCollapsed = _collapsedAddonSections.Contains(sectionKey);
        foreach (PlaceItem item in Places.Where(p => p.Category == PlaceCategory.Addons && p.SectionKey == sectionKey))
        {
            if (item.IsHeader)
            {
                item.IsCollapsed = isCollapsed;
            }
            else
            {
                item.IsVisible = !isCollapsed;
            }
        }
    }

    private int GetPlaceSortIndex(PlaceItem place)
    {
        if (place.Category == PlaceCategory.Addons)
        {
            return place.SortIndex;
        }

        return place.IsHeader ? -1 : _sidebarOrder.GetItemIndex((PlaceKind) place.Category, GetPlaceKey(place));
    }

    private void SyncPlaces(List<PlaceItem> target)
    {
        foreach (IGrouping<PlaceCategory, PlaceItem> group in target.Where(p => !p.IsHeader && p.Category != PlaceCategory.Addons).GroupBy(p => p.Category))
        {
            _sidebarOrder.AppendMissing((PlaceKind) group.Key, group.Select(GetPlaceKey));
        }

        List<PlaceItem> resolved = target
            .OrderBy(p => _sectionOrder.IndexOf(p.Category))
            .ThenBy(GetPlaceSortIndex)
            .Select(t => Places.FirstOrDefault(p => IsSamePlace(p, t)) ?? t)
            .ToList();

        for (int i = Places.Count - 1; i >= 0; i--)
        {
            if (!resolved.Contains(Places[i]))
            {
                Places.RemoveAt(i);
            }
        }

        for (int i = 0; i < resolved.Count; i++)
        {
            int current = Places.IndexOf(resolved[i]);
            if (current < 0)
            {
                Places.Insert(i, resolved[i]);
            }
            else if (current != i)
            {
                Places.Move(current, i);
            }
        }
    }

    private static bool IsSamePlace(PlaceItem a, PlaceItem b)
    {
        return a.Category == b.Category
            && a.SectionKey == b.SectionKey
            && a.IsHeader == b.IsHeader
            && a.Path == b.Path
            && a.Name == b.Name
            && a.IsMounted == b.IsMounted
            && a.IsNetworkBookmark == b.IsNetworkBookmark
            && a.DevicePath == b.DevicePath;
    }

    private void ApplySectionOrder()
    {
        List<PlaceItem> ordered = Places
            .OrderBy(p => _sectionOrder.IndexOf(p.Category))
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            int current = Places.IndexOf(ordered[i]);
            if (current != i)
            {
                Places.Move(current, i);
            }
        }
    }

    public void ToggleCategoryCollapse(PlaceCategory category)
    {
        if (_collapsedCategories.Contains(category))
        {
            _collapsedCategories.Remove(category);
        }
        else
        {
            _collapsedCategories.Add(category);
        }

        bool isCollapsed = _collapsedCategories.Contains(category);
        foreach (PlaceItem item in Places)
        {
            if (item.Category != category)
            {
                continue;
            }

            if (item.IsHeader)
            {
                item.IsCollapsed = isCollapsed;
            }
            else
            {
                item.IsVisible = !isCollapsed;
            }
        }
    }

    public void LoadPlaces()
    {
        _sidebarOrder.Reload();
        LoadSectionOrder();

        List<PlaceItem> placesSection = new();

        string userProfile = UserDirs.Home;
        string desktop = UserDirs.Desktop;
        string documents = UserDirs.Documents;
        string downloads = UserDirs.Downloads;
        string music = UserDirs.Music;
        string pictures = UserDirs.Pictures;
        string videos = UserDirs.Videos;

        bool placesCollapsed = _collapsedCategories.Contains(PlaceCategory.Places);
        placesSection.Add(new PlaceItem
        {
            Name = LocalizationService.Get("Places"),
            Path = "",
            IsHeader = true,
            Category = PlaceCategory.Places,
            IsCollapsed = placesCollapsed,
        });

        bool placesVisible = !placesCollapsed;

        if (Directory.Exists(userProfile))
        {
            placesSection.Add(new PlaceItem
            {
                Name = LocalizationService.Get("Home"),
                Path = userProfile,
                Icon = IconService.Get("home"),
                Category = PlaceCategory.Places,
                IsVisible = placesVisible,
            });
        }

        if (Directory.Exists(desktop))
        {
            placesSection.Add(new PlaceItem
            {
                Name = Path.GetFileName(desktop),
                Path = desktop,
                Icon = IconService.Get("desktop"),
                Category = PlaceCategory.Places,
                IsVisible = placesVisible,
            });
        }

        if (Directory.Exists(documents))
        {
            placesSection.Add(new PlaceItem
            {
                Name = Path.GetFileName(documents),
                Path = documents,
                Icon = IconService.Get("documents"),
                Category = PlaceCategory.Places,
                IsVisible = placesVisible,
            });
        }

        if (Directory.Exists(downloads))
        {
            placesSection.Add(new PlaceItem
            {
                Name = Path.GetFileName(downloads),
                Path = downloads,
                Icon = IconService.Get("downloads"),
                Category = PlaceCategory.Places,
                IsVisible = placesVisible,
            });
        }

        if (Directory.Exists(music))
        {
            placesSection.Add(new PlaceItem
            {
                Name = Path.GetFileName(music),
                Path = music,
                Icon = IconService.Get("music"),
                Category = PlaceCategory.Places,
                IsVisible = placesVisible,
            });
        }

        if (Directory.Exists(pictures))
        {
            placesSection.Add(new PlaceItem
            {
                Name = Path.GetFileName(pictures),
                Path = pictures,
                Icon = IconService.Get("pictures"),
                Category = PlaceCategory.Places,
                IsVisible = placesVisible,
            });
        }

        if (Directory.Exists(videos))
        {
            placesSection.Add(new PlaceItem
            {
                Name = Path.GetFileName(videos),
                Path = videos,
                Icon = IconService.Get("videos"),
                Category = PlaceCategory.Places,
                IsVisible = placesVisible,
            });
        }

        placesSection.Add(new PlaceItem
        {
            Name = LocalizationService.Get("Root"),
            Path = "/",
            Icon = IconService.Get("disk"),
            Category = PlaceCategory.Places,
            IsVisible = placesVisible,
        });

        string trashDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Trash", "files");
        try
        {
            Directory.CreateDirectory(trashDir);
        }
        catch { }

        placesSection.Add(new PlaceItem
        {
            Name = LocalizationService.Get("Trash"),
            Path = trashDir,
            Icon = IconService.Get("trash"),
            Category = PlaceCategory.Places,
            IsVisible = placesVisible,
        });

        SyncPlaces(placesSection
            .Concat(Places.Where(p => p.Category != PlaceCategory.Places))
            .ToList());

        LoadDevicesAndNetworkPlacesAsync();
    }

    private void LoadDevicesAndNetworkPlacesAsync()
    {
        Task.Run(() =>
        {
            List<PlaceItem> extraPlaces = new();

            bool bookmarksCollapsed = _collapsedCategories.Contains(PlaceCategory.Bookmarks);
            IEnumerable<string> bookmarks = _bookmarkService.GetBookmarks();
            List<string> bookmarkList = bookmarks.ToList();

            if (bookmarkList.Count > 0)
            {
                extraPlaces.Add(new PlaceItem
                {
                    Name = LocalizationService.Get("Bookmarks"),
                    Path = "",
                    IsHeader = true,
                    Category = PlaceCategory.Bookmarks,
                    IsCollapsed = bookmarksCollapsed,
                });

                foreach (string bm in bookmarkList)
                {
                    string resolvedPath = bm;
                    try
                    {
                        DirectoryInfo dir = new(bm);
                        FileSystemInfo? target = dir.ResolveLinkTarget(true);
                        if (target != null)
                        {
                            resolvedPath = target.FullName;
                        }
                    }
                    catch { }

                    extraPlaces.Add(new PlaceItem
                    {
                        Name = resolvedPath == "/" ? LocalizationService.Get("Root") : BookmarkService.GetDisplayName(resolvedPath),
                        Path = resolvedPath,
                        Icon = IconService.Get("bookmark"),
                        IsBookmark = true,
                        Category = PlaceCategory.Bookmarks,
                        IsVisible = !bookmarksCollapsed,
                    });
                }
            }

            bool devicesCollapsed = _collapsedCategories.Contains(PlaceCategory.Devices);
            extraPlaces.Add(new PlaceItem
            {
                Name = LocalizationService.Get("Devices"),
                Path = "",
                IsHeader = true,
                Category = PlaceCategory.Devices,
                IsCollapsed = devicesCollapsed,
            });

            HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);

            if (OperatingSystem.IsLinux())
            {
                try
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = "lsblk",
                        Arguments = "-J -b -o NAME,PATH,LABEL,PARTLABEL,MOUNTPOINT,FSTYPE,SIZE,MODEL,RM,HOTPLUG,TYPE",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    using Process process = new() { StartInfo = psi };
                    process.Start();
                    string json = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(json))
                    {
                        using JsonDocument doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("blockdevices", out JsonElement bdevs) && bdevs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement dev in bdevs.EnumerateArray())
                            {
                                ParseBlockDevices(dev, extraPlaces, seenPaths, !devicesCollapsed);
                            }
                        }
                    }
                }
                catch { }
            }

            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo drive in drives)
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    string mount = drive.RootDirectory.FullName;
                    if (seenPaths.Contains(mount) || mount == "/" || mount == "/boot" || mount.StartsWith("/boot/"))
                    {
                        continue;
                    }

                    if (mount.StartsWith("/snap") || mount.StartsWith("/var/lib/snapd") || mount.Contains("/snap/"))
                    {
                        continue;
                    }

                    if (!mount.StartsWith("/media") && !mount.StartsWith("/run/media") && !mount.StartsWith("/mnt"))
                    {
                        continue;
                    }

                    seenPaths.Add(mount);

                    string name = drive.VolumeLabel;
                    if (string.IsNullOrWhiteSpace(name) || name.Contains('/'))
                    {
                        name = Path.GetFileName(mount.TrimEnd('/'));
                    }

                    if (string.IsNullOrWhiteSpace(name) || name.Contains('/'))
                    {
                        name = Path.GetFileName(drive.Name.TrimEnd('/'));
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = LocalizationService.Get("Drive");
                    }

                    extraPlaces.Add(new()
                    {
                        Name = name,
                        Path = mount,
                        Icon = IconService.Get("disk"),
                        Category = PlaceCategory.Devices,
                        IsMounted = true,
                        IsVisible = !devicesCollapsed,
                    });
                }
            }
            catch { }

            List<NetworkBookmark> savedBookmarks = _networkBookmarkService.GetBookmarks();
            List<PlaceItem> networkPlaces = new();
            HashSet<string> matchedBookmarkUris = new(StringComparer.OrdinalIgnoreCase);

            try
            {
                string gvfsDir = $"/run/user/{GetUserId()}/gvfs";
                if (Directory.Exists(gvfsDir))
                {
                    string[] gvfsMounts = Directory.GetDirectories(gvfsDir);
                    foreach (string gvfsMount in gvfsMounts)
                    {
                        string dirName = Path.GetFileName(gvfsMount);
                        string displayName = GetGvfsFastName(gvfsMount);
                        PlaceItem placeItem;

                        if (dirName.StartsWith("mtp:", StringComparison.OrdinalIgnoreCase) ||
                            dirName.StartsWith("afc:", StringComparison.OrdinalIgnoreCase))
                        {
                            placeItem = new()
                            {
                                Name = displayName,
                                Path = gvfsMount,
                                Icon = IconService.Get("phone") ?? IconService.Get("disk"),
                                Category = PlaceCategory.Devices,
                                IsVisible = !devicesCollapsed,
                            };
                            extraPlaces.Add(placeItem);
                        }
                        else if (dirName.StartsWith("gphoto2:", StringComparison.OrdinalIgnoreCase))
                        {
                            placeItem = new()
                            {
                                Name = displayName,
                                Path = gvfsMount,
                                Icon = IconService.Get("camera") ?? IconService.Get("disk"),
                                Category = PlaceCategory.Devices,
                                IsVisible = !devicesCollapsed,
                            };
                            extraPlaces.Add(placeItem);
                        }
                        else if (dirName.StartsWith("cdda:", StringComparison.OrdinalIgnoreCase))
                        {
                            placeItem = new()
                            {
                                Name = displayName,
                                Path = gvfsMount,
                                Icon = IconService.Get("disk"),
                                Category = PlaceCategory.Devices,
                                IsVisible = !devicesCollapsed,
                            };
                            extraPlaces.Add(placeItem);
                        }
                        else
                        {
                            NetworkBookmark? matchedBookmark = savedBookmarks.FirstOrDefault(b =>
                            {
                                try
                                {
                                    Uri u = new(b.Uri);
                                    return dirName.Contains(u.Host, StringComparison.OrdinalIgnoreCase);
                                }
                                catch
                                {
                                    return false;
                                }
                            });

                            if (matchedBookmark != null)
                            {
                                matchedBookmarkUris.Add(matchedBookmark.Uri);
                            }

                            placeItem = new()
                            {
                                Name = matchedBookmark?.Name ?? displayName,
                                Path = gvfsMount,
                                RemoteUri = matchedBookmark?.Uri,
                                Icon = IconService.Get("network"),
                                Category = PlaceCategory.Network,
                                IsNetworkBookmark = matchedBookmark != null,
                                IsMounted = true,
                            };
                            networkPlaces.Add(placeItem);
                        }

                        if (placeItem.IsNetworkBookmark)
                        {
                            continue;
                        }

                        string mountToResolve = gvfsMount;
                        PlaceItem itemToUpdate = placeItem;
                        Task.Run(() => ResolveGvfsDisplayNameAsync(mountToResolve, itemToUpdate));
                    }
                }
            }
            catch { }

            bool networkCollapsed = _collapsedCategories.Contains(PlaceCategory.Network);
            foreach (NetworkBookmark bm in savedBookmarks)
            {
                if (matchedBookmarkUris.Contains(bm.Uri))
                {
                    continue;
                }

                networkPlaces.Add(new PlaceItem
                {
                    Name = bm.Name,
                    Path = bm.Uri,
                    RemoteUri = bm.Uri,
                    Icon = IconService.Get("network"),
                    Category = PlaceCategory.Network,
                    IsNetworkBookmark = true,
                    IsMounted = false,
                    IsVisible = !networkCollapsed,
                });
            }

            if (GioFileLister.IsAvailable)
            {
                networkPlaces.Add(new PlaceItem
                {
                    Name = LocalizationService.Get("NetworkBrowse"),
                    Path = NetworkBrowseService.NetworkRoot,
                    Icon = IconService.Get("network"),
                    Category = PlaceCategory.Network,
                    IsMounted = true,
                    IsVisible = !networkCollapsed,
                });
            }

            extraPlaces.Add(new PlaceItem
            {
                Name = LocalizationService.Get("Network"),
                Path = "",
                IsHeader = true,
                Category = PlaceCategory.Network,
                IsCollapsed = networkCollapsed,
            });

            foreach (PlaceItem netPlace in networkPlaces)
            {
                netPlace.IsVisible = !networkCollapsed;
                extraPlaces.Add(netPlace);
            }

            Dispatcher.UIThread.Post(() =>
            {
                SyncPlaces(Places
                    .Where(p => p.Category is PlaceCategory.Places or PlaceCategory.Addons)
                    .Concat(extraPlaces)
                    .ToList());
                UpdateActiveBreadcrumbs();
            });
        });
    }

    private CancellationTokenSource? _reloadPlacesCts;
    private readonly List<IDisposable> _deviceWatchers = new();
    private Process? _udisksMonitorProcess;

    private static void ParseBlockDevices(JsonElement elem, List<PlaceItem> extraPlaces, HashSet<string> seenPaths, bool isVisible)
    {
        if (elem.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                ParseBlockDevices(child, extraPlaces, seenPaths, isVisible);
            }

            return;
        }

        string? type = elem.TryGetProperty("type", out JsonElement typeElem) ? typeElem.GetString() : null;
        if (string.Equals(type, "loop", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? path = elem.TryGetProperty("path", out JsonElement pathElem) ? pathElem.GetString() : null;
        if (string.IsNullOrEmpty(path) || path.StartsWith("/dev/loop", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? fstype = elem.TryGetProperty("fstype", out JsonElement fsElem) ? fsElem.GetString() : null;
        if (string.Equals(fstype, "squashfs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? mountpoint = elem.TryGetProperty("mountpoint", out JsonElement mpElem) ? mpElem.GetString() : null;
        if (!string.IsNullOrEmpty(mountpoint) && (mountpoint.StartsWith("/snap") || mountpoint.StartsWith("/var/lib/snapd") || mountpoint.Contains("/snap/")))
        {
            return;
        }

        string? label = elem.TryGetProperty("label", out JsonElement lblElem) ? lblElem.GetString() : null;
        string? partlabel = elem.TryGetProperty("partlabel", out JsonElement plblElem) ? plblElem.GetString() : null;
        string? model = elem.TryGetProperty("model", out JsonElement mdlElem) ? mdlElem.GetString() : null;
        long size = elem.TryGetProperty("size", out JsonElement szElem) && szElem.TryGetInt64(out long s) ? s : 0;

        if (!string.IsNullOrEmpty(mountpoint))
        {
            if (mountpoint == "/" || mountpoint == "/boot" || mountpoint.StartsWith("/boot/"))
            {
                return;
            }

            if (seenPaths.Contains(mountpoint) || seenPaths.Contains(path))
            {
                return;
            }

            seenPaths.Add(mountpoint);
            seenPaths.Add(path);

            string name = !string.IsNullOrWhiteSpace(label) ? label : (!string.IsNullOrWhiteSpace(partlabel) ? partlabel : (!string.IsNullOrWhiteSpace(model) ? model : Path.GetFileName(mountpoint.TrimEnd('/'))));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = LocalizationService.Get("Drive");
            }

            extraPlaces.Add(new()
            {
                Name = name,
                Path = mountpoint,
                DevicePath = path,
                Icon = IconService.Get("disk"),
                Category = PlaceCategory.Devices,
                IsMounted = true,
                IsVisible = isVisible,
            });
            return;
        }

        if (!string.IsNullOrEmpty(fstype) && fstype != "swap" && fstype != "squashfs")
        {
            if (partlabel == "Microsoft reserved partition" || (size > 0 && size < 10 * 1024 * 1024))
            {
                return;
            }

            if (seenPaths.Contains(path))
            {
                return;
            }

            seenPaths.Add(path);

            string name = !string.IsNullOrWhiteSpace(label)
                ? label
                : (!string.IsNullOrWhiteSpace(partlabel)
                    ? partlabel
                    : (!string.IsNullOrWhiteSpace(model)
                        ? $"{model} ({FormatDeviceSize(size)})"
                        : (!string.IsNullOrEmpty(FormatDeviceSize(size)) ? $"{FormatDeviceSize(size)} Volume" : Path.GetFileName(path))));

            extraPlaces.Add(new()
            {
                Name = name,
                Path = path,
                DevicePath = path,
                Icon = IconService.Get("disk"),
                Category = PlaceCategory.Devices,
                IsMounted = false,
                IsVisible = isVisible,
            });
        }
    }

    private static string FormatDeviceSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double len = (double) bytes;
        int order = 0;
        while (len >= 1000 && order < units.Length - 1)
        {
            order++;
            len /= 1000;
        }

        return $"{len:0.#} {units[order]}";
    }

    public async Task<string?> MountDeviceAsync(string devPath)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "udisksctl",
                Arguments = $"mount -b \"{devPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process process = new() { StartInfo = psi };
            process.Start();

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string combined = stdout + " " + stderr;

            string marker1 = " at `";
            int idx1 = combined.IndexOf(marker1, StringComparison.Ordinal);
            if (idx1 >= 0)
            {
                int endIdx = combined.IndexOf('\'', idx1 + marker1.Length);
                if (endIdx > idx1)
                {
                    string mountPoint = combined.Substring(idx1 + marker1.Length, endIdx - (idx1 + marker1.Length)).Trim();
                    if (Directory.Exists(mountPoint))
                    {
                        StatusText = LocalizationService.Format("MountedAt", mountPoint);
                        return mountPoint;
                    }
                }
            }

            string marker2 = " at ";
            int idx2 = combined.IndexOf(marker2, StringComparison.Ordinal);
            if (idx2 >= 0)
            {
                string rest = combined.Substring(idx2 + marker2.Length).Trim();
                string mountPoint = rest.Split(['\r', '\n', '.'])[0].Trim().Trim('\'', '`', '"');
                if (Directory.Exists(mountPoint))
                {
                    StatusText = LocalizationService.Format("MountedAt", mountPoint);
                    return mountPoint;
                }
            }

            if (process.ExitCode != 0)
            {
                StatusText = LocalizationService.Format("MountFailedWithError", stderr.Trim());
            }
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Format("MountFailedWithError", ex.Message);
        }

        return null;
    }

    private void DebouncedReloadPlaces()
    {
        _reloadPlacesCts?.Cancel();
        CancellationTokenSource cts = new();
        _reloadPlacesCts = cts;

        Task.Delay(300, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(LoadPlaces);
            }
        });
    }

    private void StartDeviceMonitoring()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Task.Run(() =>
        {
            AddMountRootWatchers("/media");
            AddMountRootWatchers("/run/media");
            AddDeviceWatcher($"/run/user/{GetUserId()}/gvfs");
        });

        Task.Run(async () =>
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = "udisksctl",
                    Arguments = "monitor",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                _udisksMonitorProcess = new() { StartInfo = psi };
                _udisksMonitorProcess.Start();

                while (!_udisksMonitorProcess.HasExited)
                {
                    string? line = await _udisksMonitorProcess.StandardOutput.ReadLineAsync();
                    if (line is null)
                    {
                        break;
                    }

                    if (!line.Contains("Monitoring the udisks daemon"))
                    {
                        DebouncedReloadPlaces();
                    }
                }
            }
            catch { }
        });
    }

    private void AddMountRootWatchers(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        AddDeviceWatcher(root);

        try
        {
            foreach (string userDir in Directory.EnumerateDirectories(root))
            {
                AddDeviceWatcher(userDir);
            }
        }
        catch { }
    }

    private void AddDeviceWatcher(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            FileSystemWatcher watcher = new(path)
            {
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            watcher.Created += (_, _) => DebouncedReloadPlaces();
            watcher.Deleted += (_, _) => DebouncedReloadPlaces();
            watcher.Renamed += (_, _) => DebouncedReloadPlaces();
            watcher.EnableRaisingEvents = true;

            lock (_deviceWatchers)
            {
                _deviceWatchers.Add(watcher);
            }
        }
        catch { }
    }

    public void MovePlaceUp(PlaceItem item)
    {
        if (item.IsHeader)
        {
            return;
        }

        int index = Places.IndexOf(item);
        if (index <= 0)
        {
            return;
        }

        int prevIndex = -1;
        for (int i = index - 1; i >= 0; i--)
        {
            if (Places[i].Category == item.Category && !Places[i].IsHeader)
            {
                prevIndex = i;
                break;
            }
        }

        if (prevIndex >= 0)
        {
            Places.Move(index, prevIndex);
            SaveItemOrder(item.Category);
        }
    }

    public void MovePlaceDown(PlaceItem item)
    {
        if (item.IsHeader)
        {
            return;
        }

        int index = Places.IndexOf(item);
        if (index < 0 || index >= Places.Count - 1)
        {
            return;
        }

        int nextIndex = -1;
        for (int i = index + 1; i < Places.Count; i++)
        {
            if (Places[i].Category == item.Category && !Places[i].IsHeader)
            {
                nextIndex = i;
                break;
            }
        }

        if (nextIndex >= 0)
        {
            Places.Move(index, nextIndex);
            SaveItemOrder(item.Category);
        }
    }

    public void ReorderPlace(PlaceItem source, PlaceItem target, bool insertAbove)
    {
        if (source == target || source.IsHeader || target.IsHeader)
        {
            return;
        }

        if (source.Category != target.Category)
        {
            return;
        }

        int oldIndex = Places.IndexOf(source);
        int targetIndex = Places.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0)
        {
            return;
        }

        int newIndex = insertAbove ? targetIndex : targetIndex + 1;
        if (oldIndex < newIndex)
        {
            newIndex--;
        }

        if (newIndex >= 0 && newIndex < Places.Count && newIndex != oldIndex)
        {
            Places.Move(oldIndex, newIndex);
            SaveItemOrder(source.Category);
        }
    }

    public void CancelOperation()
    {
        SelectedOperation?.Cancel();
    }

    public void TransferItemsTo(IEnumerable<string> paths, string destinationDirectory, bool isCopy)
    {
        _ = TransferItemsToAsync(paths, destinationDirectory, isCopy);
    }

    public async Task TransferItemsToAsync(IEnumerable<string> paths, string destinationDirectory, bool isCopy)
    {
        if (string.IsNullOrEmpty(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            return;
        }

        string normDest = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar);
        List<string> pathList = new();

        foreach (string p in paths)
        {
            if (!File.Exists(p) && !Directory.Exists(p))
            {
                continue;
            }

            string normSrc = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);

            if (string.Equals(normSrc, normDest, StringComparison.OrdinalIgnoreCase) ||
                normDest.StartsWith(normSrc + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!isCopy)
            {
                string? srcParent = Path.GetDirectoryName(normSrc);
                if (srcParent != null && string.Equals(Path.GetFullPath(srcParent).TrimEnd(Path.DirectorySeparatorChar), normDest, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            pathList.Add(p);
        }

        if (pathList.Count == 0)
        {
            return;
        }

        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        string destName = Path.GetFileName(destinationDirectory.TrimEnd('/'));
        if (string.IsNullOrEmpty(destName))
        {
            destName = destinationDirectory;
        }

        string operation = isCopy ? "Copy" : "Move";
        OperationProgressViewModel progressView = new()
        {
            Title = LocalizationService.Format(pathList.Count == 1 ? $"{operation}TitleOne" : $"{operation}TitleMany", pathList.Count, destName),
            Text = LocalizationService.Format($"{operation}Progress", 0, pathList.Count),
            Status = LocalizationService.Get("Calculating"),
            Icon = IconService.Get(isCopy ? "copy" : "cut"),
            CancelAction = () => cts.Cancel(),
        };
        AddOperation(progressView);

        Progress<FileTransferProgress> progress = new(p =>
        {
            progressView.Value = p.Percent;
            string sizeText = p.TotalBytes > 0
                ? LocalizationService.Format("TransferSizeProgress", FileUtils.FormatSize(p.TransferredBytes), FileUtils.FormatSize(p.TotalBytes))
                : "";
            progressView.Text = LocalizationService.Format($"{operation}Progress", p.TransferredItems, p.TotalItems) + sizeText;
            progressView.Status = $"{FileUtils.FormatSpeed(p.BytesPerSecond)} — {FormatEta(p.EstimatedRemaining)}";
            progressView.Item = p.CurrentItemName;
        });

        try
        {
            ClipboardMode mode = isCopy ? ClipboardMode.Copy : ClipboardMode.Cut;
            await Task.Run(() => _operationService.TransferAsync(destinationDirectory, mode, pathList, ConflictResolver, progress, token), token);
            List<string> targets = pathList.Select(p => Path.Combine(destinationDirectory, Path.GetFileName(p.TrimEnd('/')))).ToList();
            progressView.Status = LocalizationService.Get("FinishingTransfer");
            progressView.Item = "";
            await WaitForTargetsAsync(targets, token);
            StatusText = LocalizationService.Get($"{operation}Completed");
            TransferCompleted?.Invoke(destinationDirectory, targets);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.Get($"{operation}Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Format($"{operation}Failed", ex.Message);
        }
        finally
        {
            progressView.CancelAction = null;
            RemoveOperation(progressView);
            Refresh();
        }
    }

    private static async Task WaitForTargetsAsync(List<string> targets, CancellationToken token)
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (string target in targets)
                {
                    token.ThrowIfCancellationRequested();
                    _ = File.Exists(target) || Directory.Exists(target);
                }
            }, token).WaitAsync(TransferSettleTimeout, token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
        }
    }

    public void MoveItemsTo(IEnumerable<string> paths, string destinationDirectory)
    {
        TransferItemsTo(paths, destinationDirectory, false);
    }

    public void CopyItemsTo(IEnumerable<string> paths, string destinationDirectory)
    {
        TransferItemsTo(paths, destinationDirectory, true);
    }

    public void EmptyTrash()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "gio",
                Arguments = "trash --empty",
                UseShellExecute = false,
            })?.WaitForExit();
        }
        catch { }

        string trashBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Trash");
        string filesDir = Path.Combine(trashBase, "files");
        string infoDir = Path.Combine(trashBase, "info");

        FolderColorService.Remove(filesDir);

        if (Directory.Exists(filesDir))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(filesDir))
            {
                try
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
                catch { }
            }
        }

        if (Directory.Exists(infoDir))
        {
            foreach (string entry in Directory.EnumerateFiles(infoDir))
            {
                try
                {
                    File.Delete(entry);
                }
                catch { }
            }
        }

        Refresh();
    }

    public void NavigateTo(string path)
    {
        path = LocationResolver.Resolve(path);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (NetworkBrowseService.IsBrowseLocation(path))
        {
            Logger.LogI("Navigated to network location");
            ActivePane.NavigateTo(path);
            return;
        }

        if (IsNetworkUri(path))
        {
            Task.Run(async () =>
            {
                (bool success, string mountPath, string error) = await _networkService.MountUriAsync(path);
                if (success && Directory.Exists(mountPath))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        LoadPlaces();
                        NavigateTo(mountPath);
                    });
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusText = LocalizationService.Format("ConnectionFailedWithError", error);
                    });
                }
            });
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        if (!CanOpenDirectory(path) && DirectoryAccessDenied != null)
        {
            Logger.LogI("Directory access denied");
            DirectoryAccessDenied.Invoke(path);
            return;
        }

        Logger.LogI("Navigated to directory");
        ActivePane.NavigateTo(path);
    }

    private static bool CanOpenDirectory(string path)
    {
        if (Environment.IsPrivilegedProcess || Environment.UserName == "root")
        {
            return true;
        }

        return FileUtils.CanRead(path, true);
    }

    public async Task DismountPlaceAsync(PlaceItem item)
    {
        if (item.IsHeader || string.IsNullOrEmpty(item.Path) || item.Path == "/")
        {
            return;
        }

        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (PaneViewModel pane in Panes)
        {
            if (pane.CurrentPath.StartsWith(item.Path, StringComparison.OrdinalIgnoreCase))
            {
                pane.NavigateTo(userHome);
            }
        }

        (bool success, string error) = await _networkService.UnmountAsync(item.Path);
        LoadPlaces();

        if (success)
        {
            StatusText = LocalizationService.Format("DismountedNamed", item.Name);
        }
        else
        {
            StatusText = LocalizationService.Format("DismountFailedWithError", error);
        }
    }

    private void OpenNetworkShare(FileItemViewModel item)
    {
        string uri = item.FullPath;
        string name = item.Name;
        StatusText = LocalizationService.Format("ConnectingTo", name);

        Task.Run(async () =>
        {
            NetworkMountResult result = await _networkService.ConnectBrowsedShareAsync(uri);
            Dispatcher.UIThread.Post(() =>
            {
                if (result.NeedsCredentials && NetworkLoginRequested != null)
                {
                    StatusText = LocalizationService.Format("NetworkLoginRequired", name);
                    NetworkLoginRequested.Invoke(result.Info);
                    return;
                }

                if (!result.Success || !Directory.Exists(result.MountPath))
                {
                    StatusText = LocalizationService.Format("NetworkShareMountFailed", name, result.ErrorMessage);
                    return;
                }

                OpenMountedShare(result.MountPath);
            });
        });
    }

    public void OpenMountedShare(string mountPath)
    {
        LoadPlaces();
        ResetActiveBreadcrumbTrail();
        NavigateTo(mountPath);
    }

    public NetworkBookmark? GetNetworkBookmark(string uri)
    {
        return _networkBookmarkService.GetBookmarks().FirstOrDefault(b => string.Equals(b.Uri, uri, StringComparison.OrdinalIgnoreCase));
    }

    public void AddNetworkBookmark(NetworkBookmark bookmark)
    {
        _networkBookmarkService.AddBookmark(bookmark);
        LoadPlaces();
    }

    public void RenameNetworkBookmark(string uri, string name)
    {
        if (!_networkBookmarkService.RenameBookmark(uri, name))
        {
            return;
        }

        Logger.LogI("Renamed network connection");
        LoadPlaces();
    }

    public void RemoveNetworkBookmark(string uri)
    {
        _networkBookmarkService.RemoveBookmark(uri);
        LoadPlaces();
    }

    public string? GetNetworkPassword(string uri)
    {
        return _networkBookmarkService.GetPassword(uri);
    }

    public void SetNetworkPassword(string uri, string password)
    {
        _networkBookmarkService.SetPassword(uri, password);
    }

    public async Task<(bool Success, string MountPath, string Error)> MountNetworkBookmarkAsync(string uri, string? password = null, CancellationToken cancellationToken = default)
    {
        return await _networkService.MountUriAsync(uri, password, cancellationToken);
    }

    private static bool IsNetworkUri(string path)
    {
        return path.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("dav://", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("davs://", StringComparison.OrdinalIgnoreCase);
    }

    public void GoBack()
    {
        Logger.LogI("Navigated back");
        ActivePane.GoBack();
    }

    public void GoForward()
    {
        Logger.LogI("Navigated forward");
        ActivePane.GoForward();
    }

    public void GoUp()
    {
        Logger.LogI("Navigated up");
        ActivePane.GoUp();
    }

    public void Refresh()
    {
        Logger.LogI("Refreshed directory");
        InvalidateFreeSpace();

        ReloadPanes();
        LoadPlaces();
        UpdateActiveBreadcrumbs();
    }

    private void ReloadPanes()
    {
        foreach (PaneViewModel pane in Panes)
        {
            pane.Refresh(pane == ActivePane ? SearchQuery : null);
        }
    }

    private bool MatchesPickerFilter(string fileName)
    {
        List<Regex> patterns = _pickerFilterPatterns;
        return patterns.Count == 0 || patterns.Any(p => p.IsMatch(fileName));
    }

    private static Regex GlobToRegex(string glob)
    {
        StringBuilder pattern = new("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char c = glob[i];
            if (c == '*')
            {
                pattern.Append(".*");
                continue;
            }

            if (c == '?')
            {
                pattern.Append('.');
                continue;
            }

            int close = c == '[' ? glob.IndexOf(']', i + 1) : -1;
            if (close <= i + 1)
            {
                pattern.Append(Regex.Escape(c.ToString()));
                continue;
            }

            string set = glob[(i + 1)..close].Replace("\\", "\\\\").Replace("[", "\\[");
            if (set.StartsWith('!'))
            {
                set = "^" + set[1..];
            }

            pattern.Append('[').Append(set).Append(']');
            i = close;
        }

        pattern.Append('$');
        return new(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public async Task<bool> TryCompletePickerAsync(string path)
    {
        bool isValid = PickerMode switch
        {
            PickerMode.File => File.Exists(path),
            PickerMode.Folder => Directory.Exists(path),
            PickerMode.Save => !Directory.Exists(path) && Directory.Exists(Path.GetDirectoryName(path)),
            _ => false,
        };
        if (!isValid)
        {
            return false;
        }

        if (PickerMode == PickerMode.Save && File.Exists(path) && OverwriteConfirmer != null && !await OverwriteConfirmer(path))
        {
            return true;
        }

        PickerCompleted?.Invoke(FilePickerResult.Selected(path));
        return true;
    }

    private async Task ConfirmSaveAsync()
    {
        string name = PickerSelectedName.Trim();
        if (name.Length == 0)
        {
            return;
        }

        string candidate = Path.IsPathRooted(name) ? name : Path.Combine(CurrentPath, name);
        if (Directory.Exists(candidate))
        {
            PickerSelectedName = "";
            NavigateTo(candidate);
            return;
        }

        string? directory = Path.GetDirectoryName(candidate);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (File.Exists(candidate) && OverwriteConfirmer != null && !await OverwriteConfirmer(candidate))
        {
            return;
        }

        PickerCompleted?.Invoke(FilePickerResult.Selected(candidate));
    }

    public void ConfirmCurrentPicker()
    {
        if (!IsPickerMode)
        {
            return;
        }

        if (PickerMode == PickerMode.Save)
        {
            _ = ConfirmSaveAsync();
            return;
        }

        FileItemViewModel? selected = ActivePane.SelectedItem;
        if (PickerOptions?.AllowMultiple == true)
        {
            List<FileItemViewModel> targets = ActivePane.GetSelection();

            if (PickerMode == PickerMode.File)
            {
                List<string> filePaths = targets.Where(t => !t.IsDirectory).Select(t => t.FullPath).ToList();
                if (filePaths.Count > 0)
                {
                    PickerCompleted?.Invoke(FilePickerResult.Selected(filePaths));
                    return;
                }
            }
            else if (PickerMode == PickerMode.Folder)
            {
                List<string> folderPaths = targets.Where(t => t.IsDirectory).Select(t => t.FullPath).ToList();
                if (folderPaths.Count > 0)
                {
                    PickerCompleted?.Invoke(FilePickerResult.Selected(folderPaths));
                    return;
                }
            }
        }

        if (PickerMode == PickerMode.File)
        {
            if (selected != null && !selected.IsDirectory)
            {
                PickerCompleted?.Invoke(FilePickerResult.Selected(selected.FullPath));
                return;
            }

            if (!string.IsNullOrWhiteSpace(PickerSelectedName))
            {
                string candidate = Path.IsPathRooted(PickerSelectedName)
                    ? PickerSelectedName
                    : Path.Combine(CurrentPath, PickerSelectedName);

                if (File.Exists(candidate))
                {
                    PickerCompleted?.Invoke(FilePickerResult.Selected(candidate));
                    return;
                }

                if (Directory.Exists(candidate))
                {
                    NavigateTo(candidate);
                    return;
                }
            }

            return;
        }

        if (PickerMode == PickerMode.Folder)
        {
            if (selected != null && selected.IsDirectory)
            {
                PickerCompleted?.Invoke(FilePickerResult.Selected(selected.FullPath));
                return;
            }

            if (!string.IsNullOrWhiteSpace(PickerSelectedName))
            {
                string candidate = Path.IsPathRooted(PickerSelectedName)
                    ? PickerSelectedName
                    : Path.Combine(CurrentPath, PickerSelectedName);

                if (Directory.Exists(candidate))
                {
                    PickerCompleted?.Invoke(FilePickerResult.Selected(candidate));
                    return;
                }
            }

            if (Directory.Exists(CurrentPath))
            {
                PickerCompleted?.Invoke(FilePickerResult.Selected(CurrentPath));
                return;
            }
        }
    }

    public void CancelPicker()
    {
        PickerCompleted?.Invoke(FilePickerResult.Cancelled());
    }

    public void OpenEntry(FileItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (IsPickerMode)
        {
            if (item.IsDirectory)
            {
                ResetActiveBreadcrumbTrail();
                NavigateTo(item.FullPath);
                return;
            }

            if (PickerMode == PickerMode.File)
            {
                PickerCompleted?.Invoke(FilePickerResult.Selected(item.FullPath));
                return;
            }

            if (PickerMode == PickerMode.Save)
            {
                PickerSelectedName = item.Name;
                _ = ConfirmSaveAsync();
                return;
            }

            return;
        }

        Logger.LogI(item.IsDirectory ? "Opened directory" : "Opened file");

        if (ActivePane.IsVirtual && !NetworkBrowseService.IsBrowseLocation(item.FullPath))
        {
            OpenNetworkShare(item);
            return;
        }

        if (item.IsDirectory)
        {
            ResetActiveBreadcrumbTrail();
            NavigateTo(item.FullPath);
            return;
        }

        if (IsScriptFile(item.FullPath) && ScriptOpenRequested != null)
        {
            ScriptOpenRequested.Invoke(item.FullPath);
            return;
        }

        _executionService.Execute(item.Entry);
    }

    private static readonly string[] ScriptExtensions = [".sh", ".bash"];

    public static bool IsScriptFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (ScriptExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            return true;
        }

        if (!FileUtils.IsFileExecutable(path))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            return stream.ReadByte() == '#' && stream.ReadByte() == '!';
        }
        catch
        {
            return false;
        }
    }

    private static string[] GetScriptCommand(string path)
    {
        if (FileUtils.IsFileExecutable(path))
        {
            return [path];
        }

        return ["bash", path];
    }

    public void RunScript(string path, bool inTerminal)
    {
        string[] command = GetScriptCommand(path);
        Logger.LogI(inTerminal ? "Running script in terminal" : "Running script");

        if (inTerminal)
        {
            RunInTerminal(path, command);
            return;
        }

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = command[0],
                WorkingDirectory = Path.GetDirectoryName(path) ?? "/",
                UseShellExecute = false,
            };
            foreach (string arg in command.Skip(1))
            {
                psi.ArgumentList.Add(arg);
            }

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Format("RunScriptFailed", ex.Message);
        }
    }

    public void ShowFileContent(string path)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "gio",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("open");
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Format("OpenFileFailed", ex.Message);
        }
    }

    public void RunInTerminal(string path, string[] command)
    {
        RunInTerminalAt(Path.GetDirectoryName(path) ?? "/", command);
    }

    public void RunInTerminalAt(string dir, IReadOnlyList<string> command, IReadOnlyDictionary<string, string>? environment = null)
    {
        string quoted = string.Join(" ", command.Select(QuoteForShell));
        string exports = string.Concat((environment ?? new Dictionary<string, string>()).Select(v => $"export {v.Key}={QuoteForShell(v.Value)}; "));
        string shellScript = $"{exports}{quoted}; echo; read -n 1 -s -r -p \"[Process finished - press any key to close]\"";
        string[] candidates = ["x-terminal-emulator", "gnome-terminal", "kitty", "alacritty", "konsole", "xfce4-terminal", "xterm"];

        foreach (string candidate in candidates)
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = candidate,
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                };
                string? separator = candidate switch
                {
                    "gnome-terminal" => "--",
                    "kitty" => null,
                    _ => "-e",
                };
                if (separator != null)
                {
                    psi.ArgumentList.Add(separator);
                }

                psi.ArgumentList.Add("bash");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(shellScript);

                if (Process.Start(psi) != null)
                {
                    return;
                }
            }
            catch
            {
            }
        }

        StatusText = LocalizationService.Get("NoTerminalFound");
    }

    private static string QuoteForShell(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    public void OpenWith(FileItemViewModel item, string program, bool always)
    {
        if (always && OperatingSystem.IsLinux())
        {
            string path = item.FullPath;
            Task.Run(() => MimeService.SetDefaultApplication(path, program));
        }

        try
        {
            FileExecutionService.OpenWith(program, item.FullPath);
        }
        catch { }
    }

    private List<FileItemViewModel> ResolveTargets(IEnumerable<FileItemViewModel>? selection)
    {
        if (ActivePane.IsVirtual)
        {
            return new();
        }

        List<FileItemViewModel> targets = selection?.ToList() ?? new();
        return targets.Count > 0 ? targets : ActivePane.GetSelection();
    }

    public void CopySelected(IEnumerable<FileItemViewModel>? selection = null)
    {
        List<FileItemViewModel> targets = ResolveTargets(selection);
        if (targets.Count == 0)
        {
            return;
        }

        List<string> paths = targets.Select(t => t.FullPath).ToList();
        try
        {
            ClipboardService.WritePaths(ClipboardMode.Copy, paths);
            ClipboardService.Copy(string.Join(Environment.NewLine, paths));
            Logger.LogI($"Copied {targets.Count} items to clipboard");
        }
        catch { }

        foreach (FileItemViewModel item in AllItems)
        {
            item.IsCut = false;
        }
    }

    public void CutSelected(IEnumerable<FileItemViewModel>? selection = null)
    {
        List<FileItemViewModel> targets = ResolveTargets(selection);
        if (targets.Count == 0)
        {
            return;
        }

        List<string> paths = targets.Select(t => t.FullPath).ToList();
        try
        {
            ClipboardService.WritePaths(ClipboardMode.Cut, paths);
            ClipboardService.Copy(string.Join(Environment.NewLine, paths));
            Logger.LogI($"Cut {targets.Count} items to clipboard");
        }
        catch { }

        foreach (FileItemViewModel item in AllItems)
        {
            item.IsCut = paths.Contains(item.FullPath);
        }
    }

    public void Paste(string? targetDirectory = null)
    {
        string dir = targetDirectory ?? CurrentPath;
        if (NetworkBrowseService.IsBrowseLocation(dir))
        {
            return;
        }

        ClipboardMode mode = ClipboardMode.Copy;
        string[] paths = [];
        try
        {
            ClipboardService.ReadPaths(out mode, out paths);
        }
        catch
        {
            return;
        }

        if (paths == null || paths.Length == 0)
        {
            return;
        }

        TransferItemsTo(paths, dir, mode == ClipboardMode.Copy);
        if (mode == ClipboardMode.Cut)
        {
            ClipboardService.ClearPaths();
        }
    }

    public static bool HasPasteContent()
    {
        ClipboardService.ReadPaths(out _, out string[] paths);
        return paths.Any(p => File.Exists(p) || Directory.Exists(p));
    }

    public async Task<List<string>> PasteAsSymlinksAsync(string? targetDirectory = null)
    {
        if (ActivePane.IsVirtual)
        {
            return [];
        }

        ClipboardService.ReadPaths(out _, out string[] paths);
        return await CreateSymlinksAsync(paths, targetDirectory ?? CurrentPath);
    }

    public async Task<List<string>> CreateSymlinksAsync(IEnumerable<string> paths, string dir)
    {
        if (NetworkBrowseService.IsBrowseLocation(dir) || !Directory.Exists(dir))
        {
            return [];
        }

        (List<string> created, List<string> errors) = await _operationService.CreateSymlinksAsync(dir, paths, ConflictResolver);
        if (errors.Count > 0)
        {
            StatusText = LocalizationService.Format("SymlinkFailed", errors[0]);
        }
        else if (created.Count > 0)
        {
            Logger.LogI($"Created {created.Count} symlinks in {dir}");
            StatusText = LocalizationService.Format("SymlinksCreated", created.Count);
        }

        Refresh();
        return created;
    }

    public void DeleteSelected(IEnumerable<FileItemViewModel>? selection = null, bool permanent = false)
    {
        List<FileItemViewModel> targets = ResolveTargets(selection);
        if (targets.Count == 0)
        {
            return;
        }

        List<string> paths = targets.Select(t => t.FullPath).ToList();
        _operationService.Delete(paths, permanent);
        Refresh();
    }

    public void RestoreSelectedFromTrash(PaneViewModel pane)
    {
        List<FileItemViewModel> targets = pane.GetSelection();
        if (targets.Count == 0)
        {
            return;
        }

        List<string> paths = targets
            .Select(t => t.FullPath)
            .ToList();
        _operationService.RestoreFromTrash(paths);
        Refresh();
    }

    public void RenameSelected(FileItemViewModel item, string newName)
    {
        if (ActivePane.IsVirtual || string.IsNullOrWhiteSpace(newName) || item.Name == newName)
        {
            return;
        }

        try
        {
            _operationService.Rename(item.FullPath, newName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFileError("RenameFailed", ex);
        }

        Refresh();
    }

    public void BulkRename(IReadOnlyList<BulkRenameEntry> renames)
    {
        if (ActivePane.IsVirtual || renames.Count == 0)
        {
            return;
        }

        HashSet<string> sourcePaths = new(renames.Select(rename => rename.Path), StringComparer.Ordinal);
        List<(string TempPath, string OriginalName, string NewName)> staged = new();
        List<BulkRenameEntry> direct = new();
        foreach (BulkRenameEntry rename in renames)
        {
            string directory = Path.GetDirectoryName(rename.Path) ?? "";
            if (!sourcePaths.Contains(Path.Combine(directory, rename.NewName)))
            {
                direct.Add(rename);
                continue;
            }

            string tempName = $".fe-rename-{Guid.NewGuid():N}";
            try
            {
                _operationService.Rename(rename.Path, tempName);
                staged.Add((Path.Combine(directory, tempName), Path.GetFileName(rename.Path), rename.NewName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReportFileError("RenameFailed", ex);
            }
        }

        foreach (BulkRenameEntry rename in direct)
        {
            try
            {
                _operationService.Rename(rename.Path, rename.NewName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReportFileError("RenameFailed", ex);
            }
        }

        foreach ((string tempPath, string originalName, string newName) in staged)
        {
            try
            {
                _operationService.Rename(tempPath, newName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReportFileError("RenameFailed", ex);
                TryRestoreStagedRename(tempPath, originalName);
            }
        }

        Refresh();
    }

    private void TryRestoreStagedRename(string tempPath, string originalName)
    {
        try
        {
            _operationService.Rename(tempPath, originalName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = Logger.LogI($"Could not restore {tempPath} to {originalName}");
        }
    }

    public void DuplicateSelected(IEnumerable<FileItemViewModel>? selection = null)
    {
        List<FileItemViewModel> targets = ResolveTargets(selection);
        if (targets.Count == 0)
        {
            return;
        }

        foreach (FileItemViewModel item in targets)
        {
            try
            {
                _operationService.Duplicate(item.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReportFileError("DuplicateFailed", ex);
                break;
            }
        }

        Refresh();
    }

    private void ReportFileError(string key, Exception ex)
    {
        string message = LocalizationService.Format(key, ex.Message);
        if (ex is UnauthorizedAccessException && AccessDenied != null)
        {
            AccessDenied.Invoke(message);
            return;
        }

        StatusText = message;
    }

    public string? CreateFolder(string folderName)
    {
        if (ActivePane.IsVirtual || string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        string baseDir = CurrentPath;
        string fullPath = Path.Combine(baseDir, folderName);
        try
        {
            _operationService.CreateFolder(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFileError("CreateFolderFailed", ex);
            return null;
        }

        Refresh();
        return fullPath;
    }

    public string? CreateFile(string fileName)
    {
        if (ActivePane.IsVirtual || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string baseDir = CurrentPath;
        string fullPath = Path.Combine(baseDir, fileName);
        try
        {
            _operationService.CreateFile(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportFileError("CreateFileFailed", ex);
            return null;
        }

        Refresh();
        return fullPath;
    }

    public void ToggleBookmark(string path)
    {
        _bookmarkService.AddOrRemoveBookmark(path);
        LoadPlaces();
    }

    public void RemoveBookmark(string path)
    {
        _bookmarkService.RemoveBookmark(path);
        LoadPlaces();
    }

    public void OpenAsRoot(string? targetPath = null)
    {
        string path = targetPath ?? CurrentPath;
        Logger.LogI("Opened directory as root");
        FileExecutionService.OpenAsRoot(path);
    }

    public string CommandDirectory => Directory.Exists(CurrentPath) ? CurrentPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static bool IsProgramAvailable(string name, string workingDir)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        if (name.Contains('/'))
        {
            string candidate = Path.GetFullPath(Path.Combine(workingDir, name));
            return File.Exists(candidate) && FileUtils.IsFileExecutable(candidate);
        }

        string[] searchDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
        return searchDirs.Any(dir => File.Exists(Path.Combine(dir, name)) && FileUtils.IsFileExecutable(Path.Combine(dir, name)));
    }

    public bool RunShellCommand(string workingDir, string commandLine)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "sh",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(commandLine);

            using Process? process = Process.Start(psi);
            Logger.LogI("Ran path bar command");
            return process != null;
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Format("CommandFailed", commandLine, ex.Message);
            return false;
        }
    }

    public void OpenTerminal(string? workingDir = null, string? command = null)
    {
        string dir = workingDir ?? CurrentPath;
        string[] candidates = ["x-terminal-emulator", "gnome-terminal", "kitty", "alacritty", "konsole", "xfce4-terminal", "xterm"];

        foreach (string candidate in candidates)
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = candidate,
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                };

                if (!string.IsNullOrEmpty(command))
                {
                    psi.Arguments = candidate == "gnome-terminal" ? $"-- {command}" : $"-e {command}";
                }

                Process? proc = Process.Start(psi);
                if (proc != null)
                {
                    Logger.LogI("Opened terminal");
                    return;
                }
            }
            catch
            {
                continue;
            }
        }
    }

    private List<BreadcrumbItem> BuildBreadcrumbs(string path)
    {
        List<BreadcrumbItem> crumbs = new();
        if (string.IsNullOrEmpty(path))
        {
            return crumbs;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (NetworkBrowseService.IsBrowseLocation(path))
        {
            bool isRoot = NetworkBrowseService.IsNetworkRoot(path);
            crumbs.Add(new BreadcrumbItem { Name = LocalizationService.Get("NetworkBrowse"), FullPath = NetworkBrowseService.NetworkRoot, IsLast = isRoot });
            if (!isRoot)
            {
                crumbs.Add(new BreadcrumbItem { Name = NetworkBrowseService.GetLocationName(path), FullPath = path, IsLast = true });
            }

            return crumbs;
        }

        if (path == "/")
        {
            crumbs.Add(new BreadcrumbItem { Name = LocalizationService.Get("Root"), FullPath = "/", IsLast = true });
            return crumbs;
        }

        if (TryGetMountCrumb(path, out string mountPath, out string mountName))
        {
            AddRootedCrumbs(crumbs, mountPath, mountName, path);
            return crumbs;
        }

        if (path.Equals(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            crumbs.Add(new BreadcrumbItem { Name = LocalizationService.Get("Home"), FullPath = userProfile, IsLast = true });
            return crumbs;
        }

        if (path.StartsWith(userProfile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            AddRootedCrumbs(crumbs, userProfile, LocalizationService.Get("Home"), path);
            return crumbs;
        }

        crumbs.Add(new BreadcrumbItem { Name = LocalizationService.Get("Root"), FullPath = "/", IsLast = false });
        string[] rootParts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        string currentAccum = "";

        for (int i = 0; i < rootParts.Length; i++)
        {
            currentAccum += "/" + rootParts[i];
            crumbs.Add(new BreadcrumbItem
            {
                Name = rootParts[i],
                FullPath = currentAccum,
                IsLast = i == rootParts.Length - 1,
            });
        }

        return crumbs;
    }

    private static void AddRootedCrumbs(List<BreadcrumbItem> crumbs, string rootPath, string rootName, string path)
    {
        string relative = path.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
        string[] parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        crumbs.Add(new BreadcrumbItem { Name = rootName, FullPath = rootPath, IsLast = parts.Length == 0 });

        string accum = rootPath;
        for (int i = 0; i < parts.Length; i++)
        {
            accum = Path.Combine(accum, parts[i]);
            crumbs.Add(new BreadcrumbItem
            {
                Name = parts[i],
                FullPath = accum,
                IsLast = i == parts.Length - 1,
            });
        }
    }

    private bool TryGetMountCrumb(string path, out string mountPath, out string name)
    {
        mountPath = "";
        name = "";

        string gvfsRoot = $"/run/user/{GetUserId()}/gvfs/";
        if (path.StartsWith(gvfsRoot, StringComparison.Ordinal))
        {
            string mountDir = path[gvfsRoot.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (mountDir.Length == 0)
            {
                return false;
            }

            mountPath = gvfsRoot + mountDir;
            string mount = mountPath;
            PlaceItem? place = Places.FirstOrDefault(p => !p.IsHeader && IsSamePath(p.Path, mount));
            name = place?.Name ?? GetGvfsFastName(mountPath);
            return true;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        PlaceItem? device = Places
            .Where(p => !p.IsHeader && p.Category == PlaceCategory.Devices && p.IsMounted && Path.IsPathRooted(p.Path))
            .Where(p => NormalizeTrailPath(p.Path) != "/" && !IsSameOrAncestor(p.Path, userProfile))
            .Where(p => IsSameOrAncestor(p.Path, path))
            .OrderByDescending(p => NormalizeTrailPath(p.Path).Length)
            .FirstOrDefault();
        if (device == null)
        {
            return false;
        }

        mountPath = NormalizeTrailPath(device.Path);
        name = device.Name;
        return true;
    }

    private static string FormatEta(TimeSpan? eta)
    {
        if (!eta.HasValue)
        {
            return LocalizationService.Get("Calculating");
        }

        if (eta.Value.TotalSeconds <= 0)
        {
            return LocalizationService.Get("EtaAlmostDone");
        }

        int seconds = (int) Math.Ceiling(eta.Value.TotalSeconds);
        if (seconds < 5)
        {
            return LocalizationService.Get("EtaFewSeconds");
        }

        if (seconds < 60)
        {
            return LocalizationService.Format("EtaSeconds", seconds);
        }

        int minutes = seconds / 60;
        int remSec = seconds % 60;
        if (minutes < 60)
        {
            return remSec == 0 ? LocalizationService.Format("EtaMinutes", minutes) : LocalizationService.Format("EtaMinutesSeconds", minutes, remSec);
        }

        return LocalizationService.Format("EtaHoursMinutes", minutes / 60, minutes % 60);
    }

    private void UpdateStatus()
    {
        PaneViewModel pane = ActivePane;
        if (pane.IsLoading)
        {
            StatusText = pane.Items.Count > 0 ? LocalizationService.Format("StatusItemsLoading", pane.Items.Count) : LocalizationService.Get("LoadingEllipsis");
            return;
        }

        List<FileItemViewModel> selection = pane.GetSelection();
        long selectedBytes = 0;
        int filesWithBytes = 0;
        foreach (FileItemViewModel item in selection)
        {
            if (item.Entry is FileEntry file)
            {
                selectedBytes += file.FileSize;
                filesWithBytes++;
            }
        }

        string freeSpace = GetFreeDiskSpace(pane.CurrentPath);
        string freeStr = !string.IsNullOrEmpty(freeSpace) ? LocalizationService.Format("StatusFreeSpace", freeSpace) : "";

        if (selection.Count > 0)
        {
            string selected = filesWithBytes > 0
                ? LocalizationService.Format("StatusItemsSelectedSize", pane.Items.Count, selection.Count, FileUtils.FormatSize(selectedBytes))
                : LocalizationService.Format("StatusItemsSelected", pane.Items.Count, selection.Count);
            StatusText = selected + freeStr;
        }
        else
        {
            StatusText = LocalizationService.Format("StatusItems", pane.Items.Count) + freeStr;
        }
    }

    private void MigrateLegacyAssociations()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(_associationsPath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_associationsPath);
            Dictionary<string, string>? data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            foreach (KeyValuePair<string, string> kvp in data ?? new())
            {
                string ext = kvp.Key.StartsWith('.') ? kvp.Key : "." + kvp.Key;
                MimeService.SetDefaultApplication(ext, kvp.Value);
            }

            File.Move(_associationsPath, _associationsPath + ".migrated", true);
            Logger.LogI($"Migrated {data?.Count ?? 0} legacy file associations to MIME defaults");
        }
        catch { }
    }

    [DllImport("libc", EntryPoint = "getuid")]
    private static extern uint libc_getuid();

    private static string GetUserId()
    {
        try
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return libc_getuid().ToString();
            }
        }
        catch
        {
        }

        return "1000";
    }

    private static string GetGvfsFastName(string mountPath)
    {
        string rawName = Path.GetFileName(mountPath);
        if (rawName.StartsWith("mtp:host=", StringComparison.OrdinalIgnoreCase))
        {
            string hostPart = rawName["mtp:host=".Length..];
            string[] parts = hostPart.Split('_', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                return string.Join(" ", parts.Take(Math.Min(2, parts.Length)));
            }

            return LocalizationService.Get("AndroidDevice");
        }

        if (rawName.StartsWith("afc:host=", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get("AppleDevice");
        }

        if (rawName.StartsWith("gphoto2:host=", StringComparison.OrdinalIgnoreCase))
        {
            return LocalizationService.Get("DigitalCamera");
        }

        if (rawName.StartsWith("sftp:host=", StringComparison.OrdinalIgnoreCase))
        {
            string hostPart = rawName["sftp:host=".Length..];
            int commaIdx = hostPart.IndexOf(',');
            string host = commaIdx >= 0 ? hostPart[..commaIdx] : hostPart;
            return $"{host} (SFTP)";
        }

        if (rawName.StartsWith("smb-share:server=", StringComparison.OrdinalIgnoreCase))
        {
            string srvPart = rawName["smb-share:server=".Length..];
            int commaIdx = srvPart.IndexOf(',');
            string srv = commaIdx >= 0 ? srvPart[..commaIdx] : srvPart;
            return $"{srv} (SMB)";
        }

        return rawName;
    }

    private static void ResolveGvfsDisplayNameAsync(string mountPath, PlaceItem item)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "gio",
                Arguments = $"info -a standard::display-name \"{mountPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);

            string[] lines = output.Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("display name:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed["display name:".Length..].Trim();
                    if (!string.IsNullOrEmpty(val) && val != item.Name)
                    {
                        Dispatcher.UIThread.Post(() => item.Name = val);
                        return;
                    }
                }

                if (trimmed.StartsWith("standard::display-name:", StringComparison.OrdinalIgnoreCase))
                {
                    string val = trimmed["standard::display-name:".Length..].Trim();
                    if (!string.IsNullOrEmpty(val) && val != item.Name)
                    {
                        Dispatcher.UIThread.Post(() => item.Name = val);
                        return;
                    }
                }
            }
        }
        catch
        {
        }
    }

    private string _freeSpacePath = "";
    private string _freeSpaceText = "";

    private string GetFreeDiskSpace(string path)
    {
        if (path == _freeSpacePath)
        {
            return _freeSpaceText;
        }

        _freeSpacePath = path;
        _freeSpaceText = "";
        if (string.IsNullOrEmpty(path) || NetworkBrowseService.IsBrowseLocation(path))
        {
            return "";
        }

        Task.Run(() => FileUtils.GetAvailableFreeSpace(path)).ContinueWith(t =>
        {
            long? available = t.IsCompletedSuccessfully ? t.Result : null;
            Dispatcher.UIThread.Post(() =>
            {
                if (_freeSpacePath != path || available == null)
                {
                    return;
                }

                _freeSpaceText = FormatBytes(available.Value);
                UpdateStatus();
            });
        });

        return "";
    }

    private void InvalidateFreeSpace()
    {
        _freeSpacePath = "";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    public void Dispose()
    {
        _reloadPlacesCts?.Dispose();
        lock (_deviceWatchers)
        {
            foreach (IDisposable w in _deviceWatchers)
            {
                w.Dispose();
            }

            _deviceWatchers.Clear();
        }

        try
        {
            if (_udisksMonitorProcess != null && !_udisksMonitorProcess.HasExited)
            {
                _udisksMonitorProcess.Kill();
            }
        }
        catch { }
        _udisksMonitorProcess?.Dispose();
        _udisksMonitorProcess = null;

        Pane1.Dispose();
        Pane2.Dispose();
        foreach (OperationProgressViewModel operation in Operations.ToList())
        {
            operation.Cancel();
        }
    }
}
