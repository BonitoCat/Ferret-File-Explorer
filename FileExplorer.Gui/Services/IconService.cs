using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Principal;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using FileExplorer.Core.Entries;

namespace FileExplorer.Gui.Services;

public static class IconService
{
    private static readonly ConcurrentDictionary<string, IImage> _cache = new();
    private static readonly string _userIconsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "fe", "icons");

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

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            if (path == Path.Combine(userProfile, "Desktop"))
            {
                return "folder-desktop";
            }

            if (path == Path.Combine(userProfile, "Documents"))
            {
                return "folder-documents";
            }

            if (path == Path.Combine(userProfile, "Downloads"))
            {
                return "folder-downloads";
            }

            if (path == Path.Combine(userProfile, "Music"))
            {
                return "folder-music";
            }

            if (path == Path.Combine(userProfile, "Pictures"))
            {
                return "folder-pictures";
            }

            if (path == Path.Combine(userProfile, "Videos"))
            {
                return "folder-videos";
            }
        }

        return "folder";
    }

    public static IImage? GetForEntry(bool isDirectory, FileType type, string? path = null)
    {
        if (isDirectory)
        {
            return Get(GetForFolder(path));
        }

        return GetForFile(type);
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
