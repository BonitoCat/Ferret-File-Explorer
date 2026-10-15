using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Gui.Services;

public static class FolderColorService
{
    public const string DefaultFolderColor = "#84B4FA";

    public static readonly (string Name, string Hex)[] Presets =
    [
        ("Red", "#EF5350"),
        ("Orange", "#FF9F43"),
        ("Yellow", "#F6C945"),
        ("Green", "#66BB6A"),
        ("Teal", "#26A69A"),
        ("Aqua", "#4DD0E1"),
        ("Blue", "#4A7DF0"),
        ("Purple", "#AB7BF5"),
        ("Pink", "#F48FB1"),
        ("Brown", "#A1887F"),
        ("Grey", "#9E9E9E"),
        ("Dark", "#5C6370"),
    ];

    private static readonly string FilePath = Path.Combine(AppPaths.DataDir, "folder_colors.json");

    private static readonly Lazy<ConcurrentDictionary<string, string>> Colors = new(Load);

    public static string? Get(string path)
    {
        return Colors.Value.TryGetValue(Normalize(path), out string? color) ? color : null;
    }

    public static void Set(IEnumerable<string> paths, string? hex)
    {
        foreach (string path in paths)
        {
            string key = Normalize(path);
            if (string.IsNullOrEmpty(hex))
            {
                Colors.Value.TryRemove(key, out _);
            }
            else
            {
                Colors.Value[key] = hex.ToUpperInvariant();
            }
        }

        Save();
    }

    private static string Normalize(string path)
    {
        string trimmed = path.TrimEnd('/');
        return string.IsNullOrEmpty(trimmed) ? "/" : trimmed;
    }

    private static ConcurrentDictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                Dictionary<string, string>? data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
                if (data != null)
                {
                    return new(data, StringComparer.Ordinal);
                }
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Colors.Value, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
