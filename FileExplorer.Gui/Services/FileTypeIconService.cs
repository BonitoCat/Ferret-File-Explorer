using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Gui.Services;

public static class FileTypeIconService
{
    private const string ExtensionKeyPrefix = "ext:";

    private static readonly string _storePath = AppPaths.GetDataPath("type_icons.json");
    private static readonly Dictionary<string, string> _overrides = Load();
    private static readonly object _lock = new();

    private static readonly HashSet<string> CodeMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/javascript",
        "application/x-javascript",
        "application/x-php",
        "application/x-perl",
        "application/x-ruby",
        "application/x-shellscript",
        "application/xml",
        "application/xhtml+xml",
        "application/x-yaml",
        "application/yaml",
        "application/toml",
        "text/html",
        "text/css",
        "text/javascript",
        "text/xml",
    };

    private static readonly HashSet<string> DocumentMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/msword",
        "application/rtf",
        "application/x-abiword",
        "application/epub+zip",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.text-template",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.template",
        "text/rtf",
    };

    private static readonly HashSet<string> SpreadsheetMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.ms-excel",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.spreadsheet-template",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.template",
        "text/csv",
        "text/tab-separated-values",
    };

    private static readonly HashSet<string> PresentationMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.ms-powerpoint",
        "application/vnd.oasis.opendocument.presentation",
        "application/vnd.oasis.opendocument.presentation-template",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.presentationml.slideshow",
        "application/vnd.openxmlformats-officedocument.presentationml.template",
    };

    private static readonly HashSet<string> DatabaseMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/vnd.sqlite3",
        "application/x-sqlite2",
        "application/x-sqlite3",
        "application/sql",
        "application/x-sql",
        "text/x-sql",
        "application/vnd.ms-access",
        "application/x-msaccess",
    };

    private static readonly HashSet<string> DiskImageMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/x-cd-image",
        "application/x-iso9660-image",
        "application/x-raw-disk-image",
        "application/x-apple-diskimage",
        "application/vnd.efi.iso",
        "application/vnd.efi.img",
        "application/x-virtualbox-vdi",
        "application/x-virtualbox-vmdk",
        "application/x-qemu-disk",
    };

    private static readonly HashSet<string> DatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db",
        ".db3",
        ".sqlite",
        ".sqlite3",
        ".mdf",
        ".ndf",
        ".ldf",
        ".accdb",
        ".mdb",
    };

    public static event Action? Changed;

    public static string? GetMimeType(string path)
    {
        return MimeService.GetMimeTypeForExtension(Path.GetExtension(path));
    }

    public static string? GetKey(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension.Length == 0)
        {
            return null;
        }

        string? mime = MimeService.GetMimeTypeForExtension(extension);
        if (mime == null || MimeService.IsGenericMimeType(mime) || MimeService.IsCustomMimeType(mime))
        {
            return ExtensionKeyPrefix + extension;
        }

        return mime;
    }

    public static string GetKeyDisplayName(string key)
    {
        return key.StartsWith(ExtensionKeyPrefix, StringComparison.Ordinal) ? key[ExtensionKeyPrefix.Length..] : key;
    }

    public static string? GetCustomIconPath(string path)
    {
        string? key = GetKey(path);
        if (key == null)
        {
            return null;
        }

        lock (_lock)
        {
            return _overrides.TryGetValue(key, out string? iconPath) && File.Exists(iconPath) ? iconPath : null;
        }
    }

    public static bool HasCustomIcon(string key)
    {
        lock (_lock)
        {
            return _overrides.ContainsKey(key);
        }
    }

    public static void SetCustomIcon(string key, string sourcePath)
    {
        string iconsDir = AppPaths.GetDataDir("type_icons");
        string safeName = new(key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
        string target = Path.Combine(iconsDir, $"{safeName}_{DateTime.UtcNow.Ticks}{Path.GetExtension(sourcePath).ToLowerInvariant()}");
        File.Copy(sourcePath, target, true);

        string? previous;
        lock (_lock)
        {
            _overrides.TryGetValue(key, out previous);
            _overrides[key] = target;
            Save();
        }

        DeleteIconFile(previous);
        Changed?.Invoke();
    }

    public static void ResetCustomIcon(string key)
    {
        string? previous;
        lock (_lock)
        {
            if (!_overrides.Remove(key, out previous))
            {
                return;
            }

            Save();
        }

        DeleteIconFile(previous);
        Changed?.Invoke();
    }

    public static string? GetBuiltInIcon(string path)
    {
        string extension = Path.GetExtension(path);
        if (DatabaseExtensions.Contains(extension))
        {
            return "file-database";
        }

        string? mime = MimeService.GetMimeTypeForExtension(extension);
        if (mime == null)
        {
            return null;
        }

        if (mime == "application/json" || mime.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            return "file-json";
        }

        if (mime is "application/pdf" or "application/postscript")
        {
            return "file-pdf";
        }

        if (DatabaseMimes.Contains(mime))
        {
            return "file-database";
        }

        if (DocumentMimes.Contains(mime))
        {
            return "file-document";
        }

        if (SpreadsheetMimes.Contains(mime))
        {
            return "file-spreadsheet";
        }

        if (PresentationMimes.Contains(mime))
        {
            return "file-presentation";
        }

        if (DiskImageMimes.Contains(mime))
        {
            return "file-disk";
        }

        if (mime.StartsWith("font/", StringComparison.OrdinalIgnoreCase) || mime.StartsWith("application/x-font", StringComparison.OrdinalIgnoreCase))
        {
            return "file-font";
        }

        if (CodeMimes.Contains(mime) || mime.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) || IsSourceMime(mime))
        {
            return "file-code";
        }

        return null;
    }

    private static bool IsSourceMime(string mime)
    {
        if (!mime.StartsWith("text/x-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return mime is not ("text/x-log" or "text/x-readme" or "text/x-changelog" or "text/x-copying" or "text/x-authors" or "text/x-install");
    }

    private static void DeleteIconFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(_storePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_storePath)) ?? new();
            }
        }
        catch
        {
        }

        return new();
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(_storePath, JsonSerializer.Serialize(_overrides, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
