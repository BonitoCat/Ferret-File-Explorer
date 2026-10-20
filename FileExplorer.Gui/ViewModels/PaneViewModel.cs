using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Services;
using FileExplorer.Core.States;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Models;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.ViewModels;

public class PaneViewModel : ViewModelBase, IDisposable
{
    private readonly FileSystemService _fileService;
    private readonly NetworkBrowseService _networkBrowser = new();
    private readonly Func<bool> _showHiddenFiles;

    private NavigationState? _navigation;
    private string _currentPath = "";
    private string? _searchPattern;
    private FileItemViewModel? _selectedItem;
    private bool _isActive;
    private bool _isLoading;
    private bool _isExternalLoading;
    private CancellationTokenSource? _loadCts;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounceTimer;

    public event Action<PaneViewModel>? BeforeLoad;
    public event Action<PaneViewModel>? ContentChanged;
    public event Action<PaneViewModel>? SelectionChanged;
    public event Action<PaneViewModel, string>? LoadFailed;
    public event Action<PaneViewModel, string, bool>? LoginRequired;

    public PaneViewModel(FileSystemService fileService, Func<bool> showHiddenFiles)
    {
        _fileService = fileService;
        _showHiddenFiles = showHiddenFiles;
    }

    public Func<string, bool>? FileFilter { get; set; }

    public BulkObservableCollection<FileItemViewModel> Items { get; } = new();
    public ObservableCollection<FileItemViewModel> SelectedItems { get; } = new();

    public string? BreadcrumbTrail { get; set; }

    public string CurrentPath => _currentPath;

    public bool IsOpen => !string.IsNullOrEmpty(_currentPath);

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowLoadingBar));
            }
        }
    }

    public bool IsExternalLoading
    {
        get => _isExternalLoading;
        set
        {
            if (SetProperty(ref _isExternalLoading, value))
            {
                OnPropertyChanged(nameof(ShowLoadingBar));
            }
        }
    }

    public bool ShowLoadingBar => IsLoading || IsExternalLoading;

    public bool IsInTrash => IsOpen && _currentPath.Contains("/.local/share/Trash");
    public bool IsVirtual => NetworkBrowseService.IsBrowseLocation(_currentPath);
    public bool CanGoBack => _navigation?.CanGoBack ?? false;
    public bool CanGoForward => _navigation?.CanGoForward ?? false;
    public bool CanGoUp => IsOpen && (IsVirtual ? NetworkBrowseService.GetParent(_currentPath) != null : Path.GetDirectoryName(_currentPath) != null);

    public static bool IsNavigable(string? path)
    {
        return NetworkBrowseService.IsBrowseLocation(path) || Directory.Exists(path);
    }

    public FileItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value))
            {
                return;
            }

            NotifySelectionProperties();
            SelectionChanged?.Invoke(this);
        }
    }

    public bool IsItemSelected => SelectedItem != null;
    public bool IsFolderSelected => SelectedItem?.IsDirectory == true;
    public bool IsFileSelected => IsItemSelected && !IsFolderSelected;
    public bool IsEmptySpaceSelected => !IsItemSelected;
    public bool CanBookmarkSelection => IsFolderSelected || IsEmptySpaceSelected;
    public bool CanCopyPath => true;
    public bool CanCreateNew => IsEmptySpaceSelected || IsFolderSelected;
    public bool CanColorSelection => IsFolderSelected;
    public bool CanExtractSelection => SelectedItem is FileItemViewModel item && (item.FileType == FileType.Archive || MainWindowViewModel.IsArchiveFile(item.FullPath));

    private void NotifySelectionProperties()
    {
        OnPropertyChanged(nameof(IsItemSelected));
        OnPropertyChanged(nameof(IsFolderSelected));
        OnPropertyChanged(nameof(IsFileSelected));
        OnPropertyChanged(nameof(IsEmptySpaceSelected));
        OnPropertyChanged(nameof(CanBookmarkSelection));
        OnPropertyChanged(nameof(CanCopyPath));
        OnPropertyChanged(nameof(CanCreateNew));
        OnPropertyChanged(nameof(CanColorSelection));
        OnPropertyChanged(nameof(CanExtractSelection));
    }

    private void NotifyPathProperties()
    {
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(IsInTrash));
        OnPropertyChanged(nameof(IsVirtual));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
    }

    public List<FileItemViewModel> GetSelection()
    {
        if (SelectedItems.Count > 0)
        {
            return SelectedItems.ToList();
        }

        return SelectedItem != null ? [SelectedItem] : [];
    }

    public void SetSelection(IEnumerable<FileItemViewModel> selected, FileItemViewModel? primary)
    {
        SelectedItems.Clear();
        foreach (FileItemViewModel item in selected)
        {
            SelectedItems.Add(item);
        }

        SelectedItem = primary ?? SelectedItems.FirstOrDefault();
        SelectionChanged?.Invoke(this);
    }

    public void ClearSelection()
    {
        SelectedItems.Clear();
        SelectedItem = null;
        SelectionChanged?.Invoke(this);
    }

    public void NavigateTo(string path)
    {
        if (string.IsNullOrEmpty(path) || !IsNavigable(path))
        {
            return;
        }

        _navigation ??= new(path);
        _navigation.NavigateTo(path);
        Load(_navigation.CurrentPath);
    }

    public void GoBack()
    {
        if (_navigation == null || !_navigation.CanGoBack)
        {
            return;
        }

        _navigation.GoBack();
        Load(_navigation.CurrentPath);
    }

    public void GoForward()
    {
        if (_navigation == null || !_navigation.CanGoForward)
        {
            return;
        }

        _navigation.GoForward();
        Load(_navigation.CurrentPath);
    }

    public void GoUp()
    {
        if (IsVirtual)
        {
            string? virtualParent = NetworkBrowseService.GetParent(_currentPath);
            if (virtualParent != null)
            {
                NavigateTo(virtualParent);
            }

            return;
        }

        string? parent = Path.GetDirectoryName(_currentPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            return;
        }

        NavigateTo(parent);
    }

    public void Reload(string? searchPattern = null)
    {
        Load(_currentPath, searchPattern);
    }

    public void Refresh(string? searchPattern = null)
    {
        string? normalized = string.IsNullOrEmpty(searchPattern) ? null : searchPattern;
        string? current = string.IsNullOrEmpty(_searchPattern) ? null : _searchPattern;
        if (IsLoading || normalized != current || Items.Count == 0 || IsVirtual)
        {
            Load(_currentPath, searchPattern);
            return;
        }

        _debounceTimer?.Stop();
        RefreshSilent();
    }

    public void TakeOver(PaneViewModel other)
    {
        NavigationState? navigation = other._navigation;
        if (navigation == null)
        {
            return;
        }

        string? trail = other.BreadcrumbTrail;
        other.Close();

        _navigation = navigation;
        BreadcrumbTrail = trail;
        Load(navigation.CurrentPath);
    }

    public void Close()
    {
        _loadCts?.Cancel();
        _debounceTimer?.Stop();
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
        }

        _navigation = null;
        _currentPath = "";
        _searchPattern = null;
        BreadcrumbTrail = null;

        BeforeLoad?.Invoke(this);
        ClearSelection();
        Items.Clear();
        IsLoading = false;
        NotifyPathProperties();
    }

    public void Load(string path, string? searchPattern = null)
    {
        if (string.IsNullOrEmpty(path) || !IsNavigable(path))
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        CancellationTokenSource cts = new();
        _loadCts = cts;
        CancellationToken token = cts.Token;

        _currentPath = path;
        _searchPattern = searchPattern;
        UpdateWatcher(path);

        BeforeLoad?.Invoke(this);
        ClearSelection();
        Items.Clear();
        IsLoading = true;
        NotifyPathProperties();

        if (NetworkBrowseService.IsBrowseLocation(path))
        {
            LoadNetworkLocation(path, searchPattern, cts);
            return;
        }

        bool showHidden = _showHiddenFiles();
        Func<string, bool>? fileFilter = FileFilter;
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

                    if (fileFilter != null && entry is FileEntry && !fileFilter(entry.FullName))
                    {
                        continue;
                    }

                    FileItemViewModel item = FileItemViewModel.FromEntry(entry, token);
                    if (clipMode == ClipboardMode.Cut && clipPaths.Contains(entry.FullPath))
                    {
                        item.IsCut = true;
                    }

                    batch.Add(item);

                    if (batch.Count >= batchThreshold || stopwatch.ElapsedMilliseconds >= 60)
                    {
                        PostBatch(batch, token);
                        batch = new();
                        if (batchThreshold < 120)
                        {
                            batchThreshold = Math.Min(batchThreshold * 2, 120);
                        }

                        stopwatch.Restart();
                        Thread.Sleep(10);
                    }
                }

                if (batch.Count > 0)
                {
                    PostBatch(batch, token);
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
                    if (_loadCts != cts)
                    {
                        return;
                    }

                    IsLoading = false;
                    ContentChanged?.Invoke(this);
                }, DispatcherPriority.Background);
            }
        }, token);
    }

    private void LoadNetworkLocation(string path, string? searchPattern, CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;
        string filter = (searchPattern ?? "").Trim('*');
        Task.Run(async () =>
        {
            List<FileItemViewModel> items = new();
            string? error;
            bool needsLogin = false;
            bool loginRejected = false;
            try
            {
                NetworkBrowseResult result = await _networkBrowser.BrowseAsync(path, token);
                error = result.Error;
                needsLogin = result.NeedsCredentials;
                loginRejected = result.CredentialsRejected;
                items = result.Entries
                    .Where(e => filter.Length == 0 || e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .Select(CreateNetworkItem)
                    .ToList();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_loadCts != cts)
                {
                    return;
                }

                Items.AddRange(items);
                IsLoading = false;
                ContentChanged?.Invoke(this);

                if (needsLogin && LoginRequired != null)
                {
                    LoginRequired.Invoke(this, path, loginRejected);
                    return;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    LoadFailed?.Invoke(this, error);
                }
            }, DispatcherPriority.Background);
        }, token);
    }

    private static FileItemViewModel CreateNetworkItem(NetworkBrowseEntry entry)
    {
        bool isContainer = entry.Kind == NetworkBrowseKind.Container;
        return new()
        {
            Entry = new FolderEntry { FullName = entry.Name, FullPath = entry.Uri },
            Name = entry.Name,
            FullPath = entry.Uri,
            IsDirectory = true,
            SizeString = "",
            TypeString = LocalizationService.Get(isContainer ? "NetworkServer" : "NetworkShare"),
            ModifiedString = "",
            IsHidden = false,
            IsAccessDenied = false,
            Icon = IconService.Get(isContainer ? "network" : "folder"),
        };
    }

    private void PostBatch(List<FileItemViewModel> batch, CancellationToken token)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            Items.AddRange(batch);
            ContentChanged?.Invoke(this);
        }, DispatcherPriority.Background);
    }

    private void RefreshSilent()
    {
        if (!IsOpen)
        {
            return;
        }

        string path = _currentPath;
        string? search = string.IsNullOrEmpty(_searchPattern) ? null : _searchPattern;
        bool showHidden = _showHiddenFiles();
        Func<string, bool>? fileFilter = FileFilter;

        Task.Run(() =>
        {
            if (!Directory.Exists(path))
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

                    if (fileFilter != null && entry is FileEntry && !fileFilter(entry.FullName))
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
                if (_currentPath != path)
                {
                    return;
                }

                SyncFolderItems(Items, allItems);

                HashSet<FileItemViewModel> remaining = new(Items);
                for (int i = SelectedItems.Count - 1; i >= 0; i--)
                {
                    if (!remaining.Contains(SelectedItems[i]))
                    {
                        SelectedItems.RemoveAt(i);
                    }
                }

                if (SelectedItem != null && !remaining.Contains(SelectedItem))
                {
                    SelectedItem = null;
                }

                ContentChanged?.Invoke(this);
            });
        });
    }

    private static void SyncFolderItems(ObservableCollection<FileItemViewModel> currentItems, List<FileItemViewModel> freshItems)
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
            else if (currentItems[i].IsCut != fresh.IsCut)
            {
                currentItems[i].IsCut = fresh.IsCut;
            }
        }
    }

    private void UpdateWatcher(string path)
    {
        try
        {
            if (_watcher == null)
            {
                _watcher = new()
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };
                _watcher.Created += (s, e) => OnWatcherEvent(e);
                _watcher.Deleted += (s, e) => OnWatcherEvent(e);
                _watcher.Renamed += (s, e) => OnWatcherEvent(e);
                _watcher.Changed += (s, e) => OnWatcherEvent(e);
                _watcher.Error += (s, e) => ScheduleSilentRefresh(300);
            }

            _watcher.EnableRaisingEvents = false;
            if (Directory.Exists(path))
            {
                _watcher.Path = path;
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
            }
        }
    }

    private void OnWatcherEvent(FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Deleted)
        {
            Dispatcher.UIThread.Post(() => RemoveDeletedItem(e));
        }

        ScheduleSilentRefresh(200);
    }

    private void RemoveDeletedItem(FileSystemEventArgs e)
    {
        FileItemViewModel? item = Items.FirstOrDefault(i => string.Equals(i.FullPath, e.FullPath, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Name, e.Name, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            return;
        }

        Items.Remove(item);
        SelectedItems.Remove(item);
        if (SelectedItem == item)
        {
            SelectedItem = null;
        }

        ContentChanged?.Invoke(this);
    }

    private void ScheduleSilentRefresh(int delayMs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _debounceTimer?.Stop();
            _debounceTimer = new()
            {
                Interval = TimeSpan.FromMilliseconds(delayMs),
            };
            _debounceTimer.Tick += (s, args) =>
            {
                _debounceTimer.Stop();
                RefreshSilent();
            };
            _debounceTimer.Start();
        });
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounceTimer?.Stop();
        _loadCts?.Dispose();
    }
}
