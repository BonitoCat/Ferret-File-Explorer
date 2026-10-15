using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Principal;
using System.Text;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Gui.Services;

public static class IconService
{
    private static readonly ConcurrentDictionary<string, IImage> _cache = new();
    private static readonly string _userIconsDir = Path.Combine(AppPaths.DataDir, "icons");

    public static IImage? Get(string iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName))
        {
            return null;
        }

        if (_cache.TryGetValue(iconName, out IImage? cached))
        {
            return cached;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.Invoke(() => Get(iconName));
        }

        IImage? loaded = LoadIcon(iconName);
        if (loaded != null)
        {
            _cache[iconName] = loaded;
        }

        return loaded;
    }

    public static IImage? GetForFile(FileType type)
    {
        return type switch
        {
            FileType.Executable => Get("executable"),
            FileType.Archive => Get("archive"),
            FileType.Image => Get("image"),
            FileType.Video => Get("video"),
            FileType.Audio => Get("audio"),
            FileType.Deb => Get("package"),
            _ => Get("file"),
        };
    }

    public static string GetForFolder(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "folder";
        }

        string trimmed = path.TrimEnd('/');
        if (trimmed == UserDirs.Desktop)
        {
            return "folder-desktop";
        }

        if (trimmed == UserDirs.Documents)
        {
            return "folder-documents";
        }

        if (trimmed == UserDirs.Downloads)
        {
            return "folder-downloads";
        }

        if (trimmed == UserDirs.Music)
        {
            return "folder-music";
        }

        if (trimmed == UserDirs.Pictures)
        {
            return "folder-pictures";
        }

        if (trimmed == UserDirs.Videos)
        {
            return "folder-videos";
        }

        return "folder";
    }

    public static IImage? GetForEntry(bool isDirectory, FileType type, string? path = null)
    {
        if (isDirectory)
        {
            string? color = string.IsNullOrEmpty(path) ? null : FolderColorService.Get(path);
            return color == null ? Get(GetForFolder(path)) : GetColored(GetForFolder(path), color);
        }

        return GetForFile(type);
    }

    public static IImage? GetColored(string iconName, string hex, bool cache = true)
    {
        string key = $"{iconName}|{hex}";
        if (cache && _cache.TryGetValue(key, out IImage? cached))
        {
            return cached;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.Invoke(() => GetColored(iconName, hex, cache));
        }

        string? svg = ReadIconSvg(iconName);
        if (svg == null)
        {
            return Get(iconName);
        }

        try
        {
            string recolored = svg.Replace(FolderColorService.DefaultFolderColor, hex, StringComparison.OrdinalIgnoreCase);
            using MemoryStream stream = new(Encoding.UTF8.GetBytes(recolored));
            SvgSource? source = SvgSource.LoadFromStream(stream, null);
            if (source == null)
            {
                return Get(iconName);
            }

            SvgImage image = new() { Source = source };
            if (cache)
            {
                _cache[key] = image;
            }

            return image;
        }
        catch
        {
            return Get(iconName);
        }
    }

    private static string? ReadIconSvg(string iconName)
    {
        string fileName = iconName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ? iconName
            : $"{iconName}.svg";

        try
        {
            string userOverridePath = Path.Combine(_userIconsDir, fileName);
            if (File.Exists(userOverridePath))
            {
                return File.ReadAllText(userOverridePath);
            }

            string localAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons", fileName);
            if (File.Exists(localAssetsPath))
            {
                return File.ReadAllText(localAssetsPath);
            }

            Uri resourceUri = new($"avares://FileExplorer.Gui/Assets/Icons/{fileName}");
            if (AssetLoader.Exists(resourceUri))
            {
                using Stream stream = AssetLoader.Open(resourceUri);
                using StreamReader reader = new(stream);
                return reader.ReadToEnd();
            }
        }
        catch
        {
        }

        return null;
    }

    public static void ClearCache()
    {
        _cache.Clear();
    }

    private static IImage? LoadIcon(string iconName)
    {
        string fileName = iconName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            ? iconName
            : $"{iconName}.svg";

        string userOverridePath = Path.Combine(_userIconsDir, fileName);
        if (File.Exists(userOverridePath))
        {
            try
            {
                using FileStream stream = File.OpenRead(userOverridePath);
                SvgSource? source = SvgSource.LoadFromStream(stream, null);
                if (source != null)
                {
                    return new SvgImage { Source = source };
                }
            }
            catch
            {
            }
        }

        string localAssetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Icons", fileName);
        if (File.Exists(localAssetsPath))
        {
            try
            {
                using FileStream stream = File.OpenRead(localAssetsPath);
                SvgSource? source = SvgSource.LoadFromStream(stream, null);
                if (source != null)
                {
                    return new SvgImage { Source = source };
                }
            }
            catch
            {
            }
        }

        try
        {
            Uri resourceUri = new($"avares://FileExplorer.Gui/Assets/Icons/{fileName}");
            if (AssetLoader.Exists(resourceUri))
            {
                using Stream stream = AssetLoader.Open(resourceUri);
                SvgSource? source = SvgSource.LoadFromStream(stream, null);
                if (source != null)
                {
                    return new SvgImage { Source = source };
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
