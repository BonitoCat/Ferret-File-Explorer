using System.IO.Compression;
using System.Text.Json;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Addons;

public sealed class AddonInstallException : Exception
{
    public string Key { get; }
    public string Detail { get; }

    public AddonInstallException(string key, string detail) : base($"{key}: {detail}")
    {
        Key = key;
        Detail = detail;
    }
}

public sealed class AddonService
{
    private sealed class StateData
    {
        public List<string> Order { get; set; } = new();
        public List<string> Disabled { get; set; } = new();
    }

    private const int WatchDebounceMs = 300;

    private static readonly Lazy<AddonService> LazyInstance = new(() => new());

    public static AddonService Instance => LazyInstance.Value;

    private readonly object _lock = new();
    private readonly string _statePath;
    private StateData _state = new();
    private List<Addon> _addons = new();
    private FileSystemWatcher? _watcher;
    private readonly Timer _reloadTimer;

    public event Action? AddonsChanged;

    public static string AddonsDirectory => AppPaths.GetDataDir("addons");

    public IReadOnlyList<Addon> Addons
    {
        get
        {
            lock (_lock)
            {
                return _addons.ToList();
            }
        }
    }

    public AddonService()
    {
        _statePath = AppPaths.GetDataPath("addons.json");
        _reloadTimer = new(_ => Reload());
        Reload();
        StartWatching();
    }

    public void Reload()
    {
        lock (_lock)
        {
            _state = ReadState() ?? new();
            _addons = Discover();
            ApplyState();
        }

        AddonsChanged?.Invoke();
    }

    public Addon? Find(string id)
    {
        lock (_lock)
        {
            return _addons.FirstOrDefault(a => a.Id == id);
        }
    }

    public List<AddonAction> GetActions(AddonLocation location)
    {
        lock (_lock)
        {
            return _addons
                .Where(a => a.IsActive)
                .SelectMany(a => a.Actions)
                .Where(a => a.Location == location)
                .ToList();
        }
    }

    public List<AddonAction> GetActions(AddonLocation location, AddonMode mode)
    {
        return GetActions(location)
            .Where(a => a.SupportsMode(mode))
            .ToList();
    }

    public List<AddonAction> GetActions(AddonLocation location, AddonContext context)
    {
        return GetActions(location)
            .Where(a => a.Matches(context))
            .ToList();
    }

    public AddonAction? FindPathBarAction(string keyword)
    {
        return FindPathBarAction(keyword, AddonMode.All);
    }

    public AddonAction? FindPathBarAction(string keyword, AddonMode mode)
    {
        return GetActions(AddonLocation.PathBar, mode)
            .FirstOrDefault(a => a.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase));
    }

    public List<Addon> GetHookAddons(AddonHook hook, AddonMode mode)
    {
        lock (_lock)
        {
            return _addons
                .Where(a => a.IsActive && a.SupportsMode(mode) && a.Hooks.ContainsKey(hook))
                .ToList();
        }
    }

    public List<AddonAction> GetShortcutActions()
    {
        lock (_lock)
        {
            return _addons
                .Where(a => a.IsActive)
                .SelectMany(a => a.Actions)
                .Where(a => a.Shortcut != null && !a.IsGroup && a.Location != AddonLocation.Paste)
                .ToList();
        }
    }

    public void SetOrder(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            List<string> order = ids.Distinct().ToList();
            order.AddRange(_state.Order.Where(id => !order.Contains(id)));
            _state.Order = order;
            ApplyState();
            SaveState();
        }

        AddonsChanged?.Invoke();
    }

    public void Move(string id, int offset)
    {
        List<string> ids = Addons.Select(a => a.Id).ToList();
        int index = ids.IndexOf(id);
        int target = index + offset;
        if (index < 0 || target < 0 || target >= ids.Count)
        {
            return;
        }

        ids.RemoveAt(index);
        ids.Insert(target, id);
        SetOrder(ids);
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_lock)
        {
            _state.Disabled.Remove(id);
            if (!enabled)
            {
                _state.Disabled.Add(id);
            }

            ApplyState();
            SaveState();
        }

        AddonsChanged?.Invoke();
    }

    public string GetInstallId(string sourcePath)
    {
        string root = LocateAddonRoot(sourcePath, out string? tempDir);
        try
        {
            return ReadInstallId(root);
        }
        finally
        {
            DeleteTemp(tempDir);
        }
    }

    public bool IsInstalled(string id)
    {
        return Directory.Exists(Path.Combine(AddonsDirectory, id));
    }

    public Addon Install(string sourcePath, bool overwrite)
    {
        string root = LocateAddonRoot(sourcePath, out string? tempDir);
        string target;
        try
        {
            string id = ReadInstallId(root);
            target = Path.Combine(AddonsDirectory, id);
            if (Path.GetFullPath(root) == Path.GetFullPath(target))
            {
                throw new AddonInstallException("AddonErrorAlreadyInAddonsFolder", id);
            }

            if (Directory.Exists(target))
            {
                if (!overwrite)
                {
                    throw new AddonInstallException("AddonErrorAlreadyInstalled", id);
                }

                Directory.Delete(target, true);
            }

            CopyDirectory(root, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new AddonInstallException("AddonErrorInstallFailed", ex.Message);
        }
        finally
        {
            DeleteTemp(tempDir);
        }

        Reload();
        return Addons.FirstOrDefault(a => a.RootPath == target) ?? throw new AddonInstallException("AddonErrorInstallFailed", Path.GetFileName(target));
    }

    public void Uninstall(string id)
    {
        Addon? addon = Find(id);
        if (addon == null)
        {
            return;
        }

        Directory.Delete(addon.RootPath, true);
        AddonSettingsStore.Remove(id);

        lock (_lock)
        {
            _state.Order.Remove(id);
            _state.Disabled.Remove(id);
            SaveState();
        }

        Reload();
    }

    public static List<T> Merge<T>(IReadOnlyList<T> baseItems, IEnumerable<(int? Index, T Item)> inserts)
    {
        SortedDictionary<int, T> placed = new();
        List<T> unplaced = new();
        foreach ((int? index, T item) in inserts)
        {
            if (index is not int position)
            {
                unplaced.Add(item);
                continue;
            }

            while (placed.ContainsKey(position))
            {
                position++;
            }

            placed[position] = item;
        }

        List<T> result = new();
        int baseIndex = 0;
        int slot = 0;
        while (baseIndex < baseItems.Count)
        {
            if (placed.Remove(slot, out T? addonItem))
            {
                result.Add(addonItem);
            }
            else
            {
                result.Add(baseItems[baseIndex]);
                baseIndex++;
            }

            slot++;
        }

        result.AddRange(placed.Values);
        result.AddRange(unplaced);
        return result;
    }

    private void StartWatching()
    {
        try
        {
            _watcher = new(AddonsDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Changed += OnAddonFilesChanged;
            _watcher.Created += OnAddonFilesChanged;
            _watcher.Deleted += OnAddonFilesChanged;
            _watcher.Renamed += OnAddonFilesChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnAddonFilesChanged(object sender, FileSystemEventArgs e)
    {
        _reloadTimer.Change(WatchDebounceMs, Timeout.Infinite);
    }

    private List<Addon> Discover()
    {
        List<Addon> addons = new();
        string dir = AddonsDirectory;
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(dir).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            return addons;
        }

        foreach (string folder in folders)
        {
            Addon addon = Addon.Load(folder);
            if (addons.Any(a => a.Id == addon.Id))
            {
                addon.SetError("AddonErrorDuplicateId", addon.Id);
                addon.Id = Path.GetFileName(folder);
            }

            addons.Add(addon);
        }

        return addons;
    }

    private void ApplyState()
    {
        List<string> missing = _addons
            .Select(a => a.Id)
            .Where(id => !_state.Order.Contains(id))
            .ToList();
        if (missing.Count > 0)
        {
            _state.Order.AddRange(missing);
            SaveState();
        }

        foreach (Addon addon in _addons)
        {
            addon.IsEnabled = !_state.Disabled.Contains(addon.Id);
        }

        _addons = _addons
            .OrderBy(a => _state.Order.IndexOf(a.Id))
            .ToList();
    }

    private static string LocateAddonRoot(string sourcePath, out string? tempDir)
    {
        tempDir = null;
        string root = sourcePath;
        if (File.Exists(sourcePath))
        {
            if (!sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                throw new AddonInstallException("AddonErrorUnsupportedSource", Path.GetFileName(sourcePath));
            }

            tempDir = Path.Combine(AppPaths.GetCacheDir("addon-install"), Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(sourcePath, tempDir);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                DeleteTemp(tempDir);
                throw new AddonInstallException("AddonErrorInstallFailed", ex.Message);
            }

            root = tempDir;
        }

        if (File.Exists(Path.Combine(root, Addon.ManifestFileName)))
        {
            return root;
        }

        List<string> children = Directory.Exists(root) ? Directory.EnumerateDirectories(root).ToList() : new();
        if (children.Count == 1 && File.Exists(Path.Combine(children[0], Addon.ManifestFileName)))
        {
            return children[0];
        }

        DeleteTemp(tempDir);
        throw new AddonInstallException("AddonErrorManifestMissing", Addon.ManifestFileName);
    }

    private static string ReadInstallId(string root)
    {
        AddonManifest manifest;
        try
        {
            manifest = Addon.ReadManifest(File.ReadAllText(Path.Combine(root, Addon.ManifestFileName)));
        }
        catch (Exception ex)
        {
            throw new AddonInstallException("AddonErrorManifestInvalid", ex.Message);
        }

        string id = string.IsNullOrWhiteSpace(manifest.Id) ? Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) : manifest.Id.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            id = id.Replace(invalid, '_');
        }

        return id;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }

    private static void DeleteTemp(string? tempDir)
    {
        try
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch
        {
        }
    }

    private StateData? ReadState()
    {
        try
        {
            if (!File.Exists(_statePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<StateData>(File.ReadAllText(_statePath));
        }
        catch
        {
            return null;
        }
    }

    private void SaveState()
    {
        try
        {
            JsonSerializerOptions options = new() { WriteIndented = true };
            File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, options));
        }
        catch
        {
        }
    }
}
