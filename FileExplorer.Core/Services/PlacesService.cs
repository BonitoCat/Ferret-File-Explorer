using System.Diagnostics;
using System.Text.Json;
using FileExplorer.Core.Models;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public enum PlaceKind
{
    Places,
    Bookmarks,
    Devices,
    Network,
}

public sealed class PlaceInfo
{
    public required PlaceKind Kind { get; init; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public bool IsMounted { get; set; } = true;
    public string? DevicePath { get; init; }
    public string? RemoteUri { get; init; }
    public bool IsNetworkBookmark { get; init; }
    public bool IsBookmark { get; init; }
}

public class PlacesService
{
    private readonly BookmarkService _bookmarkService = new();
    private readonly NetworkBookmarkService _networkBookmarkService = new();

    public List<PlaceInfo> GetAll()
    {
        List<PlaceInfo> places = GetStandardPlaces();
        places.AddRange(GetBookmarks());
        places.AddRange(GetDevices());
        places.AddRange(GetNetworkPlaces());

        return places;
    }

    public List<PlaceInfo> GetStandardPlaces()
    {
        List<PlaceInfo> places = new();
        AddIfExists(places, "Home", UserDirs.Home);
        AddIfExists(places, null, UserDirs.Desktop);
        AddIfExists(places, null, UserDirs.Documents);
        AddIfExists(places, null, UserDirs.Downloads);
        AddIfExists(places, null, UserDirs.Music);
        AddIfExists(places, null, UserDirs.Pictures);
        AddIfExists(places, null, UserDirs.Videos);
        places.Add(new() { Kind = PlaceKind.Places, Name = "File System", Path = "/" });

        string trash = System.IO.Path.Combine(UserDirs.Home, ".local", "share", "Trash", "files");
        if (Directory.Exists(trash))
        {
            places.Add(new() { Kind = PlaceKind.Places, Name = "Trash", Path = trash });
        }

        return places;
    }

    private static void AddIfExists(List<PlaceInfo> places, string? name, string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        places.Add(new()
        {
            Kind = PlaceKind.Places,
            Name = name ?? System.IO.Path.GetFileName(path),
            Path = path,
        });
    }

    public List<PlaceInfo> GetBookmarks()
    {
        List<PlaceInfo> places = new();
        foreach (string bookmark in _bookmarkService.GetBookmarks())
        {
            string resolved = bookmark;
            try
            {
                FileSystemInfo? target = new DirectoryInfo(bookmark).ResolveLinkTarget(true);
                if (target != null)
                {
                    resolved = target.FullName;
                }
            }
            catch
            {
            }

            places.Add(new()
            {
                Kind = PlaceKind.Bookmarks,
                Name = System.IO.Path.GetFileName(bookmark),
                Path = resolved,
                IsBookmark = true,
            });
        }

        return places;
    }

    public List<PlaceInfo> GetDevices()
    {
        List<PlaceInfo> places = new();
        if (!OperatingSystem.IsLinux())
        {
            return places;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        string? json = Run("lsblk", "-J -b -o NAME,PATH,LABEL,PARTLABEL,MOUNTPOINT,FSTYPE,SIZE,MODEL,RM,HOTPLUG,TYPE");
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("blockdevices", out JsonElement devices) && devices.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement device in devices.EnumerateArray())
                    {
                        ParseBlockDevice(device, places, seen);
                    }
                }
            }
            catch
            {
            }
        }

        foreach (string mount in NetworkService.GetGvfsMounts())
        {
            string dirName = System.IO.Path.GetFileName(mount);
            if (!dirName.StartsWith("mtp:", StringComparison.OrdinalIgnoreCase)
                && !dirName.StartsWith("afc:", StringComparison.OrdinalIgnoreCase)
                && !dirName.StartsWith("gphoto2:", StringComparison.OrdinalIgnoreCase)
                && !dirName.StartsWith("cdda:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            places.Add(new() { Kind = PlaceKind.Devices, Name = GetGvfsDisplayName(mount), Path = mount });
        }

        return places;
    }

    private static void ParseBlockDevice(JsonElement elem, List<PlaceInfo> places, HashSet<string> seen)
    {
        if (elem.TryGetProperty("children", out JsonElement children) && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0)
        {
            foreach (JsonElement child in children.EnumerateArray())
            {
                ParseBlockDevice(child, places, seen);
            }

            return;
        }

        string? type = GetString(elem, "type");
        string? path = GetString(elem, "path");
        string? fsType = GetString(elem, "fstype");
        string? mountPoint = GetString(elem, "mountpoint");
        string? label = GetString(elem, "label");
        string? partLabel = GetString(elem, "partlabel");
        string? model = GetString(elem, "model");
        long size = elem.TryGetProperty("size", out JsonElement sizeElem) && sizeElem.TryGetInt64(out long parsed) ? parsed : 0;

        if (type == "loop" || string.IsNullOrEmpty(path) || path.StartsWith("/dev/loop") || fsType is "squashfs" or "swap")
        {
            return;
        }

        if (!string.IsNullOrEmpty(mountPoint))
        {
            if (mountPoint is "/" or "/boot" || mountPoint.StartsWith("/boot/") || mountPoint.StartsWith("/snap") || mountPoint.StartsWith("/var/lib/snapd") || mountPoint.StartsWith("[SWAP]"))
            {
                return;
            }

            if (!seen.Add(mountPoint) || !seen.Add(path))
            {
                return;
            }

            places.Add(new()
            {
                Kind = PlaceKind.Devices,
                Name = FirstNonEmpty(label, partLabel, model) ?? System.IO.Path.GetFileName(mountPoint.TrimEnd('/')),
                Path = mountPoint,
                DevicePath = path,
            });
            return;
        }

        if (string.IsNullOrEmpty(fsType) || partLabel == "Microsoft reserved partition" || (size > 0 && size < 10 * 1024 * 1024) || !seen.Add(path))
        {
            return;
        }

        string sizeText = FormatDeviceSize(size);
        places.Add(new()
        {
            Kind = PlaceKind.Devices,
            Name = FirstNonEmpty(label, partLabel) ?? (model != null ? $"{model} ({sizeText})" : $"{sizeText} Volume"),
            Path = path,
            DevicePath = path,
            IsMounted = false,
        });
    }

    public List<PlaceInfo> GetNetworkPlaces()
    {
        List<PlaceInfo> places = new();
        List<NetworkBookmark> bookmarks = _networkBookmarkService.GetBookmarks();
        HashSet<string> matched = new(StringComparer.OrdinalIgnoreCase);

        foreach (string mount in NetworkService.GetGvfsMounts())
        {
            string dirName = System.IO.Path.GetFileName(mount);
            if (dirName.StartsWith("mtp:", StringComparison.OrdinalIgnoreCase)
                || dirName.StartsWith("afc:", StringComparison.OrdinalIgnoreCase)
                || dirName.StartsWith("gphoto2:", StringComparison.OrdinalIgnoreCase)
                || dirName.StartsWith("cdda:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            NetworkBookmark? bookmark = bookmarks.FirstOrDefault(b => MatchesHost(dirName, b.Uri));
            if (bookmark != null)
            {
                matched.Add(bookmark.Uri);
            }

            places.Add(new()
            {
                Kind = PlaceKind.Network,
                Name = bookmark?.Name ?? GetGvfsDisplayName(mount),
                Path = mount,
                RemoteUri = bookmark?.Uri,
                IsNetworkBookmark = bookmark != null,
            });
        }

        foreach (NetworkBookmark bookmark in bookmarks.Where(b => !matched.Contains(b.Uri)))
        {
            places.Add(new()
            {
                Kind = PlaceKind.Network,
                Name = bookmark.Name,
                Path = bookmark.Uri,
                RemoteUri = bookmark.Uri,
                IsNetworkBookmark = true,
                IsMounted = false,
            });
        }

        return places;
    }

    public static async Task<string?> MountDeviceAsync(string devicePath)
    {
        string output = await Task.Run(() => Run("udisksctl", $"mount -b \"{devicePath}\"", true) ?? "");
        foreach (string marker in new[] { " at `", " at " })
        {
            int start = output.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            start += marker.Length;
            int end = output.IndexOfAny(['\'', '.', '\n'], start);
            string mountPoint = (end > start ? output[start..end] : output[start..]).Trim().TrimEnd('.');
            if (Directory.Exists(mountPoint))
            {
                return mountPoint;
            }
        }

        return null;
    }

    public static async Task<bool> UnmountDeviceAsync(string devicePath)
    {
        string? output = await Task.Run(() => Run("udisksctl", $"unmount -b \"{devicePath}\"", true));
        return output != null && output.Contains("Unmounted", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetGvfsDisplayName(string mountPath)
    {
        string rawName = System.IO.Path.GetFileName(mountPath);
        if (rawName.StartsWith("mtp:host=", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = rawName["mtp:host=".Length..].Split('_', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? string.Join(" ", parts.Take(2)) : "Android Device";
        }

        if (rawName.StartsWith("afc:host=", StringComparison.OrdinalIgnoreCase))
        {
            return "Apple Device";
        }

        if (rawName.StartsWith("gphoto2:host=", StringComparison.OrdinalIgnoreCase))
        {
            return "Digital Camera";
        }

        foreach ((string prefix, string label) in new[] { ("sftp:host=", "SFTP"), ("ftp:host=", "FTP"), ("dav:host=", "WebDAV"), ("davs:host=", "WebDAV"), ("smb-share:server=", "SMB") })
        {
            if (!rawName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rest = rawName[prefix.Length..];
            int comma = rest.IndexOf(',');
            return $"{(comma >= 0 ? rest[..comma] : rest)} ({label})";
        }

        return rawName;
    }

    private static bool MatchesHost(string gvfsDirName, string uri)
    {
        try
        {
            return gvfsDirName.Contains(new Uri(uri).Host, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetString(JsonElement elem, string property)
    {
        return elem.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static string FormatDeviceSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double length = bytes;
        int order = 0;
        while (length >= 1000 && order < units.Length - 1)
        {
            order++;
            length /= 1000;
        }

        return $"{length:0.#} {units[order]}";
    }

    private static string? Run(string fileName, string arguments, bool includeStdErr = false)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return includeStdErr ? output + "\n" + error : output;
        }
        catch
        {
            return null;
        }
    }
}
