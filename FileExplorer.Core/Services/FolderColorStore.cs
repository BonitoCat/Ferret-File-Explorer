using System.Text.Json;
using FileExplorer.Core.Utilities;
using FileLib;

namespace FileExplorer.Core.Services;

public static class FolderColorStore
{
    private const long ReloadCheckIntervalMs = 1000;

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static Dictionary<string, string> _colors = new(StringComparer.Ordinal);
    private static DateTime _loadedWriteTime = DateTime.MinValue;
    private static long _lastCheck;

    private static string FilePath => Path.Combine(AppPaths.DataDir, "folder_colors.json");

    public static string? Get(string path)
    {
        lock (Sync)
        {
            Reload(false);
            return _colors.TryGetValue(Normalize(path), out string? color) ? color : null;
        }
    }

    public static bool HasAny(string path)
    {
        string key = Normalize(path);
        lock (Sync)
        {
            Reload(false);
            return _colors.Keys.Any(k => IsSameOrInside(k, key));
        }
    }

    public static void Set(IEnumerable<string> paths, string? hex)
    {
        Update(colors =>
        {
            foreach (string path in paths)
            {
                string key = Normalize(path);
                if (string.IsNullOrEmpty(hex))
                {
                    colors.Remove(key);
                }
                else
                {
                    colors[key] = hex.ToUpperInvariant();
                }
            }

            return true;
        });
    }

    public static void Move(string oldPath, string newPath)
    {
        string oldKey = Normalize(oldPath);
        string newKey = Normalize(newPath);
        if (oldKey == newKey)
        {
            return;
        }

        Update(colors =>
        {
            List<string> keys = colors.Keys.Where(k => IsSameOrInside(k, oldKey)).ToList();
            if (keys.Count == 0)
            {
                return false;
            }

            List<KeyValuePair<string, string>> moved = keys.Select(k => new KeyValuePair<string, string>(newKey + k[oldKey.Length..], colors[k])).ToList();
            foreach (string key in keys)
            {
                colors.Remove(key);
            }

            foreach (KeyValuePair<string, string> entry in moved)
            {
                colors[entry.Key] = entry.Value;
            }

            return true;
        });
    }

    public static void Copy(string sourcePath, string destPath, bool includeChildren = true)
    {
        string sourceKey = Normalize(sourcePath);
        string destKey = Normalize(destPath);
        if (sourceKey == destKey)
        {
            return;
        }

        Update(colors =>
        {
            List<KeyValuePair<string, string>> copied = colors
                .Where(pair => includeChildren ? IsSameOrInside(pair.Key, sourceKey) : pair.Key == sourceKey)
                .Select(pair => new KeyValuePair<string, string>(destKey + pair.Key[sourceKey.Length..], pair.Value))
                .ToList();

            bool changed = false;
            foreach (KeyValuePair<string, string> entry in copied)
            {
                if (colors.TryGetValue(entry.Key, out string? existing) && existing == entry.Value)
                {
                    continue;
                }

                colors[entry.Key] = entry.Value;
                changed = true;
            }

            return changed;
        });
    }

    public static void Remove(string path)
    {
        string key = Normalize(path);
        Update(colors =>
        {
            List<string> keys = colors.Keys.Where(k => IsSameOrInside(k, key)).ToList();
            foreach (string removed in keys)
            {
                colors.Remove(removed);
            }

            return keys.Count > 0;
        });
    }

    private static void Update(Func<Dictionary<string, string>, bool> change)
    {
        lock (Sync)
        {
            Reload(true);
            if (!change(_colors))
            {
                return;
            }

            Save();
        }
    }

    private static bool IsSameOrInside(string key, string root)
    {
        return PathManager.IsSameOrAncestor(root, key);
    }

    private static string Normalize(string path)
    {
        return PathManager.TrimEnd(path);
    }

    private static void Reload(bool force)
    {
        long now = Environment.TickCount64;
        if (!force && now - _lastCheck < ReloadCheckIntervalMs)
        {
            return;
        }

        _lastCheck = now;

        DateTime writeTime = GetWriteTime();
        if (writeTime == _loadedWriteTime)
        {
            return;
        }

        _colors = Load();
        _loadedWriteTime = writeTime;
    }

    private static DateTime GetWriteTime()
    {
        try
        {
            return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new(StringComparer.Ordinal);
            }

            Dictionary<string, string>? data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            if (data != null)
            {
                return new(data, StringComparer.Ordinal);
            }
        }
        catch
        {
        }

        return new(StringComparer.Ordinal);
    }

    private static void Save()
    {
        try
        {
            AppPaths.EnsureDir(AppPaths.DataDir);
            if (!File.Exists(FilePath))
            {
                FileHelper.Create(FilePath);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(_colors, JsonOptions));
            _loadedWriteTime = GetWriteTime();
        }
        catch
        {
        }
    }
}
