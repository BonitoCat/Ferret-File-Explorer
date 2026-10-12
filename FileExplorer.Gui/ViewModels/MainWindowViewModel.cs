using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private readonly Dictionary<string, string> _associations = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _associationsPath;

    private NavigationState _navigation;
    private NavigationState? _navigation2;

    private string _pane1Path = "";
    private string _pane2Path = "";
    private string _searchQuery = "";
    private bool _showHiddenFiles = true;
    private FileItemViewModel? _selectedItem;
    private FileItemViewModel? _pane2SelectedItem;
    private string _statusText = "";
    private bool _showDetailsPane;
    private bool _isGridView = true;
    private bool _isDualPane;
    private double _colNameWidth = 260;
    private double _colSizeWidth = 110;
    private double _colTypeWidth = 110;
    private double _colModifiedWidth = 140;
    public event Action? BeforeFolderLoad;
    private bool _isPathEditing;
    private int _activePane = 1;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _loadPane2Cts;
    private CancellationTokenSource? _operationCts;

    private FileSystemWatcher? _watcher1;
    private FileSystemWatcher? _watcher2;
    private DispatcherTimer? _debounceTimer1;
    private DispatcherTimer? _debounceTimer2;

    private bool _isOperationInProgress;
    private string _operationTitle = "";
    private double _operationPercent;
    private string _operationItemsProgress = "";
    private string _operationSpeedAndEta = "";
    private string _operationCurrentFileName = "";
    private string _operationIconSvg = "";
    private IImage? _operationIcon;

    private Bitmap? _previewImage;
    private string _previewDetails = "";
    private bool _hasImagePreview;

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

    public bool IsOperationInProgress
    {
        get => _isOperationInProgress;
        set => SetProperty(ref _isOperationInProgress, value);
    }

    public string OperationTitle
    {
        get => _operationTitle;
        set => SetProperty(ref _operationTitle, value);
    }

    public double OperationPercent
    {
        get => _operationPercent;
        set => SetProperty(ref _operationPercent, value);
    }

    public string OperationItemsProgress
    {
        get => _operationItemsProgress;
        set => SetProperty(ref _operationItemsProgress, value);
    }

    public string OperationSpeedAndEta
    {
        get => _operationSpeedAndEta;
        set => SetProperty(ref _operationSpeedAndEta, value);
    }

    public string OperationCurrentFileName
    {
        get => _operationCurrentFileName;
        set => SetProperty(ref _operationCurrentFileName, value);
    }

    public string OperationIconSvg
    {
        get => _operationIconSvg;
        set => SetProperty(ref _operationIconSvg, value);
    }

    public IImage? OperationIcon
    {
        get => _operationIcon;
        set => SetProperty(ref _operationIcon, value);
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
                if (ActivePane == 2)
                {
                    _pane1Path = _pane2Path;
                    _navigation = _navigation2 ?? new(_pane2Path);
                    LoadFolder(_pane1Path);
                }

                _pane2Path = "";
                _navigation2 = null;
                ActivePane = 1;
                OnPropertyChanged(nameof(IsPane1InTrash));
                OnPropertyChanged(nameof(IsPane2InTrash));
            }

            _isDualPane = value;
            OnPropertyChanged(nameof(IsDualPane));
            OnPropertyChanged(nameof(SecondaryPaneWidth));
            OnPropertyChanged(nameof(SecondaryPaneMinWidth));

            if (value && string.IsNullOrEmpty(_pane2Path))
            {
                NavigatePane2To(_pane1Path);
            }
        }
    }

    public GridLength SecondaryPaneWidth => IsDualPane ? new(1, GridUnitType.Star) : new(0, GridUnitType.Pixel);
    public double SecondaryPaneMinWidth => IsDualPane ? 200 : 0;

    public bool IsPathEditing
    {
        get => _isPathEditing;
        set => SetProperty(ref _isPathEditing, value);
    }

    public int ActivePane
    {
        get => _activePane;
        set
        {
            if (SetProperty(ref _activePane, value))
            {
                OnPropertyChanged(nameof(IsPane1Active));
                OnPropertyChanged(nameof(IsPane2Active));
                OnPropertyChanged(nameof(CurrentPath));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoForward));
                OnPropertyChanged(nameof(CanGoUp));
                OnPropertyChanged(nameof(IsInTrash));
                UpdateActiveBreadcrumbs();
                NotifySelectionProperties();
                UpdateStatus();
                GuiInstanceRegistry.Update(Environment.ProcessId, CurrentPath);

                if (!string.IsNullOrEmpty(SearchQuery))
                {
                    if (value == 2)
                    {
                        LoadPane2Folder(_pane2Path, SearchQuery);
                    }
                    else
                    {
                        LoadFolder(_pane1Path, SearchQuery);
                    }
                }
            }
        }
    }

    public bool IsPane1Active => ActivePane == 1;
    public bool IsPane2Active => ActivePane == 2;

    private bool _isPane1Loading;
    public bool IsPane1Loading
    {
        get => _isPane1Loading;
        set
        {
            if (SetProperty(ref _isPane1Loading, value))
            {
                OnPropertyChanged(nameof(IsLoading));
            }
        }
    }

    private bool _isPane2Loading;
    public bool IsPane2Loading
    {
        get => _isPane2Loading;
        set
        {
            if (SetProperty(ref _isPane2Loading, value))
            {
                OnPropertyChanged(nameof(IsLoading));
            }
        }
    }

    public bool IsLoading => _isPane1Loading || _isPane2Loading;

    public bool IsItemSelected => (ActivePane == 2 ? Pane2SelectedItem : SelectedItem) != null;
    public bool IsFolderSelected => (ActivePane == 2 ? Pane2SelectedItem : SelectedItem)?.IsDirectory == true;
    public bool IsFileSelected => IsItemSelected && !IsFolderSelected;
    public bool IsEmptySpaceSelected => !IsItemSelected;
    public bool CanBookmarkSelection => IsFolderSelected || IsEmptySpaceSelected;
    public bool CanCreateNew => IsEmptySpaceSelected || IsFolderSelected;
    public bool CanExtractSelection => (ActivePane == 2 ? Pane2SelectedItem : SelectedItem) is FileItemViewModel item && (item.FileType == FileType.Archive || IsArchiveFile(item.FullPath));
    public bool Pane2CanExtractSelection => Pane2SelectedItem is FileItemViewModel item && (item.FileType == FileType.Archive || IsArchiveFile(item.FullPath));

    public static bool IsArchiveFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".zip" || ext == ".7z" || ext == ".tar" || ext == ".gz" || ext == ".xz" || ext == ".bz2" || ext == ".zst" || ext == ".rar" || ext == ".deb" || ext == ".tgz";
    }

    public void NotifySelectionProperties()
    {
        OnPropertyChanged(nameof(IsItemSelected));
        OnPropertyChanged(nameof(IsFolderSelected));
        OnPropertyChanged(nameof(IsFileSelected));
        OnPropertyChanged(nameof(IsEmptySpaceSelected));
        OnPropertyChanged(nameof(CanBookmarkSelection));
        OnPropertyChanged(nameof(CanCreateNew));
        OnPropertyChanged(nameof(CanExtractSelection));
        OnPropertyChanged(nameof(Pane2CanExtractSelection));
    }

    public void ResetPlacesOrder()
    {
        LoadPlaces();
    }

    public BulkObservableCollection<FileItemViewModel> Items { get; } = new();
    public BulkObservableCollection<FileItemViewModel> Pane2Items { get; } = new();
    public ObservableCollection<PlaceItem> Places { get; } = new();
    public ObservableCollection<FileItemViewModel> SelectedItems { get; } = new();
    public ObservableCollection<FileItemViewModel> Pane2SelectedItems { get; } = new();
    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();
    public ObservableCollection<BreadcrumbItem> Pane2Breadcrumbs { get; } = new();

    public bool IsPane1InTrash => !string.IsNullOrEmpty(_pane1Path) && _pane1Path.Contains("/.local/share/Trash");
    public bool IsPane2InTrash => !string.IsNullOrEmpty(_pane2Path) && _pane2Path.Contains("/.local/share/Trash");

    public bool IsInTrash
    {
        get
        {
            string p = CurrentPath;
            return !string.IsNullOrEmpty(p) && p.Contains("/.local/share/Trash");
        }
    }

    public string CurrentPath
    {
        get => ActivePane == 2 ? _pane2Path : _pane1Path;
        set
        {
            if (ActivePane == 2)
            {
                _pane2Path = value;
                OnPropertyChanged(nameof(IsPane2InTrash));
            }
            else
            {
                _pane1Path = value;
                OnPropertyChanged(nameof(IsPane1InTrash));
            }

            UpdateActiveBreadcrumbs();
            OnPropertyChanged(nameof(CurrentPath));
            OnPropertyChanged(nameof(CanGoUp));
            OnPropertyChanged(nameof(IsInTrash));
            GuiInstanceRegistry.Update(Environment.ProcessId, CurrentPath);
        }
    }

    public string Pane1Path => _pane1Path;

    public string Pane2Path
    {
        get => _pane2Path;
        set
        {
            if (SetProperty(ref _pane2Path, value))
            {
                OnPropertyChanged(nameof(IsPane2InTrash));
                if (ActivePane == 2)
                {
                    UpdateActiveBreadcrumbs();
                    OnPropertyChanged(nameof(CurrentPath));
                    OnPropertyChanged(nameof(CanGoUp));
                    OnPropertyChanged(nameof(IsInTrash));
                    GuiInstanceRegistry.Update(Environment.ProcessId, _pane2Path);
                }

                OnPropertyChanged(nameof(Pane2CanGoUp));
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                if (ActivePane == 2)
                {
                    LoadPane2Folder(_pane2Path, value);
                }
                else
                {
                    LoadFolder(_pane1Path, value);
                }
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
                LoadFolder(_pane1Path, SearchQuery);
                if (IsDualPane && !string.IsNullOrEmpty(_pane2Path))
                {
                    LoadPane2Folder(_pane2Path);
                }
            }
        }
    }

    public FileItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                UpdatePreview(value);
                UpdateStatus();
                NotifySelectionProperties();

                if (IsPickerMode)
                {
                    if (PickerMode == PickerMode.File && value != null && !value.IsDirectory)
                    {
                        PickerSelectedName = value.Name;
                    }
                    else if (PickerMode == PickerMode.Folder && value != null && value.IsDirectory)
                    {
                        PickerSelectedName = value.Name;
                    }
                }
            }
        }
    }

    public FileItemViewModel? Pane2SelectedItem
    {
        get => _pane2SelectedItem;
        set
        {
            if (SetProperty(ref _pane2SelectedItem, value))
            {
                if (ActivePane == 2)
                {
                    UpdatePreview(value);
                    UpdateStatus();
                }
                NotifySelectionProperties();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool ShowDetailsPane
    {
        get => _showDetailsPane;
        set => SetProperty(ref _showDetailsPane, value);
    }

    public Bitmap? PreviewImage
    {
        get => _previewImage;
        set
        {
            if (SetProperty(ref _previewImage, value))
            {
                HasImagePreview = value != null;
            }
        }
    }

    public bool HasImagePreview
    {
        get => _hasImagePreview;
        set => SetProperty(ref _hasImagePreview, value);
    }

    public string PreviewDetails
    {
        get => _previewDetails;
        set => SetProperty(ref _previewDetails, value);
    }

    public bool CanGoBack => (ActivePane == 2 ? _navigation2?.CanGoBack : _navigation.CanGoBack) ?? false;
    public bool CanGoForward => (ActivePane == 2 ? _navigation2?.CanGoForward : _navigation.CanGoForward) ?? false;
    public bool CanGoUp => Path.GetDirectoryName(CurrentPath) != null;

    public bool Pane2CanGoBack => _navigation2?.CanGoBack ?? false;
    public bool Pane2CanGoForward => _navigation2?.CanGoForward ?? false;
    public bool Pane2CanGoUp => !string.IsNullOrEmpty(Pane2Path) && Path.GetDirectoryName(Pane2Path) != null;

    public FilePickerOptions? PickerOptions { get; }

    public bool IsPickerMode => PickerOptions != null && PickerOptions.Mode != PickerMode.None;
    public bool IsPrivilegedProcess => Environment.IsPrivilegedProcess || Environment.UserName == "root";

    public PickerMode PickerMode => PickerOptions?.Mode ?? PickerMode.None;

    public string PickerTargetLabel => PickerMode == PickerMode.Folder ? "Folder:" : "File:";

    public string PickerActionButtonText => PickerOptions?.ActionButtonText ?? (PickerMode == PickerMode.Folder ? "Select Folder" : "Open");

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

        string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fe");
        _associationsPath = Path.Combine(configDir, "associations.json");
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(configDir);
                LoadAssociations();
            }
            catch
            {
            }
        });

        string startPath = initialPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(startPath) || !Directory.Exists(startPath))
        {
            startPath = "/";
        }

        _navigation = new(startPath);
        _pane1Path = startPath;
        UpdateActiveBreadcrumbs();

        _executionService.OnOpenTerminalForExecutable = path =>
        {
            string? dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            OpenTerminal(dir, path);
        };

        LoadPlaces();
        StartDeviceMonitoring();
        LoadFolder(_pane1Path);
    }

    public void UpdateActiveBreadcrumbs()
    {
        UpdateBreadcrumbs(CurrentPath, Breadcrumbs);
    }

    public void SetGridView(bool grid)
    {
        IsGridView = grid;
        Logger.LogI($"Set grid view: {grid}");
    }

    public void ToggleDualPane()
    {
        IsDualPane = !IsDualPane;
        Logger.LogI($"Toggled dual pane: {IsDualPane}");
    }

    public void TogglePathEditing()
    {
        IsPathEditing = !IsPathEditing;
    }

    public void LoadPlaces()
    {
        Places.Clear();

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string downloads = Path.Combine(userProfile, "Downloads");
        string music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        Places.Add(new PlaceItem { Name = LocalizationService.Get("Places"), Path = "", IsHeader = true, Category = PlaceCategory.Places });

        if (Directory.Exists(userProfile))
        {
            Places.Add(new PlaceItem
            {
                Name = LocalizationService.Get("Home"),
                Path = userProfile,
                Icon = IconService.Get("home"),
                Category = PlaceCategory.Places,
            });
        }

        if (Directory.Exists(desktop))
        {
            Places.Add(new PlaceItem
            {
                Name = "Desktop",
                Path = desktop,
                Icon = IconService.Get("desktop"),
                Category = PlaceCategory.Places,
            });
        }

        if (Directory.Exists(documents))
        {
            Places.Add(new PlaceItem
            {
                Name = "Documents",
                Path = documents,
                Icon = IconService.Get("documents"),
                Category = PlaceCategory.Places,
            });
        }

        if (Directory.Exists(downloads))
        {
            Places.Add(new PlaceItem
            {
                Name = "Downloads",
                Path = downloads,
                Icon = IconService.Get("downloads"),
                Category = PlaceCategory.Places,
            });
        }

        if (Directory.Exists(music))
        {
            Places.Add(new PlaceItem
            {
                Name = "Music",
                Path = music,
                Icon = IconService.Get("music"),
                Category = PlaceCategory.Places,
            });
        }

        if (Directory.Exists(pictures))
        {
            Places.Add(new PlaceItem
            {
                Name = "Pictures",
                Path = pictures,
                Icon = IconService.Get("pictures"),
                Category = PlaceCategory.Places,
            });
        }

        if (Directory.Exists(videos))
        {
            Places.Add(new PlaceItem
            {
                Name = "Videos",
                Path = videos,
                Icon = IconService.Get("videos"),
                Category = PlaceCategory.Places,
            });
        }

        Places.Add(new PlaceItem
        {
            Name = LocalizationService.Get("Root"),
            Path = "/",
            Icon = IconService.Get("disk"),
            Category = PlaceCategory.Places,
        });

        string trashDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Trash", "files");
        try
        {
            Directory.CreateDirectory(trashDir);
        }
        catch { }

        Places.Add(new PlaceItem
        {
            Name = LocalizationService.Get("Trash"),
            Path = trashDir,
            Icon = IconService.Get("trash"),
            Category = PlaceCategory.Places,
        });

        LoadDevicesAndNetworkPlacesAsync();
    }

    private void LoadDevicesAndNetworkPlacesAsync()
    {
        Task.Run(() =>
        {
            List<PlaceItem> extraPlaces = new();
            extraPlaces.Add(new PlaceItem { Name = LocalizationService.Get("Devices"), Path = "", IsHeader = true, Category = PlaceCategory.Devices });

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
                                ParseBlockDevices(dev, extraPlaces, seenPaths);
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
                        name = "Drive";
                    }

                    extraPlaces.Add(new()
                    {
                        Name = name,
                        Path = mount,
                        Icon = IconService.Get("disk"),
                        Category = PlaceCategory.Devices,
                        IsMounted = true,
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

                        string mountToResolve = gvfsMount;
                        PlaceItem itemToUpdate = placeItem;
                        Task.Run(() => ResolveGvfsDisplayNameAsync(mountToResolve, itemToUpdate));
                    }
                }
            }
            catch { }

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
                });
            }

            extraPlaces.Add(new PlaceItem { Name = LocalizationService.Get("Network"), Path = "", IsHeader = true, Category = PlaceCategory.Network });
            foreach (PlaceItem netPlace in networkPlaces)
            {
                extraPlaces.Add(netPlace);
            }

            IEnumerable<string> bookmarks = _bookmarkService.GetBookmarks();
            List<string> bookmarkList = bookmarks.ToList();

            if (bookmarkList.Count > 0)
            {
                extraPlaces.Add(new PlaceItem { Name = LocalizationService.Get("Bookmarks"), Path = "", IsHeader = true, Category = PlaceCategory.Bookmarks });

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
                        Name = Path.GetFileName(bm),
                        Path = resolvedPath,
                        Icon = IconService.Get("bookmark"),
                        IsBookmark = true,
                        Category = PlaceCategory.Bookmarks,
                    });
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                for (int i = Places.Count - 1; i >= 0; i--)
                {
                    if (Places[i].Category != PlaceCategory.Places)
                    {
                        Places.RemoveAt(i);
                    }
                }

                foreach (PlaceItem item in extraPlaces)
                {
                    Places.Add(item);
                }
            });
        });
    }

    private CancellationTokenSource? _reloadPlacesCts;
    private readonly List<IDisposable> _deviceWatchers = new();
    private Process? _udisksMonitorProcess;

    private static void ParseBlockDevices(JsonElement elem, List<PlaceItem> extraPlaces, HashSet<string> seenPaths)
    {
        if (elem.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                ParseBlockDevices(child, extraPlaces, seenPaths);
            }

            return;
        }

        string? path = elem.TryGetProperty("path", out JsonElement pathElem) ? pathElem.GetString() : null;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string? mountpoint = elem.TryGetProperty("mountpoint", out JsonElement mpElem) ? mpElem.GetString() : null;
        string? fstype = elem.TryGetProperty("fstype", out JsonElement fsElem) ? fsElem.GetString() : null;
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
                name = "Drive";
            }

            extraPlaces.Add(new()
            {
                Name = name,
                Path = mountpoint,
                DevicePath = path,
                Icon = IconService.Get("disk"),
                Category = PlaceCategory.Devices,
                IsMounted = true,
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
                        StatusText = $"{LocalizationService.Get("Mounted")} {mountPoint}";
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
                    StatusText = $"{LocalizationService.Get("Mounted")} {mountPoint}";
                    return mountPoint;
                }
            }

            if (process.ExitCode != 0)
            {
                StatusText = $"{LocalizationService.Get("MountFailed")}: {stderr.Trim()}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"{LocalizationService.Get("MountFailed")}: {ex.Message}";
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

        try
        {
            if (Directory.Exists("/media"))
            {
                FileSystemWatcher mediaWatcher = new("/media")
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                };
                mediaWatcher.Created += (_, _) => DebouncedReloadPlaces();
                mediaWatcher.Deleted += (_, _) => DebouncedReloadPlaces();
                mediaWatcher.Renamed += (_, _) => DebouncedReloadPlaces();
                _deviceWatchers.Add(mediaWatcher);
            }

            if (Directory.Exists("/run/media"))
            {
                FileSystemWatcher runMediaWatcher = new("/run/media")
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true,
                };
                runMediaWatcher.Created += (_, _) => DebouncedReloadPlaces();
                runMediaWatcher.Deleted += (_, _) => DebouncedReloadPlaces();
                runMediaWatcher.Renamed += (_, _) => DebouncedReloadPlaces();
                _deviceWatchers.Add(runMediaWatcher);
            }

            string gvfsDir = $"/run/user/{GetUserId()}/gvfs";
            if (Directory.Exists(gvfsDir))
            {
                FileSystemWatcher gvfsWatcher = new(gvfsDir)
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };
                gvfsWatcher.Created += (_, _) => DebouncedReloadPlaces();
                gvfsWatcher.Deleted += (_, _) => DebouncedReloadPlaces();
                gvfsWatcher.Renamed += (_, _) => DebouncedReloadPlaces();
                _deviceWatchers.Add(gvfsWatcher);
            }
        }
        catch { }

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
        }
    }

    public void CancelOperation()
    {
        _operationCts?.Cancel();
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

        _operationCts?.Cancel();
        _operationCts = new();
        CancellationToken token = _operationCts.Token;

        string destName = Path.GetFileName(destinationDirectory.TrimEnd('/'));
        if (string.IsNullOrEmpty(destName))
        {
            destName = destinationDirectory;
        }

        string actionVerb = isCopy ? "Copying" : "Moving";
        string actionNoun = isCopy ? "copied" : "moved";
        OperationTitle = $"{actionVerb} {pathList.Count} {(pathList.Count == 1 ? "item" : "items")} to '{destName}'...";
        OperationItemsProgress = $"0 of {pathList.Count} {actionNoun}";
        OperationSpeedAndEta = "Calculating...";
        OperationPercent = 0;
        OperationCurrentFileName = "";
        OperationIcon = IconService.Get(isCopy ? "copy" : "cut");
        IsOperationInProgress = true;

        Progress<FileTransferProgress> progress = new(p =>
        {
            OperationPercent = p.Percent;
            string sizeText = p.TotalBytes > 0
                ? $" ({FileUtils.FormatSize(p.TransferredBytes)} of {FileUtils.FormatSize(p.TotalBytes)})"
                : "";
            OperationItemsProgress = $"{p.TransferredItems} of {p.TotalItems} {actionNoun}{sizeText}";
            OperationSpeedAndEta = $"{FileUtils.FormatSpeed(p.BytesPerSecond)} — {FileUtils.FormatEta(p.EstimatedRemaining)}";
            OperationCurrentFileName = p.CurrentItemName;
        });

        try
        {
            ClipboardMode mode = isCopy ? ClipboardMode.Copy : ClipboardMode.Cut;
            await Task.Run(() => _operationService.TransferAsync(destinationDirectory, mode, pathList, progress, token), token);
            StatusText = $"{actionVerb} completed successfully.";
        }
        catch (OperationCanceledException)
        {
            StatusText = $"{actionVerb} cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"{actionVerb} failed: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
            Refresh();
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

    public void LoadFolder(string path, string? searchPattern = null)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        CancellationTokenSource cts = new();
        _loadCts = cts;
        CancellationToken token = cts.Token;

        _pane1Path = path;
        UpdateWatcher1(path);
        OnPropertyChanged(nameof(IsPane1InTrash));
        BeforeFolderLoad?.Invoke();
        SelectedItem = null;
        SelectedItems.Clear();
        Items.Clear();
        IsPane1Loading = true;
        StatusText = LocalizationService.Get("Loading") + "...";

        if (ActivePane == 1)
        {
            UpdateActiveBreadcrumbs();
            OnPropertyChanged(nameof(CurrentPath));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            OnPropertyChanged(nameof(CanGoUp));
            OnPropertyChanged(nameof(IsInTrash));
            GuiInstanceRegistry.Update(Environment.ProcessId, _pane1Path);
        }

        bool showHidden = ShowHiddenFiles;
        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            ClipboardMode clipMode = ClipboardMode.Copy;
            string[] clipPaths = [];
            try
            {
                ClipboardService.ReadPaths(out clipMode, out clipPaths);
            }
            catch
            {
            }

            List<FileItemViewModel> batch = new();
            Stopwatch stopwatch = Stopwatch.StartNew();
            int batchThreshold = 25;

            try
            {
                foreach (IFileSystemEntry entry in _fileService.EnumerateFolderEntries(path, searchPattern, token))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!showHidden && entry.IsHidden)
                    {
                        continue;
                    }

                    FileItemViewModel item = FileItemViewModel.FromEntry(entry);
                    if (clipMode == ClipboardMode.Cut && clipPaths.Contains(entry.FullPath))
                    {
                        item.IsCut = true;
                    }

                    batch.Add(item);

                    if (batch.Count >= batchThreshold || stopwatch.ElapsedMilliseconds >= 60)
                    {
                        List<FileItemViewModel> toPost = batch;
                        batch = new();
                        if (batchThreshold < 120)
                        {
                            batchThreshold = Math.Min(batchThreshold * 2, 120);
                        }

                        stopwatch.Restart();

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            Items.AddRange(toPost);
                            if (ActivePane == 1)
                            {
                                StatusText = $"{Items.Count} {LocalizationService.Get("ItemsCount")} ({LocalizationService.Get("Loading")}...)";
                            }
                        }, DispatcherPriority.Background);

                        Thread.Sleep(10);
                    }
                }

                if (batch.Count > 0)
                {
                    List<FileItemViewModel> toPost = batch;
                    batch = new();

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        Items.AddRange(toPost);
                    }, DispatcherPriority.Background);
                }
            }
            catch
            {
                return;
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_loadCts == cts)
                    {
                        IsPane1Loading = false;
                        if (ActivePane == 1)
                        {
                            UpdateStatus();
                        }
                    }
                });
            }
        }, token);
    }

    public void LoadPane2Folder(string path, string? searchPattern = null)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        _loadPane2Cts?.Cancel();
        _loadPane2Cts?.Dispose();
        CancellationTokenSource cts = new();
        _loadPane2Cts = cts;
        CancellationToken token = cts.Token;

        _pane2Path = path;
        UpdateWatcher2(path);
        OnPropertyChanged(nameof(IsPane2InTrash));
        BeforeFolderLoad?.Invoke();
        Pane2SelectedItem = null;
        Pane2SelectedItems.Clear();
        Pane2Items.Clear();
        IsPane2Loading = true;
        StatusText = LocalizationService.Get("Loading") + "...";

        if (ActivePane == 2)
        {
            UpdateActiveBreadcrumbs();
            OnPropertyChanged(nameof(CurrentPath));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            OnPropertyChanged(nameof(CanGoUp));
            OnPropertyChanged(nameof(IsInTrash));
            GuiInstanceRegistry.Update(Environment.ProcessId, _pane2Path);
        }

        OnPropertyChanged(nameof(Pane2CanGoBack));
        OnPropertyChanged(nameof(Pane2CanGoForward));
        OnPropertyChanged(nameof(Pane2CanGoUp));

        bool showHidden = ShowHiddenFiles;
        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            ClipboardMode clipMode = ClipboardMode.Copy;
            string[] clipPaths = [];
            try
            {
                ClipboardService.ReadPaths(out clipMode, out clipPaths);
            }
            catch
            {
            }

            List<FileItemViewModel> batch = new();
            Stopwatch stopwatch = Stopwatch.StartNew();
            int batchThreshold = 25;

            try
            {
                foreach (IFileSystemEntry entry in _fileService.EnumerateFolderEntries(path, searchPattern, token))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!showHidden && entry.IsHidden)
                    {
                        continue;
                    }

                    FileItemViewModel item = FileItemViewModel.FromEntry(entry);
                    if (clipMode == ClipboardMode.Cut && clipPaths.Contains(entry.FullPath))
                    {
                        item.IsCut = true;
                    }

                    batch.Add(item);

                    if (batch.Count >= batchThreshold || stopwatch.ElapsedMilliseconds >= 60)
                    {
                        List<FileItemViewModel> toPost = batch;
                        batch = new();
                        if (batchThreshold < 120)
                        {
                            batchThreshold = Math.Min(batchThreshold * 2, 120);
                        }

                        stopwatch.Restart();

                        Dispatcher.UIThread.Post(() =>
                        {
                            if (token.IsCancellationRequested)
                            {
                                return;
                            }

                            Pane2Items.AddRange(toPost);
                            if (ActivePane == 2)
                            {
                                StatusText = $"{Pane2Items.Count} {LocalizationService.Get("ItemsCount")} ({LocalizationService.Get("Loading")}...)";
                            }
                        }, DispatcherPriority.Background);

                        Thread.Sleep(10);
                    }
                }

                if (batch.Count > 0)
                {
                    List<FileItemViewModel> toPost = batch;
                    batch = new();

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        Pane2Items.AddRange(toPost);
                    }, DispatcherPriority.Background);
                }
            }
            catch
            {
                return;
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_loadPane2Cts == cts)
                    {
                        IsPane2Loading = false;
                        if (ActivePane == 2)
                        {
                            UpdateStatus();
                        }
                    }
                });
            }
        }, token);
    }

    public void NavigateTo(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
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
                        StatusText = $"Connection failed: {error}";
                    });
                }
            });
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        Logger.LogI("Navigated to directory");

        if (ActivePane == 2)
        {
            NavigatePane2To(path);
        }
        else
        {
            NavigatePane1To(path);
        }
    }

    public async Task DismountPlaceAsync(PlaceItem item)
    {
        if (item.IsHeader || string.IsNullOrEmpty(item.Path) || item.Path == "/")
        {
            return;
        }

        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (_pane1Path.StartsWith(item.Path, StringComparison.OrdinalIgnoreCase))
        {
            NavigatePane1To(userHome);
        }

        if (_pane2Path.StartsWith(item.Path, StringComparison.OrdinalIgnoreCase))
        {
            NavigatePane2To(userHome);
        }

        (bool success, string error) = await _networkService.UnmountAsync(item.Path);
        LoadPlaces();

        if (success)
        {
            StatusText = $"{item.Name} {LocalizationService.Get("DismountSuccess")}";
        }
        else
        {
            StatusText = $"{LocalizationService.Get("DismountFailed")}: {error}";
        }
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

    public void NavigatePane1To(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        _navigation.NavigateTo(path);
        LoadFolder(_navigation.CurrentPath);
    }

    public void NavigatePane2To(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        _navigation2 ??= new(path);
        _navigation2.NavigateTo(path);
        LoadPane2Folder(_navigation2.CurrentPath);
    }

    public void GoBack()
    {
        Logger.LogI("Navigated back");

        if (ActivePane == 2)
        {
            GoBackPane2();
        }
        else
        {
            GoBackPane1();
        }
    }

    public void GoBackPane1()
    {
        if (!_navigation.CanGoBack)
        {
            return;
        }

        _navigation.GoBack();
        LoadFolder(_navigation.CurrentPath);
    }

    public void GoForward()
    {
        Logger.LogI("Navigated forward");

        if (ActivePane == 2)
        {
            GoForwardPane2();
        }
        else
        {
            GoForwardPane1();
        }
    }

    public void GoForwardPane1()
    {
        if (!_navigation.CanGoForward)
        {
            return;
        }

        _navigation.GoForward();
        LoadFolder(_navigation.CurrentPath);
    }

    public void GoUp()
    {
        Logger.LogI("Navigated up");

        if (ActivePane == 2)
        {
            GoUpPane2();
        }
        else
        {
            GoUpPane1();
        }
    }

    public void GoUpPane1()
    {
        string? parent = Path.GetDirectoryName(_pane1Path);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return;
        }

        NavigatePane1To(parent);
    }

    public void GoBackPane2()
    {
        if (_navigation2 == null || !_navigation2.CanGoBack)
        {
            return;
        }

        _navigation2.GoBack();
        LoadPane2Folder(_navigation2.CurrentPath);
    }

    public void GoForwardPane2()
    {
        if (_navigation2 == null || !_navigation2.CanGoForward)
        {
            return;
        }

        _navigation2.GoForward();
        LoadPane2Folder(_navigation2.CurrentPath);
    }

    public void GoUpPane2()
    {
        string? parent = Path.GetDirectoryName(_pane2Path);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return;
        }

        NavigatePane2To(parent);
    }

    public void Refresh()
    {
        Logger.LogI("Refreshed directory");

        LoadFolder(_pane1Path, ActivePane == 1 ? SearchQuery : null);
        if (IsDualPane && !string.IsNullOrEmpty(_pane2Path))
        {
            LoadPane2Folder(_pane2Path, ActivePane == 2 ? SearchQuery : null);
        }

        LoadPlaces();
        UpdateActiveBreadcrumbs();
    }

    public void ConfirmCurrentPicker()
    {
        if (!IsPickerMode)
        {
            return;
        }

        if (PickerOptions?.AllowMultiple == true)
        {
            List<FileItemViewModel> targets = SelectedItems.Count > 0
                ? SelectedItems.ToList()
                : (SelectedItem != null ? new List<FileItemViewModel> { SelectedItem } : new List<FileItemViewModel>());

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
            if (SelectedItem != null && !SelectedItem.IsDirectory)
            {
                PickerCompleted?.Invoke(FilePickerResult.Selected(SelectedItem.FullPath));
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
            if (SelectedItem != null && SelectedItem.IsDirectory)
            {
                PickerCompleted?.Invoke(FilePickerResult.Selected(SelectedItem.FullPath));
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
                NavigateTo(item.FullPath);
                return;
            }

            if (PickerMode == PickerMode.File)
            {
                PickerCompleted?.Invoke(FilePickerResult.Selected(item.FullPath));
                return;
            }

            return;
        }

        Logger.LogI(item.IsDirectory ? "Opened directory" : "Opened file");

        if (item.IsDirectory)
        {
            if (ActivePane == 2)
            {
                NavigatePane2To(item.FullPath);
            }
            else
            {
                NavigateTo(item.FullPath);
            }

            return;
        }

        string ext = Path.GetExtension(item.FullPath).ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext) && _associations.TryGetValue(ext, out string? associatedProgram))
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = associatedProgram,
                    Arguments = $"\"{item.FullPath}\"",
                    UseShellExecute = false,
                };
                if (OperatingSystem.IsLinux())
                {
                    psi.EnvironmentVariables["DESKTOP_STARTUP_ID"] = $"ferret_{Environment.ProcessId}_TIME{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                }

                Process.Start(psi);
                return;
            }
            catch { }
        }

        _executionService.Execute(item.Entry);
    }

    public void OpenWith(FileItemViewModel item, string program, bool always)
    {
        if (always)
        {
            string ext = Path.GetExtension(item.FullPath).ToLowerInvariant();
            if (!string.IsNullOrEmpty(ext))
            {
                _associations[ext] = program;
                SaveAssociations();
            }

            if (OperatingSystem.IsLinux())
            {
                Task.Run(() => MimeService.SetDefaultApplication(item.FullPath, program));
            }
        }

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = program,
                Arguments = $"\"{item.FullPath}\"",
                UseShellExecute = false,
            };
            if (OperatingSystem.IsLinux())
            {
                psi.EnvironmentVariables["DESKTOP_STARTUP_ID"] = $"ferret_{Environment.ProcessId}_TIME{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            }

            Process.Start(psi);
        }
        catch { }
    }

    public void CopySelected(IEnumerable<FileItemViewModel>? selection = null)
    {
        List<FileItemViewModel> targets = (selection ?? SelectedItems).ToList();
        if (targets.Count == 0 && SelectedItem != null)
        {
            targets.Add(SelectedItem);
        }

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

        foreach (FileItemViewModel item in Items)
        {
            item.IsCut = false;
        }

        foreach (FileItemViewModel item in Pane2Items)
        {
            item.IsCut = false;
        }
    }

    public void CutSelected(IEnumerable<FileItemViewModel>? selection = null)
    {
        List<FileItemViewModel> targets = (selection ?? SelectedItems).ToList();
        if (targets.Count == 0 && SelectedItem != null)
        {
            targets.Add(SelectedItem);
        }

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

        foreach (FileItemViewModel item in Items)
        {
            item.IsCut = paths.Contains(item.FullPath);
        }

        foreach (FileItemViewModel item in Pane2Items)
        {
            item.IsCut = paths.Contains(item.FullPath);
        }
    }

    public void Paste(string? targetDirectory = null)
    {
        string dir = targetDirectory ?? (ActivePane == 2 ? Pane2Path : CurrentPath);
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

    public void DeleteSelected(IEnumerable<FileItemViewModel>? selection = null, bool permanent = false)
    {
        List<FileItemViewModel> targets = (selection ?? (ActivePane == 2 ? Pane2SelectedItems : SelectedItems)).ToList();
        FileItemViewModel? activeSelected = ActivePane == 2 ? Pane2SelectedItem : SelectedItem;
        if (targets.Count == 0 && activeSelected != null)
        {
            targets.Add(activeSelected);
        }

        if (targets.Count == 0)
        {
            return;
        }

        List<string> paths = targets.Select(t => t.FullPath).ToList();
        _operationService.Delete(paths, permanent);
        Refresh();
    }

    public void RenameSelected(FileItemViewModel item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || item.Name == newName)
        {
            return;
        }

        _operationService.Rename(item.FullPath, newName);
        Refresh();
    }

    public void DuplicateSelected(IEnumerable<FileItemViewModel>? selection = null)
    {
        List<FileItemViewModel> targets = (selection ?? (ActivePane == 2 ? Pane2SelectedItems : SelectedItems)).ToList();
        FileItemViewModel? activeSelected = ActivePane == 2 ? Pane2SelectedItem : SelectedItem;
        if (targets.Count == 0 && activeSelected != null)
        {
            targets.Add(activeSelected);
        }

        if (targets.Count == 0)
        {
            return;
        }

        foreach (FileItemViewModel item in targets)
        {
            _operationService.Duplicate(item.FullPath);
        }

        Refresh();
    }

    public void CreateFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        string baseDir = ActivePane == 2 ? Pane2Path : CurrentPath;
        string fullPath = Path.Combine(baseDir, folderName);
        _operationService.CreateFolder(fullPath);
        Refresh();
    }

    public void CreateFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        string baseDir = ActivePane == 2 ? Pane2Path : CurrentPath;
        string fullPath = Path.Combine(baseDir, fileName);
        _operationService.CreateFile(fullPath);
        Refresh();
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
        string path = targetPath ?? (ActivePane == 2 ? Pane2Path : CurrentPath);
        Logger.LogI("Opened directory as root");
        FileExecutionService.OpenAsRoot(path);
    }

    public void OpenTerminal(string? workingDir = null, string? command = null)
    {
        string dir = workingDir ?? (ActivePane == 2 ? Pane2Path : CurrentPath);
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

    private void UpdateBreadcrumbs(string path, ObservableCollection<BreadcrumbItem> targetList)
    {
        targetList.Clear();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path == "/")
        {
            targetList.Add(new BreadcrumbItem { Name = LocalizationService.Get("Root"), FullPath = "/", IsLast = true });
            return;
        }

        if (path.Equals(userProfile, StringComparison.OrdinalIgnoreCase))
        {
            targetList.Add(new BreadcrumbItem { Name = LocalizationService.Get("Home"), FullPath = userProfile, IsLast = true });
            return;
        }

        if (path.StartsWith(userProfile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            targetList.Add(new BreadcrumbItem { Name = LocalizationService.Get("Home"), FullPath = userProfile, IsLast = false });
            string relative = path.Substring(userProfile.Length).TrimStart(Path.DirectorySeparatorChar);
            string[] parts = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            string accum = userProfile;

            for (int i = 0; i < parts.Length; i++)
            {
                accum = Path.Combine(accum, parts[i]);
                targetList.Add(new BreadcrumbItem
                {
                    Name = parts[i],
                    FullPath = accum,
                    IsLast = i == parts.Length - 1,
                });
            }

            return;
        }

        targetList.Add(new BreadcrumbItem { Name = LocalizationService.Get("Root"), FullPath = "/", IsLast = false });
        string[] rootParts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        string currentAccum = "";

        for (int i = 0; i < rootParts.Length; i++)
        {
            currentAccum += "/" + rootParts[i];
            targetList.Add(new BreadcrumbItem
            {
                Name = rootParts[i],
                FullPath = currentAccum,
                IsLast = i == rootParts.Length - 1,
            });
        }
    }

    private void UpdatePreview(FileItemViewModel? selected)
    {
        PreviewImage?.Dispose();
        PreviewImage = null;

        if (selected == null)
        {
            PreviewDetails = "";
            return;
        }

        string details = $"{LocalizationService.Get("Name")}: {selected.Name}\n{LocalizationService.Get("Type")}: {selected.TypeString}\n{LocalizationService.Get("Size")}: {selected.SizeString}\n{LocalizationService.Get("Modified")}: {selected.ModifiedString}\n{LocalizationService.Get("LocationLabel")}: {selected.FullPath}";
        if (selected.IsAccessDenied)
        {
            details += $"\n{LocalizationService.Get("AccessDenied")}";
        }

        PreviewDetails = details;

        if (!selected.IsDirectory && (selected.Entry as FileEntry)?.FileType == FileType.Image)
        {
            try
            {
                PreviewImage = new Bitmap(selected.FullPath);
            }
            catch
            {
                PreviewImage = null;
            }
        }
    }

    public void SetSelectedItems(IEnumerable<FileItemViewModel> selected)
    {
        ObservableCollection<FileItemViewModel> target = ActivePane == 2 ? Pane2SelectedItems : SelectedItems;
        target.Clear();
        foreach (FileItemViewModel item in selected)
        {
            target.Add(item);
        }

        UpdateStatus();
        NotifySelectionProperties();
    }

    public void ClearSelection()
    {
        if (ActivePane == 2)
        {
            Pane2SelectedItem = null;
            Pane2SelectedItems.Clear();
        }
        else
        {
            SelectedItem = null;
            SelectedItems.Clear();
        }

        UpdateStatus();
        NotifySelectionProperties();
    }

    private void UpdateStatus()
    {
        int totalItems = ActivePane == 2 ? Pane2Items.Count : Items.Count;
        ObservableCollection<FileItemViewModel> selectedList = ActivePane == 2 ? Pane2SelectedItems : SelectedItems;
        int selectedCount = selectedList.Count;
        FileItemViewModel? activeSelected = ActivePane == 2 ? Pane2SelectedItem : SelectedItem;

        if (selectedCount == 0 && activeSelected != null)
        {
            selectedCount = 1;
        }

        long selectedBytes = 0;
        int filesWithBytes = 0;
        if (selectedList.Count > 0)
        {
            foreach (FileItemViewModel item in selectedList)
            {
                if (item.Entry is FileEntry file)
                {
                    selectedBytes += file.FileSize;
                    filesWithBytes++;
                }
            }
        }
        else if (activeSelected?.Entry is FileEntry file)
        {
            selectedBytes = file.FileSize;
            filesWithBytes = 1;
        }

        string activePath = ActivePane == 2 ? Pane2Path : CurrentPath;
        string freeSpace = GetFreeDiskSpace(activePath);
        string freeStr = !string.IsNullOrEmpty(freeSpace) ? $" | {freeSpace} {LocalizationService.Get("FreeSpace")}" : "";

        if (selectedCount > 0)
        {
            string sizePart = filesWithBytes > 0 ? $", {FileUtils.FormatSize(selectedBytes)}" : "";
            StatusText = $"{totalItems} {LocalizationService.Get("ItemsCount")} ({selectedCount} {LocalizationService.Get("SelectedCount")}{sizePart}){freeStr}";
        }
        else
        {
            StatusText = $"{totalItems} {LocalizationService.Get("ItemsCount")}{freeStr}";
        }
    }

    private void LoadAssociations()
    {
        if (!File.Exists(_associationsPath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_associationsPath);
            Dictionary<string, string>? data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data != null)
            {
                foreach (KeyValuePair<string, string> kvp in data)
                {
                    _associations[kvp.Key] = kvp.Value;
                }
            }
        }
        catch { }
    }

    private void SaveAssociations()
    {
        try
        {
            string json = JsonSerializer.Serialize(_associations, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_associationsPath, json);
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

            return "Android Device";
        }

        if (rawName.StartsWith("afc:host=", StringComparison.OrdinalIgnoreCase))
        {
            return "Apple Device";
        }

        if (rawName.StartsWith("gphoto2:host=", StringComparison.OrdinalIgnoreCase))
        {
            return "Digital Camera";
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

    private static string GetFreeDiskSpace(string path)
    {
        try
        {
            string root = Path.GetPathRoot(path) ?? "/";
            DriveInfo drive = new(root);
            return FormatBytes(drive.AvailableFreeSpace);
        }
        catch
        {
            return "";
        }
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

    private void UpdateWatcher1(string path)
    {
        try
        {
            if (_watcher1 == null)
            {
                _watcher1 = new()
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                _watcher1.Created += (s, e) => OnWatcherEvent1(e);
                _watcher1.Deleted += (s, e) => OnWatcherEvent1(e);
                _watcher1.Renamed += (s, e) => OnWatcherEvent1(e);
                _watcher1.Changed += (s, e) => OnWatcherEvent1(e);
                _watcher1.Error += (s, e) => OnWatcherError1();
            }

            if (Directory.Exists(path))
            {
                _watcher1.EnableRaisingEvents = false;
                _watcher1.Path = path;
                _watcher1.EnableRaisingEvents = true;
            }
            else
            {
                _watcher1.EnableRaisingEvents = false;
            }
        }
        catch
        {
            if (_watcher1 != null)
            {
                _watcher1.EnableRaisingEvents = false;
            }
        }
    }

    private void UpdateWatcher2(string path)
    {
        try
        {
            if (_watcher2 == null)
            {
                _watcher2 = new()
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                _watcher2.Created += (s, e) => OnWatcherEvent2(e);
                _watcher2.Deleted += (s, e) => OnWatcherEvent2(e);
                _watcher2.Renamed += (s, e) => OnWatcherEvent2(e);
                _watcher2.Changed += (s, e) => OnWatcherEvent2(e);
                _watcher2.Error += (s, e) => OnWatcherError2();
            }

            if (Directory.Exists(path))
            {
                _watcher2.EnableRaisingEvents = false;
                _watcher2.Path = path;
                _watcher2.EnableRaisingEvents = true;
            }
            else
            {
                _watcher2.EnableRaisingEvents = false;
            }
        }
        catch
        {
            if (_watcher2 != null)
            {
                _watcher2.EnableRaisingEvents = false;
            }
        }
    }

    private void OnWatcherEvent1(FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Deleted)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FileItemViewModel? item = Items.FirstOrDefault(i => string.Equals(i.FullPath, e.FullPath, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Name, e.Name, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    Items.Remove(item);
                    if (SelectedItem == item)
                    {
                        SelectedItem = null;
                    }

                    if (SelectedItems.Contains(item))
                    {
                        SelectedItems.Remove(item);
                    }

                    if (ActivePane == 1)
                    {
                        UpdateStatus();
                    }
                }
            });
        }

        Dispatcher.UIThread.Post(() =>
        {
            _debounceTimer1?.Stop();
            _debounceTimer1 = new()
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _debounceTimer1.Tick += (s, args) =>
            {
                _debounceTimer1.Stop();
                RefreshPane1Silent();
            };
            _debounceTimer1.Start();
        });
    }

    private void OnWatcherError1()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _debounceTimer1?.Stop();
            _debounceTimer1 = new()
            {
                Interval = TimeSpan.FromMilliseconds(300),
            };
            _debounceTimer1.Tick += (s, args) =>
            {
                _debounceTimer1.Stop();
                RefreshPane1Silent();
            };
            _debounceTimer1.Start();
        });
    }

    private void OnWatcherEvent2(FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Deleted)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FileItemViewModel? item = Pane2Items.FirstOrDefault(i => string.Equals(i.FullPath, e.FullPath, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Name, e.Name, StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    Pane2Items.Remove(item);
                    if (Pane2SelectedItem == item)
                    {
                        Pane2SelectedItem = null;
                    }

                    if (Pane2SelectedItems.Contains(item))
                    {
                        Pane2SelectedItems.Remove(item);
                    }

                    if (ActivePane == 2)
                    {
                        UpdateStatus();
                    }
                }
            });
        }

        Dispatcher.UIThread.Post(() =>
        {
            _debounceTimer2?.Stop();
            _debounceTimer2 = new()
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _debounceTimer2.Tick += (s, args) =>
            {
                _debounceTimer2.Stop();
                RefreshPane2Silent();
            };
            _debounceTimer2.Start();
        });
    }

    private void OnWatcherError2()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _debounceTimer2?.Stop();
            _debounceTimer2 = new()
            {
                Interval = TimeSpan.FromMilliseconds(300),
            };
            _debounceTimer2.Tick += (s, args) =>
            {
                _debounceTimer2.Stop();
                RefreshPane2Silent();
            };
            _debounceTimer2.Start();
        });
    }

    private void RefreshPane1Silent()
    {
        if (string.IsNullOrEmpty(_pane1Path) || !Directory.Exists(_pane1Path))
        {
            return;
        }

        string path = _pane1Path;
        string? search = ActivePane == 1 ? (string.IsNullOrEmpty(SearchQuery) ? null : SearchQuery) : null;
        bool showHidden = ShowHiddenFiles;

        Task.Run(() =>
        {
            ClipboardMode clipMode = ClipboardMode.Copy;
            string[] clipPaths = [];
            try
            {
                ClipboardService.ReadPaths(out clipMode, out clipPaths);
            }
            catch
            {
            }

            NameComparer comparer = new();
            List<FileItemViewModel> allItems = new();
            try
            {
                foreach (IFileSystemEntry entry in _fileService.EnumerateFolderEntries(path, search, CancellationToken.None))
                {
                    if (!showHidden && entry.IsHidden)
                    {
                        continue;
                    }

                    FileItemViewModel item = FileItemViewModel.FromEntry(entry);
                    if (clipMode == ClipboardMode.Cut && clipPaths.Contains(entry.FullPath))
                    {
                        item.IsCut = true;
                    }

                    allItems.Add(item);
                }
            }
            catch
            {
                return;
            }

            allItems.Sort((a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : comparer.Compare(a.Name, b.Name));

            Dispatcher.UIThread.Post(() =>
            {
                if (_pane1Path != path)
                {
                    return;
                }

                SyncFolderItems(Items, allItems);
                if (ActivePane == 1)
                {
                    UpdateStatus();
                }
            });
        });
    }

    private void RefreshPane2Silent()
    {
        if (string.IsNullOrEmpty(_pane2Path) || !Directory.Exists(_pane2Path))
        {
            return;
        }

        string path = _pane2Path;
        string? search = ActivePane == 2 ? (string.IsNullOrEmpty(SearchQuery) ? null : SearchQuery) : null;
        bool showHidden = ShowHiddenFiles;

        Task.Run(() =>
        {
            ClipboardMode clipMode = ClipboardMode.Copy;
            string[] clipPaths = [];
            try
            {
                ClipboardService.ReadPaths(out clipMode, out clipPaths);
            }
            catch
            {
            }

            NameComparer comparer = new();
            List<FileItemViewModel> allItems = new();
            try
            {
                foreach (IFileSystemEntry entry in _fileService.EnumerateFolderEntries(path, search, CancellationToken.None))
                {
                    if (!showHidden && entry.IsHidden)
                    {
                        continue;
                    }

                    FileItemViewModel item = FileItemViewModel.FromEntry(entry);
                    if (clipMode == ClipboardMode.Cut && clipPaths.Contains(entry.FullPath))
                    {
                        item.IsCut = true;
                    }

                    allItems.Add(item);
                }
            }
            catch
            {
                return;
            }

            allItems.Sort((a, b) => a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1) : comparer.Compare(a.Name, b.Name));

            Dispatcher.UIThread.Post(() =>
            {
                if (_pane2Path != path)
                {
                    return;
                }

                SyncFolderItems(Pane2Items, allItems);
                if (ActivePane == 2)
                {
                    UpdateStatus();
                }
            });
        });
    }

    private void SyncFolderItems(ObservableCollection<FileItemViewModel> currentItems, List<FileItemViewModel> freshItems)
    {
        Dictionary<string, FileItemViewModel> freshMap = new(StringComparer.OrdinalIgnoreCase);
        foreach (FileItemViewModel item in freshItems)
        {
            freshMap[item.FullPath] = item;
        }

        for (int i = currentItems.Count - 1; i >= 0; i--)
        {
            if (!freshMap.ContainsKey(currentItems[i].FullPath))
            {
                currentItems.RemoveAt(i);
            }
        }

        for (int i = 0; i < freshItems.Count; i++)
        {
            FileItemViewModel fresh = freshItems[i];
            if (i >= currentItems.Count)
            {
                currentItems.Add(fresh);
            }
            else if (!string.Equals(currentItems[i].FullPath, fresh.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                int existingIdx = -1;
                for (int j = i + 1; j < currentItems.Count; j++)
                {
                    if (string.Equals(currentItems[j].FullPath, fresh.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx != -1)
                {
                    currentItems.Move(existingIdx, i);
                }
                else
                {
                    currentItems.Insert(i, fresh);
                }
            }
            else if (currentItems[i].SizeString != fresh.SizeString || currentItems[i].ModifiedString != fresh.ModifiedString)
            {
                currentItems[i] = fresh;
            }
        }
    }

    public void Dispose()
    {
        _reloadPlacesCts?.Dispose();
        foreach (IDisposable w in _deviceWatchers)
        {
            w.Dispose();
        }
        _deviceWatchers.Clear();

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

        _watcher1?.Dispose();
        _watcher1 = null;
        _watcher2?.Dispose();
        _watcher2 = null;
        _debounceTimer1?.Stop();
        _debounceTimer2?.Stop();
        _loadCts?.Dispose();
        _loadPane2Cts?.Dispose();
        _operationCts?.Dispose();
        PreviewImage?.Dispose();
    }
}
