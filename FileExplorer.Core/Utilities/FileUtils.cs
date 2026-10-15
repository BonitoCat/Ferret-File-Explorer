using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public static partial class FileUtils
{
    private const int XOk = 1;

    [LibraryImport("libc.so.6", EntryPoint = "access", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LinuxAccess(string pathname, int mode);

    [LibraryImport("libSystem.dylib", EntryPoint = "access", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MacAccess(string pathname, int mode);

    [LibraryImport("kernel32.dll", EntryPoint = "GetBinaryTypeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetBinaryType(string applicationName, out uint binaryType);

    public static bool CanRead(string path, bool isDir)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            int mode = isDir ? 5 : 4;
            return LinuxAccess(path, mode) == 0;
        }

        return true;
    }

    public static bool HasAccess(string path, bool read, bool write, bool execute)
    {
        if (string.IsNullOrWhiteSpace(path) || !OperatingSystem.IsLinux())
        {
            return false;
        }

        int mode = (read ? 4 : 0) | (write ? 2 : 0) | (execute ? 1 : 0);
        return LinuxAccess(path, mode) == 0;
    }

    public static bool IsFileExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            return LinuxAccess(path, XOk) == 0;
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacAccess(path, XOk) == 0;
        }

        if (OperatingSystem.IsWindows())
        {
            if (GetBinaryType(path, out _))
            {
                return true;
            }

            string extension = Path.GetExtension(path);
            return extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        if (bytes < 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "-- KB/s";
        }

        return $"{FormatSize((long) bytesPerSecond)}/s";
    }

    public static string FormatEta(TimeSpan? eta)
    {
        if (!eta.HasValue || eta.Value.TotalSeconds <= 0)
        {
            return "almost done";
        }

        int seconds = (int) Math.Ceiling(eta.Value.TotalSeconds);
        if (seconds < 5)
        {
            return "a few seconds remaining";
        }

        if (seconds < 60)
        {
            return $"about {seconds} seconds remaining";
        }

        int minutes = seconds / 60;
        int remSec = seconds % 60;
        if (minutes < 60)
        {
            return remSec == 0 ? $"about {minutes} minutes remaining" : $"about {minutes}m {remSec}s remaining";
        }

        int hours = minutes / 60;
        int remMin = minutes % 60;
        return $"about {hours}h {remMin}m remaining";
    }
}