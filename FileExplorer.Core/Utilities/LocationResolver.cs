using System;
using System.IO;
using FileExplorer.Core.Services;

namespace FileExplorer.Core.Utilities;

public static class LocationResolver
{
    private static readonly string[] DesktopSchemes = ["x-nemo-desktop:", "x-nautilus-desktop:"];
    private static readonly string[] GvfsSchemes = ["mtp", "afc", "gphoto2"];

    public static string TrashFilesDir => Path.Combine(UserDirs.Home, ".local", "share", "Trash", "files");

    public static string Resolve(string? input)
    {
        string location = (input ?? "").Trim();
        if (location.Length == 0)
        {
            return location;
        }

        if (location == "~" || location.StartsWith("~/", StringComparison.Ordinal))
        {
            return PathManager.ToNative(UserDirs.Home + location[1..]);
        }

        if (TryStripScheme(location, "trash:", out string trashRest))
        {
            EnsureDir(TrashFilesDir);
            return trashRest.Length == 0 ? TrashFilesDir : Path.Combine(TrashFilesDir, trashRest);
        }

        if (TryStripScheme(location, "computer:", out _))
        {
            return PathManager.RootPath;
        }

        if (TryStripScheme(location, "network:", out _))
        {
            return NetworkBrowseService.NetworkRoot;
        }

        foreach (string scheme in DesktopSchemes)
        {
            if (TryStripScheme(location, scheme, out _))
            {
                return UserDirs.Desktop;
            }
        }

        if (location.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(location, UriKind.Absolute, out Uri? fileUri) && fileUri.IsFile)
        {
            return fileUri.LocalPath;
        }

        return ResolveGvfs(location) ?? PathManager.ToNative(location);
    }

    private static string? ResolveGvfs(string location)
    {
        foreach (string scheme in GvfsSchemes)
        {
            string prefix = scheme + "://";
            if (!location.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "";
            if (runtimeDir.Length == 0)
            {
                return null;
            }

            string rest = location[prefix.Length..];
            int slash = rest.IndexOf('/');
            string host = Uri.UnescapeDataString(slash < 0 ? rest : rest[..slash]);
            string subPath = slash < 0 ? "" : Uri.UnescapeDataString(rest[(slash + 1)..]);

            string mountDir = Path.Combine(runtimeDir, "gvfs", $"{scheme}:host={host}");
            string target = subPath.Length == 0 ? mountDir : Path.Combine(mountDir, subPath);
            return Directory.Exists(target) ? target : null;
        }

        return null;
    }

    private static bool TryStripScheme(string location, string scheme, out string rest)
    {
        rest = "";
        if (!location.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rest = Uri.UnescapeDataString(location[scheme.Length..].TrimStart('/').TrimEnd('/'));
        return true;
    }

    private static void EnsureDir(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
        }
    }
}
