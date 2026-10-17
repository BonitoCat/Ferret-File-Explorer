using System.Text.Json;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public sealed class SidebarOrderService
{
    private sealed class OrderData
    {
        public List<string> Sections { get; set; } = new();
        public Dictionary<string, List<string>> Items { get; set; } = new();
    }

    public static readonly PlaceKind[] DefaultSectionOrder = [PlaceKind.Places, PlaceKind.Bookmarks, PlaceKind.Devices, PlaceKind.Network];

    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly string _legacySectionsPath;
    private OrderData _data = new();

    public SidebarOrderService()
    {
        _filePath = AppPaths.GetDataPath("sidebar_order.json");
        _legacySectionsPath = Path.Combine(AppPaths.DataDir, "sidebar_sections");
        Reload();
    }

    public static string GetKey(string path, string? remoteUri, string? devicePath)
    {
        return remoteUri ?? devicePath ?? path;
    }

    public static string GetKey(PlaceInfo place)
    {
        return GetKey(place.Path, place.RemoteUri, place.DevicePath);
    }

    public void Reload()
    {
        lock (_lock)
        {
            _data = ReadFile() ?? ReadLegacy() ?? new();
        }
    }

    public List<PlaceKind> GetSectionOrder()
    {
        lock (_lock)
        {
            List<PlaceKind> order = new();
            foreach (string name in _data.Sections)
            {
                if (Enum.TryParse(name, out PlaceKind kind) && !order.Contains(kind))
                {
                    order.Add(kind);
                }
            }

            order.AddRange(DefaultSectionOrder.Where(k => !order.Contains(k)));
            return order;
        }
    }

    public void SetSectionOrder(IEnumerable<PlaceKind> order)
    {
        lock (_lock)
        {
            _data = ReadFile() ?? _data;
            _data.Sections = order.Select(k => k.ToString()).ToList();
            Save();
        }
    }

    public int GetItemIndex(PlaceKind kind, string key)
    {
        lock (_lock)
        {
            if (!_data.Items.TryGetValue(kind.ToString(), out List<string>? keys))
            {
                return int.MaxValue;
            }

            int index = keys.IndexOf(key);
            return index >= 0 ? index : int.MaxValue;
        }
    }

    public List<T> Sort<T>(PlaceKind kind, IEnumerable<T> items, Func<T, string> keySelector)
    {
        List<T> list = items.ToList();
        AppendMissing(kind, list.Select(keySelector));

        return list
            .OrderBy(item => GetItemIndex(kind, keySelector(item)))
            .ToList();
    }

    public void AppendMissing(PlaceKind kind, IEnumerable<string> keys)
    {
        lock (_lock)
        {
            if (!_data.Items.TryGetValue(kind.ToString(), out List<string>? stored))
            {
                return;
            }

            List<string> missing = keys
                .Where(k => !stored.Contains(k))
                .Distinct()
                .ToList();
            if (missing.Count == 0)
            {
                return;
            }

            _data = ReadFile() ?? _data;
            if (!_data.Items.TryGetValue(kind.ToString(), out List<string>? current))
            {
                return;
            }

            current.AddRange(missing.Where(k => !current.Contains(k)));
            Save();
        }
    }

    public void SetItemOrder(PlaceKind kind, IEnumerable<string> keys)
    {
        lock (_lock)
        {
            _data = ReadFile() ?? _data;
            List<string> ordered = keys.Distinct().ToList();
            if (_data.Items.TryGetValue(kind.ToString(), out List<string>? previous))
            {
                ordered.AddRange(previous.Where(k => !ordered.Contains(k)));
            }

            _data.Items[kind.ToString()] = ordered;
            Save();
        }
    }

    public void ResetLayout()
    {
        lock (_lock)
        {
            _data = ReadFile() ?? _data;
            _data.Sections.Clear();
            _data.Items.Remove(PlaceKind.Places.ToString());
            Save();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _data = new();
            Save();
        }
    }

    private OrderData? ReadFile()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<OrderData>(File.ReadAllText(_filePath));
        }
        catch
        {
            return null;
        }
    }

    private OrderData? ReadLegacy()
    {
        try
        {
            if (!File.Exists(_legacySectionsPath))
            {
                return null;
            }

            OrderData data = new()
            {
                Sections = File.ReadAllLines(_legacySectionsPath)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .ToList(),
            };

            _data = data;
            Save();
            File.Delete(_legacySectionsPath);

            return data;
        }
        catch
        {
            return null;
        }
    }

    private void Save()
    {
        try
        {
            JsonSerializerOptions options = new() { WriteIndented = true };
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, options));
        }
        catch
        {
        }
    }
}
