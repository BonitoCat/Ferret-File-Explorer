using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public static class MimeUtils
{
    [DllImport("libmagic.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr magic_open(int flags);

    [DllImport("libmagic.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern int magic_load(IntPtr cookie, IntPtr filename);

    [DllImport("libmagic.so.1", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr magic_file(IntPtr cookie, string filename);
    
    private static readonly ConcurrentDictionary<string, string?> ExtensionCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string?> PathCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object MagicLock = new();
    private static IntPtr _magicCookie = IntPtr.Zero;
    private static bool _magicReady;

    public static string? GetMimeTypeFast(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        string extension = Path.GetExtension(path);
        if (extension.Length != 0)
        {
            if (ExtensionCache.TryGetValue(extension, out string? cached))
            {
                return cached;
            }

            string? guess = GuessByExtension(extension);
            ExtensionCache[extension] = guess;
            
            return guess;
        }

        return null;
    }

    public static string? GetMimeTypeAccurate(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        if (PathCache.TryGetValue(path, out string? cached))
        {
            return cached;
        }

        EnsureMagic();

        if (!_magicReady)
        {
            return null;
        }

        string? result = null;

        lock (MagicLock)
        {
            IntPtr ptr = magic_file(_magicCookie, path);
            if (ptr != IntPtr.Zero)
            {
                result = Marshal.PtrToStringAnsi(ptr);
            }
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            result = null;
        }
        else
        {
            int semicolon = result.IndexOf(';');
            if (semicolon >= 0)
            {
                result = result.Substring(0, semicolon);
            }
            
            result = result.Trim();
        }

        PathCache[path] = result;
        return result;
    }

    public static void ClearCaches()
    {
        ExtensionCache.Clear();
        PathCache.Clear();
    }

    private static string? GuessByExtension(string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".txt":
            case ".log":
            case ".md":
            case ".json":
            case ".xml":
            case ".yaml":
            case ".yml":
            case ".ini":
            case ".conf":
            case ".cfg":
            case ".cs":
            case ".cpp":
            case ".c":
            case ".h":
            case ".hpp":
            case ".py":
            case ".js":
            case ".ts":
            case ".sh":
            case ".lua":
                return "text/plain";

            case ".png":
                return "image/png";
            case ".jpg":
            case ".jpeg":
                return "image/jpeg";
            case ".gif":
                return "image/gif";
            case ".webp":
                return "image/webp";
            case ".svg":
            case ".svgz":
                return "image/svg+xml";
            case ".ico":
                return "image/vnd.microsoft.icon";
            case ".bmp":
                return "image/bmp";

            case ".mp3":
                return "audio/mpeg";
            case ".wav":
                return "audio/wav";
            case ".flac":
                return "audio/flac";
            case ".ogg":
                return "audio/ogg";

            case ".mp4":
                return "video/mp4";
            case ".mkv":
                return "video/x-matroska";
            case ".webm":
                return "video/webm";
            case ".avi":
                return "video/x-msvideo";
            case ".mov":
                return "video/quicktime";

            case ".deb":
                return "application/vnd.debian.binary-package";

            case ".pdf":
                return "application/pdf";
                
            case ".zip":
                return "application/zip";
            case ".7z":
                return "application/x-7z-compressed";
            case ".rar":
                return "application/x-rar";
            case ".tar":
                return "application/x-tar";
            case ".gz":
                return "application/gzip";
            case ".bz2":
                return "application/x-bzip2";
            case ".xz":
                return "application/x-xz";
            case ".zst":
                return "application/zstd";
            case ".lz4":
                return "application/x-lz4";
            case ".iso":
                return "application/x-iso9660-image";

            default:
                return null;
        }
    }
    
    public static bool IsArchiveMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return false;
        }

        return mime is
            "application/zip" or
            "application/x-7z-compressed" or
            "application/x-rar" or
            "application/x-rar-compressed" or
            "application/x-tar" or
            "application/gzip" or
            "application/x-gzip" or
            "application/x-bzip2" or
            "application/x-xz" or
            "application/zstd" or
            "application/x-lz4" or
            "application/x-rpm" or
            "application/x-iso9660-image" or
            "application/java-archive";
    }

    private static void EnsureMagic()
    {
        if (_magicReady)
        {
            return;
        }

        lock (MagicLock)
        {
            if (_magicReady)
            {
                return;
            }

            _magicCookie = magic_open(0x000010);
            if (_magicCookie == IntPtr.Zero)
            {
                _magicReady = false;
                return;
            }

            int loadResult = magic_load(_magicCookie, IntPtr.Zero);
            _magicReady = loadResult == 0;
        }
    }
}