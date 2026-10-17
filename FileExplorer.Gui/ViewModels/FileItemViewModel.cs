using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.ViewModels;

public class FileItemViewModel : ViewModelBase
{
    private bool _isCut;
    private Bitmap? _thumbnail;
    private IImage? _icon;

    public required IFileSystemEntry Entry { get; init; }
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public bool IsSymlink { get; init; }
    public required string SizeString { get; init; }
    public required string TypeString { get; init; }
    public required string ModifiedString { get; init; }
    public required bool IsHidden { get; init; }
    public required bool IsAccessDenied { get; init; }
    public FileType FileType { get; init; }
    public IImage? Icon
    {
        get => _icon ?? Thumbnail ?? IconService.GetForEntry(IsDirectory, FileType, FullPath);
        init => _icon = value;
    }
    public string IconSvg { get; init; } = "";
    public string IconColor { get; init; } = "";

    public double DisplayOpacity => IsCut ? 0.5 : 1.0;
    public bool HasThumbnail => Thumbnail != null;

    public bool IsCut
    {
        get => _isCut;
        set
        {
            if (SetProperty(ref _isCut, value))
            {
                OnPropertyChanged(nameof(DisplayOpacity));
            }
        }
    }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(Icon));
            }
        }
    }

    private bool _isDropTarget;

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }

    public void RefreshIcon()
    {
        OnPropertyChanged(nameof(Icon));
    }

    public static FileItemViewModel FromEntry(IFileSystemEntry entry)
    {
        return FromEntry(entry, CancellationToken.None);
    }

    public static FileItemViewModel FromEntry(IFileSystemEntry entry, CancellationToken token)
    {
        bool isDir = entry is FolderEntry;
        bool isAccessDenied = CheckRequiresElevatedAccess(entry, isDir) && !Environment.IsPrivilegedProcess && Environment.UserName != "root";

        string sizeStr = "";
        string typeStr = LocalizationService.Get(isDir ? "FolderType" : "FileType");
        FileType fileType = FileType.Unknown;

        if (entry is FileEntry file)
        {
            sizeStr = FormatFileSize(file.FileSize);
            typeStr = LocalizationService.Get($"FileType{file.FileType}");
            fileType = file.FileType;
        }

        string modifiedStr = "";
        try
        {
            DateTime lastWrite = entry.LastWriteTime != default
                ? entry.LastWriteTime
                : (isDir ? Directory.GetLastWriteTime(entry.FullPath) : File.GetLastWriteTime(entry.FullPath));
            modifiedStr = lastWrite.ToString("yyyy-MM-dd HH:mm");
        }
        catch
        {
            modifiedStr = LocalizationService.Get("Unknown");
        }

        FileItemViewModel item = new()
        {
            Entry = entry,
            Name = entry.FullName,
            FullPath = entry.FullPath,
            IsDirectory = isDir,
            IsSymlink = entry.IsSymlink,
            FileType = fileType,
            SizeString = sizeStr,
            TypeString = typeStr,
            ModifiedString = modifiedStr,
            IsHidden = entry.IsHidden,
            IsAccessDenied = isAccessDenied,
        };

        if (!isDir && entry is FileEntry imageFile && imageFile.FileType == FileType.Image)
        {
            ThumbnailService.Request(item, entry.LastWriteTime, token);
        }

        return item;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double size = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private static bool CheckRequiresElevatedAccess(IFileSystemEntry entry, bool isDir)
    {
        string path = entry.FullPath;
        if (path.StartsWith("/run/user/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (OperatingSystem.IsLinux())
        {
            if (entry.UnixMode.HasValue && (entry.UnixMode.Value & UnixFileMode.OtherRead) != 0)
            {
                return false;
            }

            return !FileUtils.CanRead(path, isDir);
        }

        try
        {
            if (!isDir && File.Exists(path))
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return false;
            }

            if (isDir && Directory.Exists(path))
            {
                using IEnumerator<string> e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                e.MoveNext();
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
