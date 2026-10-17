using FileExplorer.Core.Entries;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Utilities;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FileExplorer.Core.Services;

public partial class FileSystemService
{
    [LibraryImport("libc.so.6", EntryPoint = "opendir", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr OpenDir(string path);

    [LibraryImport("libc.so.6", EntryPoint = "readdir")]
    private static partial IntPtr ReadDir(IntPtr dirp);

    [LibraryImport("libc.so.6", EntryPoint = "closedir")]
    private static partial int CloseDir(IntPtr dirp);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_new_for_path", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GFileNewForPath(string path);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_enumerate_children", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GFileEnumerateChildren(IntPtr file, string attributes, int flags, IntPtr cancellable, out IntPtr error);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_enumerator_next_file")]
    private static partial IntPtr GFileEnumeratorNextFile(IntPtr enumerator, IntPtr cancellable, out IntPtr error);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_info_get_name")]
    private static partial IntPtr GFileInfoGetName(IntPtr info);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_info_get_file_type")]
    private static partial int GFileInfoGetFileType(IntPtr info);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_info_get_size")]
    private static partial long GFileInfoGetSize(IntPtr info);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_file_info_get_attribute_uint64", StringMarshalling = StringMarshalling.Utf8)]
    private static partial ulong GFileInfoGetAttributeUInt64(IntPtr info, string attribute);

    [LibraryImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    private static partial void GObjectUnref(IntPtr handle);

    public IEnumerable<IFileSystemEntry> EnumerateFolderEntries(string path, string? searchPattern = null, CancellationToken token = default)
    {
        Regex? searchRegex = null;
        if (!string.IsNullOrEmpty(searchPattern))
        {
            try
            {
                searchRegex = new(searchPattern, RegexOptions.IgnoreCase);
            }
            catch
            {
                try
                {
                    string wildcardRegex = "^" + Regex.Escape(searchPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    searchRegex = new(wildcardRegex, RegexOptions.IgnoreCase);
                }
                catch
                {
                    searchRegex = null;
                }
            }
        }
        
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }
        
        NameComparer comparer = new();
        List<FolderEntry> dirList = new();
        List<FileEntry> fileList = new();

        if (OperatingSystem.IsLinux())
        {
            bool isRemote = path.StartsWith("/run/user/", StringComparison.OrdinalIgnoreCase);
            if (isRemote && TryEnumerateWithGio(path, searchPattern, searchRegex, token, dirList, fileList))
            {
                dirList.Sort((a, b) => comparer.Compare(a.FullName, b.FullName));
                fileList.Sort((a, b) => comparer.Compare(a.FullName, b.FullName));

                foreach (FolderEntry folder in dirList)
                {
                    token.ThrowIfCancellationRequested();
                    yield return folder;
                }

                foreach (FileEntry file in fileList)
                {
                    token.ThrowIfCancellationRequested();
                    yield return file;
                }

                yield break;
            }

            IntPtr dirp = OpenDir(path);
            if (dirp != IntPtr.Zero)
            {

                try
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        IntPtr entryPtr = ReadDir(dirp);
                        if (entryPtr == IntPtr.Zero)
                        {
                            break;
                        }

                        string? name = Marshal.PtrToStringUTF8(entryPtr + 19);
                        if (string.IsNullOrEmpty(name) || name == "." || name == "..")
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(searchPattern))
                        {
                            if (searchRegex != null)
                            {
                                if (!searchRegex.IsMatch(name))
                                {
                                    continue;
                                }
                            }
                            else if (!name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }

                        byte dType = Marshal.ReadByte(entryPtr, 18);
                        string fullPath = Path.Combine(path, name);
                        bool isDir = dType == 4;
                        bool isSymlink = dType == 10 || (dType == 0 && IsSymlinkPath(fullPath));

                        if (dType == 0 || dType == 10)
                        {
                            try
                            {
                                isDir = Directory.Exists(fullPath);
                            }
                            catch
                            {
                                isDir = false;
                            }
                        }

                        if (isDir)
                        {
                            DateTime creationTime = default;
                            DateTime lastAccessTime = default;
                            DateTime lastWriteTime = default;
                            FileAttributes attributes = default;
                            UnixFileMode? unixMode = null;

                            if (!isRemote)
                            {
                                try
                                {
                                    DirectoryInfo dirInfo = new(fullPath);
                                    attributes = dirInfo.Attributes;
                                    creationTime = dirInfo.CreationTime;
                                    lastAccessTime = dirInfo.LastAccessTime;
                                    lastWriteTime = dirInfo.LastWriteTime;
                                    unixMode = dirInfo.UnixFileMode;
                                }
                                catch
                                {
                                }
                            }

                            FolderEntry folder = new()
                            {
                                FullName = name,
                                FullPath = fullPath,
                                IsHidden = (attributes & FileAttributes.Hidden) != 0 || name.StartsWith('.'),
                                IsEncrypted = (attributes & FileAttributes.Encrypted) != 0,
                                IsCompressed = (attributes & FileAttributes.Compressed) != 0,
                                IsReadOnly = (attributes & FileAttributes.ReadOnly) != 0,
                                IsSystem = (attributes & FileAttributes.System) != 0,
                                IsSymlink = isSymlink,
                                CreationTime = creationTime,
                                LastAccessTime = lastAccessTime,
                                LastWriteTime = lastWriteTime,
                                UnixMode = unixMode,
                            };
                            dirList.Add(folder);
                        }
                        else
                        {
                            long fileSize = 0;
                            DateTime creationTime = default;
                            DateTime lastAccessTime = default;
                            DateTime lastWriteTime = default;
                            FileAttributes attributes = default;
                            UnixFileMode? unixMode = null;
                            bool isExecutable = false;

                            if (!isRemote)
                            {
                                try
                                {
                                    FileInfo fileInfo = new(fullPath);
                                    fileSize = fileInfo.Length;
                                    attributes = fileInfo.Attributes;
                                    creationTime = fileInfo.CreationTime;
                                    lastAccessTime = fileInfo.LastAccessTime;
                                    lastWriteTime = fileInfo.LastWriteTime;
                                    unixMode = fileInfo.UnixFileMode;
                                    if (unixMode.HasValue)
                                    {
                                        isExecutable = (unixMode.Value & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
                                    }
                                }
                                catch
                                {
                                }
                            }
                            else
                            {
                                try
                                {
                                    fileSize = new FileInfo(fullPath).Length;
                                }
                                catch
                                {
                                }
                            }

                            FileEntry file = new()
                            {
                                Name = Path.GetFileNameWithoutExtension(fullPath),
                                Extension = Path.GetExtension(fullPath),
                                FullName = name,
                                FullPath = fullPath,
                                FileSize = fileSize,
                                IsExecutable = isExecutable,
                                IsHidden = (attributes & FileAttributes.Hidden) != 0 || name.StartsWith('.'),
                                IsEncrypted = (attributes & FileAttributes.Encrypted) != 0,
                                IsCompressed = (attributes & FileAttributes.Compressed) != 0,
                                IsReadOnly = false,
                                IsSystem = (attributes & FileAttributes.System) != 0,
                                IsSymlink = isSymlink,
                                CreationTime = creationTime,
                                LastAccessTime = lastAccessTime,
                                LastWriteTime = lastWriteTime,
                                UnixMode = unixMode,
                            };
                            PopulateFileMetadataFast(file);
                            fileList.Add(file);
                        }
                    }
                }
                finally
                {
                    CloseDir(dirp);
                }

                dirList.Sort((a, b) => comparer.Compare(a.FullName, b.FullName));
                fileList.Sort((a, b) => comparer.Compare(a.FullName, b.FullName));

                foreach (FolderEntry folder in dirList)
                {
                    token.ThrowIfCancellationRequested();
                    yield return folder;
                }

                foreach (FileEntry file in fileList)
                {
                    token.ThrowIfCancellationRequested();
                    yield return file;
                }

                yield break;
            }
        }

        DirectoryInfo rootDir = new(path);
        EnumerationOptions options = new()
        {
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        try
        {
            foreach (FileSystemInfo info in rootDir.EnumerateFileSystemInfos("*", options))
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    string name = info.Name;
                    if (!string.IsNullOrEmpty(searchPattern))
                    {
                        if (searchRegex != null)
                        {
                            if (!searchRegex.IsMatch(name))
                            {
                                continue;
                            }
                        }
                        else if (!name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    FileAttributes attributes = default;
                    try
                    {
                        attributes = info.Attributes;
                    }
                    catch
                    {
                    }

                    DateTime creationTime = default;
                    DateTime lastAccessTime = default;
                    DateTime lastWriteTime = default;
                    try { creationTime = info.CreationTime; } catch { }
                    try { lastAccessTime = info.LastAccessTime; } catch { }
                    try { lastWriteTime = info.LastWriteTime; } catch { }

                    UnixFileMode? unixMode = null;
                    try
                    {
                        unixMode = info.UnixFileMode;
                    }
                    catch
                    {
                    }

                    if (info is DirectoryInfo dirInfo)
                    {
                        FolderEntry folder = new()
                        {
                            FullName = name,
                            FullPath = dirInfo.FullName,
                            IsHidden = (attributes & FileAttributes.Hidden) != 0 || name.StartsWith('.'),
                            IsEncrypted = (attributes & FileAttributes.Encrypted) != 0,
                            IsCompressed = (attributes & FileAttributes.Compressed) != 0,
                            IsReadOnly = (attributes & FileAttributes.ReadOnly) != 0,
                            IsSystem = (attributes & FileAttributes.System) != 0,
                            CreationTime = creationTime,
                            LastAccessTime = lastAccessTime,
                            LastWriteTime = lastWriteTime,
                            UnixMode = unixMode,
                        };
                        dirList.Add(folder);
                    }
                    else if (info is FileInfo fileInfo)
                    {
                        bool isExecutable = false;
                        if (unixMode.HasValue && (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
                        {
                            isExecutable = (unixMode.Value & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
                        }

                        long fileSize = 0;
                        try
                        {
                            fileSize = fileInfo.Length;
                        }
                        catch
                        {
                        }

                        bool isReadOnly = false;
                        try
                        {
                            isReadOnly = fileInfo.IsReadOnly;
                        }
                        catch
                        {
                        }

                        FileEntry file = new()
                        {
                            Name = Path.GetFileNameWithoutExtension(fileInfo.FullName),
                            Extension = fileInfo.Extension,
                            FullName = name,
                            FullPath = fileInfo.FullName,
                            FileSize = fileSize,
                            IsExecutable = isExecutable,
                            IsHidden = (attributes & FileAttributes.Hidden) != 0 || name.StartsWith('.'),
                            IsEncrypted = (attributes & FileAttributes.Encrypted) != 0,
                            IsCompressed = (attributes & FileAttributes.Compressed) != 0,
                            IsReadOnly = isReadOnly,
                            IsSystem = (attributes & FileAttributes.System) != 0,
                            CreationTime = creationTime,
                            LastAccessTime = lastAccessTime,
                            LastWriteTime = lastWriteTime,
                            UnixMode = unixMode,
                        };
                        PopulateFileMetadataFast(file);
                        fileList.Add(file);
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        dirList.Sort((a, b) => comparer.Compare(a.FullName, b.FullName));
        fileList.Sort((a, b) => comparer.Compare(a.FullName, b.FullName));

        foreach (FolderEntry folder in dirList)
        {
            token.ThrowIfCancellationRequested();
            yield return folder;
        }

        foreach (FileEntry file in fileList)
        {
            token.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    private static void UpdateAttributes(IFileSystemEntry entry)
    {
        switch (entry)
        {
            case FileEntry file:
            {
                FileInfo info = new(file.FullPath);

                try
                {
                    file.IsHidden = (info.Attributes & FileAttributes.Hidden) != 0 || file.FullName.StartsWith('.');
                    file.IsEncrypted = (info.Attributes & FileAttributes.Encrypted) != 0;
                    file.IsCompressed = (info.Attributes & FileAttributes.Compressed) != 0;
                    file.IsReadOnly = info.IsReadOnly;
                    file.IsSystem = (info.Attributes & FileAttributes.System) != 0;
                    file.CreationTime = info.CreationTime;
                    file.LastAccessTime = info.LastAccessTime;
                    file.LastWriteTime = info.LastWriteTime;
                    file.FileSize = info.Length;
                    file.UnixMode = info.UnixFileMode;
                }
                catch
                {
                }

                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    if (file.UnixMode.HasValue)
                    {
                        file.IsExecutable = (file.UnixMode.Value & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
                    }
                }
                else
                {
                    file.IsExecutable = FileUtils.IsFileExecutable(file.FullPath);
                }

                break;
            }

            case FolderEntry folder:
            {
                DirectoryInfo info = new(folder.FullPath);

                try
                {
                    folder.IsHidden = (info.Attributes & FileAttributes.Hidden) != 0 || folder.FullName.StartsWith('.');
                    folder.IsEncrypted = (info.Attributes & FileAttributes.Encrypted) != 0;
                    folder.IsCompressed = (info.Attributes & FileAttributes.Compressed) != 0;
                    folder.IsReadOnly = (info.Attributes & FileAttributes.ReadOnly) != 0;
                    folder.IsSystem = (info.Attributes & FileAttributes.System) != 0;
                    folder.CreationTime = info.CreationTime;
                    folder.LastAccessTime = info.LastAccessTime;
                    folder.LastWriteTime = info.LastWriteTime;
                    folder.UnixMode = info.UnixFileMode;
                }
                catch
                {
                }

                break;
            }
        }
    }

    private static void UpdateFolder(string path)
    {

    }

    private static void PopulateFileMetadataFast(FileEntry file)
    {
        string? mime = MimeUtils.GetMimeTypeFast(file.FullPath);
        if (file.IsExecutable && !IsDocumentMime(mime))
        {
            file.FileType = FileType.Executable;
            return;
        }

        if (mime is null)
        {
            file.NeedsAccurateTypeCheck = true;
            file.FileType = FileType.Unknown;
            
            return;
        }

        file.FileType = mime switch
        {
            { } s when MimeUtils.IsArchiveMime(s) => FileType.Archive,
            { } s when s.StartsWith("text/") => FileType.Text,
            { } s when s.StartsWith("image/") => FileType.Image,
            { } s when s.StartsWith("audio/") => FileType.Audio,
            { } s when s.StartsWith("video/") => FileType.Video,
            "application/vnd.debian.binary-package" => FileType.Deb,
            _ => FileType.Unknown,
        };

        if (file.FileType == FileType.Unknown)
        {
            file.NeedsAccurateTypeCheck = true;
        }
    }

    private static bool IsDocumentMime(string? mime)
    {
        if (string.IsNullOrEmpty(mime))
        {
            return false;
        }

        return mime.StartsWith("image/")
               || mime.StartsWith("audio/")
               || mime.StartsWith("video/")
               || mime.StartsWith("font/")
               || mime == "application/pdf"
               || mime == "application/vnd.debian.binary-package"
               || MimeUtils.IsArchiveMime(mime);
    }

    private static void PopulateFileMetadataAccurate(FileEntry file)
    {
        string? mime = MimeUtils.GetMimeTypeAccurate(file.FullPath);
        switch (mime)
        {
            case { } s when MimeUtils.IsArchiveMime(s):
                file.FileType = FileType.Archive;
            break;
            
            case { } s when s.StartsWith("text/"):
                file.FileType = FileType.Text;
            break;
            
            case { } s when s.StartsWith("image/"):
                file.FileType = FileType.Image;
            break;
            
            case { } s when s.StartsWith("audio/"):
                file.FileType = FileType.Audio;
            break;
            
            case { } s when s.StartsWith("video/"):
                file.FileType = FileType.Video;
            break;
            
            case "application/vnd.debian.binary-package":
                file.FileType = FileType.Deb;
            break;
            
            default:
                file.FileType = FileType.Unknown;
            break;
        }

        file.IsAccurateType = true;
    }

    private static bool IsSymlinkPath(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryEnumerateWithGio(
        string path,
        string? searchPattern,
        Regex? searchRegex,
        CancellationToken token,
        List<FolderEntry> dirList,
        List<FileEntry> fileList)
    {
        try
        {
            IntPtr file = GFileNewForPath(path);
            if (file == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                IntPtr enumerator = GFileEnumerateChildren(file, "standard::name,standard::type,standard::size,time::modified", 0, IntPtr.Zero, out IntPtr error);
                if (enumerator == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        IntPtr info = GFileEnumeratorNextFile(enumerator, IntPtr.Zero, out IntPtr nextError);
                        if (info == IntPtr.Zero)
                        {
                            break;
                        }

                        try
                        {
                            IntPtr namePtr = GFileInfoGetName(info);
                            string? name = Marshal.PtrToStringUTF8(namePtr);
                            if (string.IsNullOrEmpty(name) || name == "." || name == "..")
                            {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(searchPattern))
                            {
                                if (searchRegex != null)
                                {
                                    if (!searchRegex.IsMatch(name))
                                    {
                                        continue;
                                    }
                                }
                                else if (!name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }
                            }

                            int fileType = GFileInfoGetFileType(info);
                            string fullPath = Path.Combine(path, name);
                            bool isHidden = name.StartsWith('.');

                            ulong mtimeSec = GFileInfoGetAttributeUInt64(info, "time::modified");
                            DateTime lastWriteTime = mtimeSec > 0 ? DateTimeOffset.FromUnixTimeSeconds((long) mtimeSec).LocalDateTime : default;

                            if (fileType == 2)
                            {
                                FolderEntry folder = new()
                                {
                                    FullName = name,
                                    FullPath = fullPath,
                                    IsHidden = isHidden,
                                    LastWriteTime = lastWriteTime,
                                };
                                dirList.Add(folder);
                            }
                            else
                            {
                                long size = GFileInfoGetSize(info);
                                FileEntry fileEntry = new()
                                {
                                    Name = Path.GetFileNameWithoutExtension(fullPath),
                                    Extension = Path.GetExtension(fullPath),
                                    FullName = name,
                                    FullPath = fullPath,
                                    FileSize = size,
                                    IsHidden = isHidden,
                                    LastWriteTime = lastWriteTime,
                                };
                                PopulateFileMetadataFast(fileEntry);
                                fileList.Add(fileEntry);
                            }
                        }
                        finally
                        {
                            GObjectUnref(info);
                        }
                    }

                    return true;
                }
                finally
                {
                    GObjectUnref(enumerator);
                }
            }
            finally
            {
                GObjectUnref(file);
            }
        }
        catch
        {
            return false;
        }
    }
}