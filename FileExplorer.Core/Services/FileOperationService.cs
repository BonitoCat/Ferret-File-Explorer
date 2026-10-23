using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Models;
using FileExplorer.Core.Utilities;
using FileLib;
using LoggerLib;
using SearchOption = System.IO.SearchOption;

namespace FileExplorer.Core.Services;

public class FileOperationService
{
    private static readonly TimeSpan DeleteProgressInterval = TimeSpan.FromMilliseconds(50);

    public void Delete(IEnumerable<string> paths, bool permanent = false)
    {
        Delete(paths, permanent, null, CancellationToken.None);
    }

    public void Delete(IEnumerable<string> paths, bool permanent, IProgress<FileTransferProgress>? progress, CancellationToken token)
    {
        List<string> pathList = paths.ToList();
        if (pathList.Count == 0) return;

        DeleteTracker tracker = new(progress, permanent ? CountEntries(pathList, token) : pathList.Count);
        foreach (string path in pathList)
        {
            token.ThrowIfCancellationRequested();
            bool isDirectory = Directory.Exists(path);
            if (!isDirectory && !File.Exists(path) && new FileInfo(path).LinkTarget == null)
            {
                continue;
            }

            bool hasColors = isDirectory && FolderColorStore.HasAny(path);
            if (permanent)
            {
                DeleteEntry(path, tracker, token);
            }
            else
            {
                tracker.Report(PathManager.GetName(path));
                MoveToTrash(path);
                tracker.Advance(PathManager.GetName(path));
            }

            if (hasColors)
            {
                ForgetFolderColors(path, permanent);
            }
        }

        tracker.Flush();
        _ = Logger.LogI($"{(permanent ? "Permanently deleted" : "Moved to trash")} {pathList.Count} items");
    }

    private static readonly EnumerationOptions ChildEnumeration = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    private static int CountEntries(List<string> paths, CancellationToken token)
    {
        int count = paths.Count;
        Stack<string> pending = new(paths.Where(IsRealDirectory));
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            List<(string Path, bool IsDirectory)> children;
            try
            {
                children = ListChildren(pending.Pop());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            count += children.Count;
            foreach ((string childPath, bool isDirectory) in children)
            {
                if (isDirectory && !IsLink(childPath))
                {
                    pending.Push(childPath);
                }
            }
        }

        return count;
    }

    private static void DeleteEntry(string path, DeleteTracker tracker, CancellationToken token)
    {
        DeleteEntry(path, Directory.Exists(path), tracker, token);
    }

    private static void DeleteEntry(string path, bool isDirectory, DeleteTracker tracker, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string name = PathManager.GetName(path);
        if (!isDirectory)
        {
            File.Delete(path);
            tracker.Advance(name);
            return;
        }

        if (!IsLink(path))
        {
            foreach ((string childPath, bool childIsDirectory) in ListChildren(path))
            {
                DeleteEntry(childPath, childIsDirectory, tracker, token);
            }
        }

        Directory.Delete(path);
        tracker.Advance(name);
    }

    private static List<(string Path, bool IsDirectory)> ListChildren(string directory)
    {
        return new FileSystemEnumerable<(string Path, bool IsDirectory)>(
            directory,
            (ref FileSystemEntry entry) => (entry.ToFullPath(), entry.IsDirectory),
            ChildEnumeration).ToList();
    }

    private static bool IsRealDirectory(string path)
    {
        return Directory.Exists(path) && !IsLink(path);
    }

    private static bool IsLink(string path)
    {
        return new FileInfo(path).LinkTarget != null;
    }

    private sealed class DeleteTracker
    {
        private readonly IProgress<FileTransferProgress>? _progress;
        private readonly int _total;
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();
        private int _deleted;
        private string _currentName = "";

        public DeleteTracker(IProgress<FileTransferProgress>? progress, int total)
        {
            _progress = progress;
            _total = total;
            Report("");
        }

        public void Advance(string name)
        {
            _deleted++;
            _currentName = name;
            if (_sinceReport.Elapsed >= DeleteProgressInterval)
            {
                Report(name);
            }
        }

        public void Report(string name)
        {
            _sinceReport.Restart();
            _progress?.Report(new()
            {
                TransferredItems = Math.Min(_deleted, _total),
                TotalItems = _total,
                CurrentItemName = name,
            });
        }

        public void Flush()
        {
            Report(_currentName);
        }
    }

    public List<string> RestoreFromTrash(IEnumerable<string> trashedPaths)
    {
        List<string> failed = new();
        int restored = 0;

        foreach (string trashedPath in trashedPaths)
        {
            try
            {
                if (TryRestoreFromTrash(trashedPath))
                {
                    restored++;
                    continue;
                }
            }
            catch (Exception ex)
            {
                _ = Logger.LogE($"Failed to restore {trashedPath}: {ex.Message}");
            }

            failed.Add(trashedPath);
        }

        _ = Logger.LogI($"Restored {restored} items from trash, {failed.Count} failed");
        return failed;
    }

    private static void MoveToTrash(string path)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "gio",
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("trash");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(path);

        Process.Start(psi)?.WaitForExit();
    }

    private static void ForgetFolderColors(string path, bool permanent)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        string? trashedPath = permanent ? null : FindTrashedPath(path);
        if (trashedPath != null)
        {
            FolderColorStore.Move(path, trashedPath);
            return;
        }

        FolderColorStore.Remove(path);
    }

    private static string? FindTrashedPath(string originalPath)
    {
        string fullPath = NormalizePath(originalPath);
        foreach (string trashDir in GetTrashDirs(fullPath))
        {
            string infoDir = Path.Combine(trashDir, "info");
            if (!Directory.Exists(infoDir))
            {
                continue;
            }

            try
            {
                string? infoFile = new DirectoryInfo(infoDir).EnumerateFiles("*.trashinfo")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Where(f => ReadTrashInfoPath(f.FullName, trashDir) == fullPath)
                    .Select(f => f.Name)
                    .FirstOrDefault();
                if (infoFile != null)
                {
                    return Path.Combine(trashDir, "files", Path.GetFileNameWithoutExtension(infoFile));
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> GetTrashDirs(string path)
    {
        yield return Path.Combine(DirectoryHelper.GetAppDataDirPath(false), "Trash");

        uint uid = UnixStat.CurrentUid;
        for (string? dir = Path.GetDirectoryName(path); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
        {
            yield return Path.Combine(dir, $".Trash-{uid}");
            yield return Path.Combine(dir, ".Trash", uid.ToString());
        }
    }

    private static bool TryRestoreFromTrash(string trashedPath)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(trashedPath);
        string? filesDir = Path.GetDirectoryName(trimmed);
        if (filesDir == null || Path.GetFileName(filesDir) != "files")
        {
            return false;
        }

        string? trashDir = Path.GetDirectoryName(filesDir);
        if (trashDir == null)
        {
            return false;
        }

        string infoPath = Path.Combine(trashDir, "info", Path.GetFileName(trimmed) + ".trashinfo");
        string? originalPath = ReadTrashInfoPath(infoPath, trashDir);
        if (originalPath == null)
        {
            return false;
        }

        if (File.Exists(originalPath) || Directory.Exists(originalPath))
        {
            _ = Logger.LogW($"Cannot restore {trimmed}: {originalPath} already exists");
            return false;
        }

        string? parent = Path.GetDirectoryName(originalPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (Directory.Exists(trimmed))
        {
            Directory.Move(trimmed, originalPath);
            FolderColorStore.Move(trimmed, originalPath);
        }
        else
        {
            File.Move(trimmed, originalPath);
        }

        File.Delete(infoPath);
        return true;
    }

    private static string? ReadTrashInfoPath(string infoPath, string trashDir)
    {
        if (!File.Exists(infoPath))
        {
            return null;
        }

        string? rawPath = File.ReadLines(infoPath)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("Path=", StringComparison.Ordinal))
            .Select(l => l.Substring("Path=".Length))
            .FirstOrDefault();
        if (string.IsNullOrEmpty(rawPath))
        {
            return null;
        }

        string decoded = Uri.UnescapeDataString(rawPath);
        if (Path.IsPathRooted(decoded))
        {
            return decoded;
        }

        string topDir = Path.GetFileName(trashDir).StartsWith(".Trash-", StringComparison.Ordinal)
            ? Path.GetDirectoryName(trashDir) ?? "/"
            : Path.GetDirectoryName(Path.GetDirectoryName(trashDir)) ?? "/";
        return Path.Combine(topDir, decoded);
    }

    public void Rename(string oldPath, string newName)
    {
        string? dir = Path.GetDirectoryName(oldPath);
        if (dir == null) return;

        string newPath = Path.Combine(dir, newName);

        if (Directory.Exists(oldPath))
        {
            Directory.Move(oldPath, newPath);
            FolderColorStore.Move(oldPath, newPath);
            _ = Logger.LogI("Directory renamed");
        }
        else if (File.Exists(oldPath))
        {
            File.Move(oldPath, newPath);
            _ = Logger.LogI("File renamed");
        }
    }

    public void CreateFolder(string path)
    {
        Directory.CreateDirectory(path);
        _ = Logger.LogI("Created new directory");
    }

    public void CreateFile(string path)
    {
        File.Create(path).Dispose();
        _ = Logger.LogI("Created new file");
    }

    public void Duplicate(string path)
    {
        string? newPath = GetDuplicatePath(path);
        if (newPath == null)
        {
            return;
        }

        if (File.Exists(path))
        {
            File.Copy(path, newPath);
            _ = Logger.LogI("Duplicated item");
        }
        else if (Directory.Exists(path))
        {
            CopyDirectory(path, newPath);
            FolderColorStore.Copy(path, newPath);
            _ = Logger.LogI("Duplicated item");
        }
    }

    public static string? GetDuplicatePath(string path)
    {
        bool isFile = File.Exists(path);
        if (!isFile && !Directory.Exists(path))
        {
            return null;
        }

        string? dir = Path.GetDirectoryName(path);
        if (dir == null)
        {
            return null;
        }

        string ext = isFile ? Path.GetExtension(path) : "";
        string name = isFile ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
        int count = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(dir, $"{name} ({count}){ext}");
            count++;
        }
        while (File.Exists(newPath) || Directory.Exists(newPath));

        return newPath;
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
        }

        foreach (string newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourceDir, destinationDir), true);
        }
    }

    public void Paste(string destDir, ClipboardMode mode, IEnumerable<string> sourcePaths)
    {
        int count = 0;

        foreach (string path in sourcePaths)
        {
            string fileName = Path.GetFileName(path);
            string destPath = Path.Combine(destDir, fileName);

            if (path == destPath && mode == ClipboardMode.Cut)
            {
                continue;
            }

            if (File.Exists(destPath) || Directory.Exists(destPath))
            {
                string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                int dupCount = 1;

                do
                {
                    destPath = Path.Combine(destDir, $"{nameOnly} ({dupCount}){ext}");
                    dupCount++;
                }
                while (File.Exists(destPath) || Directory.Exists(destPath));
            }

            if (File.Exists(path))
            {
                if (mode == ClipboardMode.Cut)
                {
                    File.Move(path, destPath);
                }
                else
                {
                    File.Copy(path, destPath);
                }

                count++;
            }
            else if (Directory.Exists(path))
            {
                if (mode == ClipboardMode.Cut)
                {
                    Directory.Move(path, destPath);
                    FolderColorStore.Move(path, destPath);
                }
                else
                {
                    CopyDirectory(path, destPath);
                    FolderColorStore.Copy(path, destPath);
                }

                count++;
            }
        }

        _ = Logger.LogI($"Pasted {count} items (mode: {mode})");
    }

    public async Task<List<TransferStep>> PlanTransferAsync(
        string destDir,
        ClipboardMode mode,
        IEnumerable<string> sourcePaths,
        Func<TransferConflict, Task<ConflictResolution>>? resolveConflict,
        bool allowMerge = true)
    {
        List<string> pathList = sourcePaths.Where(PathExists).ToList();
        List<TransferStep> steps = new();
        ConflictState conflicts = new(resolveConflict, mode == ClipboardMode.Copy, pathList.Count > 1 || pathList.Any(Directory.Exists), allowMerge);
        string normDest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar);

        foreach (string path in pathList)
        {
            string normSrc = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string destPath = Path.Combine(destDir, Path.GetFileName(normSrc));
            if (Directory.Exists(path) && (string.Equals(normSrc, normDest, StringComparison.Ordinal) || normDest.StartsWith(normSrc + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                continue;
            }

            if (mode == ClipboardMode.Cut && string.Equals(Path.GetDirectoryName(normSrc), normDest, StringComparison.Ordinal))
            {
                continue;
            }

            if (!PathExists(destPath))
            {
                steps.Add(new(path, destPath, false));
                continue;
            }

            if (IsSamePath(path, destPath))
            {
                steps.Add(new(path, GetUniqueDestinationPath(destDir, Path.GetFileName(normSrc)), false));
                continue;
            }

            ConflictAction action = await conflicts.ResolveAsync(path, destPath);
            switch (action)
            {
                case ConflictAction.Cancel:
                    throw new OperationCanceledException();

                case ConflictAction.Skip:
                    continue;

                case ConflictAction.Duplicate:
                    steps.Add(new(path, GetUniqueDestinationPath(destDir, Path.GetFileName(destPath)), false));
                    continue;

                case ConflictAction.Replace:
                    if (IsInside(path, destPath))
                    {
                        throw new IOException($"Cannot replace \"{Path.GetFileName(destPath)}\" because it contains the item being transferred.");
                    }

                    steps.Add(new(path, destPath, true));
                    continue;

                default:
                    steps.Add(new(path, destPath, false));
                    continue;
            }
        }

        return steps;
    }

    public Task<List<string>> TransferAsync(
        string destDir,
        ClipboardMode mode,
        IEnumerable<string> sourcePaths,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return TransferAsync(destDir, mode, sourcePaths, (Func<TransferConflict, Task<ConflictResolution>>?) null, progress, cancellationToken);
    }

    public async Task<List<string>> TransferAsync(
        string destDir,
        ClipboardMode mode,
        IEnumerable<string> sourcePaths,
        Func<TransferConflict, Task<ConflictResolution>>? resolveConflict,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<string> targets = new();
        if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
        {
            return targets;
        }

        List<string> pathList = sourcePaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (pathList.Count == 0)
        {
            return targets;
        }

        await Logger.LogI($"Starting transfer of {pathList.Count} items (mode: {mode})");

        int totalItems = 0;
        long totalBytes = 0;

        foreach (string path in pathList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(path))
            {
                totalItems++;
                try
                {
                    FileInfo fi = new(path);
                    totalBytes += fi.Length;
                }
                catch
                {
                }
            }
            else if (Directory.Exists(path))
            {
                totalItems++;
                try
                {
                    foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        totalItems++;
                        try
                        {
                            FileInfo fi = new(file);
                            totalBytes += fi.Length;
                        }
                        catch
                        {
                        }
                    }

                    foreach (string _ in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    {
                        totalItems++;
                    }
                }
                catch
                {
                }
            }
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        int transferredItems = 0;
        long transferredBytes = 0;
        long lastReportBytes = 0;
        double lastReportTime = 0;
        double speed = 0;

        void ReportProgress(string currentItemName)
        {
            double elapsed = stopwatch.Elapsed.TotalSeconds;
            double timeDelta = elapsed - lastReportTime;
            if (timeDelta >= 0.5)
            {
                double currentSpeed = (transferredBytes - lastReportBytes) / timeDelta;
                speed = speed <= 0 ? currentSpeed : speed * 0.7 + currentSpeed * 0.3;
                lastReportTime = elapsed;
                lastReportBytes = transferredBytes;
            }

            long remainingBytes = Math.Max(0, totalBytes - transferredBytes);
            TimeSpan? remaining = speed > 1024 ? TimeSpan.FromSeconds((double) remainingBytes / speed) : null;

            progress?.Report(new()
            {
                TransferredItems = transferredItems,
                TotalItems = totalItems,
                TransferredBytes = transferredBytes,
                TotalBytes = totalBytes,
                BytesPerSecond = speed,
                EstimatedRemaining = remaining,
                CurrentItemName = currentItemName,
                IsCopy = mode == ClipboardMode.Copy
            });
        }

        ReportProgress(string.Empty);

        byte[] buffer = new byte[262144];
        ConflictState conflicts = new(resolveConflict, mode == ClipboardMode.Copy, pathList.Count > 1 || pathList.Any(Directory.Exists));

        foreach (string path in pathList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fileName = Path.GetFileName(path);
            string destPath = Path.Combine(destDir, fileName);

            if (Directory.Exists(path))
            {
                string normSrc = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                string normDest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar);

                if (string.Equals(normSrc, normDest, StringComparison.OrdinalIgnoreCase) ||
                    normDest.StartsWith(normSrc + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (mode == ClipboardMode.Cut)
            {
                string? srcParent = Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
                if (srcParent != null && string.Equals(Path.GetFullPath(srcParent).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            if (path == destPath && mode == ClipboardMode.Cut)
            {
                continue;
            }

            if (PathExists(destPath))
            {
                if (IsSamePath(path, destPath))
                {
                    destPath = GetUniqueDestinationPath(destDir, fileName);
                }
                else
                {
                    ConflictAction action = await conflicts.ResolveAsync(path, destPath);
                    if (action == ConflictAction.Skip)
                    {
                        continue;
                    }

                    destPath = ApplyConflictAction(action, path, destPath);
                }
            }

            targets.Add(destPath);
            if (File.Exists(path))
            {
                await TransferSingleFileAsync(path, destPath, mode, buffer, () => transferredBytes, v => transferredBytes = v, () => transferredItems, v => transferredItems = v, ReportProgress, cancellationToken);
            }
            else if (Directory.Exists(path))
            {
                await TransferDirectoryRecursiveAsync(path, destPath, mode, buffer, () => transferredBytes, v => transferredBytes = v, () => transferredItems, v => transferredItems = v, ReportProgress, conflicts, cancellationToken);
            }
        }

        progress?.Report(new()
        {
            TransferredItems = totalItems,
            TotalItems = totalItems,
            TransferredBytes = totalBytes,
            TotalBytes = totalBytes,
            BytesPerSecond = 0,
            EstimatedRemaining = TimeSpan.Zero,
            CurrentItemName = string.Empty,
            IsCopy = mode == ClipboardMode.Copy
        });

        await Logger.LogI($"Transfer completed for {totalItems} items (mode: {mode})");
        return targets;
    }

    private async Task TransferSingleFileAsync(
        string sourceFile,
        string destFile,
        ClipboardMode mode,
        byte[] buffer,
        Func<long> getBytes,
        Action<long> setBytes,
        Func<int> getItems,
        Action<int> setItems,
        Action<string> reportProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        long fileSize = 0;
        try
        {
            FileInfo fi = new(sourceFile);
            fileSize = fi.Length;
        }
        catch
        {
        }

        if (GioFileCopier.ShouldUse(sourceFile, destFile))
        {
            long startBytes = getBytes();
            await Task.Run(() => GioFileCopier.Copy(sourceFile, destFile, copied =>
            {
                setBytes(startBytes + copied);
                reportProgress(Path.GetFileName(sourceFile));
            }, cancellationToken), cancellationToken);

            setBytes(startBytes + fileSize);
            if (mode == ClipboardMode.Cut)
            {
                try
                {
                    File.Delete(sourceFile);
                }
                catch
                {
                }
            }

            setItems(getItems() + 1);
            reportProgress(Path.GetFileName(sourceFile));
            return;
        }

        if (mode == ClipboardMode.Cut)
        {
            try
            {
                File.Move(sourceFile, destFile);
                setBytes(getBytes() + fileSize);
                setItems(getItems() + 1);
                reportProgress(Path.GetFileName(sourceFile));
                return;
            }
            catch (IOException)
            {
            }
        }

        string? destParent = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(destParent) && !Directory.Exists(destParent))
        {
            Directory.CreateDirectory(destParent);
        }

        try
        {
            await using FileStream sourceStream = new(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, true);
            await using FileStream destStream = new(destFile, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);

            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await destStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                setBytes(getBytes() + bytesRead);
                reportProgress(Path.GetFileName(sourceFile));
            }
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(destFile))
            {
                try
                {
                    File.Delete(destFile);
                }
                catch
                {
                }
            }

            throw;
        }

        if (mode == ClipboardMode.Cut)
        {
            try
            {
                File.Delete(sourceFile);
            }
            catch
            {
            }
        }

        setItems(getItems() + 1);
        reportProgress(Path.GetFileName(sourceFile));
    }

    private async Task<bool> TransferDirectoryRecursiveAsync(
        string sourceDir,
        string destDir,
        ClipboardMode mode,
        byte[] buffer,
        Func<long> getBytes,
        Action<long> setBytes,
        Func<int> getItems,
        Action<int> setItems,
        Action<string> reportProgress,
        ConflictState conflicts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (mode == ClipboardMode.Cut && !PathExists(destDir))
        {
            try
            {
                Directory.Move(sourceDir, destDir);
                FolderColorStore.Move(sourceDir, destDir);
                long dirBytes = 0;
                int dirItems = 1;
                try
                {
                    foreach (string f in Directory.EnumerateFiles(destDir, "*", SearchOption.AllDirectories))
                    {
                        dirItems++;
                        FileInfo fi = new(f);
                        dirBytes += fi.Length;
                    }

                    foreach (string _ in Directory.EnumerateDirectories(destDir, "*", SearchOption.AllDirectories))
                    {
                        dirItems++;
                    }
                }
                catch
                {
                }

                setBytes(getBytes() + dirBytes);
                setItems(getItems() + dirItems);
                reportProgress(Path.GetFileName(sourceDir));
                return true;
            }
            catch (IOException)
            {
            }
        }

        Directory.CreateDirectory(destDir);
        if (mode == ClipboardMode.Copy)
        {
            FolderColorStore.Copy(sourceDir, destDir, false);
        }

        setItems(getItems() + 1);
        reportProgress(Path.GetFileName(sourceDir));

        bool complete = true;
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string subDest = Path.Combine(destDir, Path.GetFileName(subDir));
            if (PathExists(subDest) && !Directory.Exists(subDest))
            {
                ConflictAction action = await conflicts.ResolveAsync(subDir, subDest);
                if (action == ConflictAction.Skip)
                {
                    complete = false;
                    continue;
                }

                subDest = ApplyConflictAction(action, subDir, subDest);
            }

            complete &= await TransferDirectoryRecursiveAsync(subDir, subDest, mode, buffer, getBytes, setBytes, getItems, setItems, reportProgress, conflicts, cancellationToken);
        }

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileDest = Path.Combine(destDir, Path.GetFileName(file));
            if (PathExists(fileDest))
            {
                ConflictAction action = await conflicts.ResolveAsync(file, fileDest);
                if (action == ConflictAction.Skip)
                {
                    complete = false;
                    continue;
                }

                fileDest = ApplyConflictAction(action, file, fileDest);
            }

            await TransferSingleFileAsync(file, fileDest, mode, buffer, getBytes, setBytes, getItems, setItems, reportProgress, cancellationToken);
        }

        if (mode == ClipboardMode.Cut && complete)
        {
            try
            {
                Directory.Delete(sourceDir, true);
            }
            catch
            {
            }

            FolderColorStore.Move(sourceDir, destDir);
        }

        return complete;
    }

    public async Task<(List<string> Created, List<string> Errors)> CreateSymlinksAsync(
        string destinationDirectory,
        IEnumerable<string> paths,
        Func<TransferConflict, Task<ConflictResolution>>? resolver)
    {
        List<string> sources = paths
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(NormalizePath)
            .ToList();
        ConflictState conflicts = new(resolver, true, sources.Count > 1, false);
        List<string> created = new();
        List<string> errors = new();

        foreach (string source in sources)
        {
            string linkPath = Path.Combine(destinationDirectory, Path.GetFileName(source));
            try
            {
                if (PathExists(linkPath))
                {
                    ConflictAction action = IsSamePath(source, linkPath) || IsInside(source, linkPath)
                        ? ConflictAction.Duplicate
                        : await conflicts.ResolveAsync(source, linkPath);
                    if (action == ConflictAction.Skip)
                    {
                        continue;
                    }

                    linkPath = ApplyConflictAction(action, source, linkPath);
                }

                File.CreateSymbolicLink(linkPath, source);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Logger.LogE($"Failed to create symlink {linkPath} -> {source}: {ex.Message}");
                errors.Add($"{Path.GetFileName(source)}: {ex.Message}");
                continue;
            }

            FolderColorStore.Remove(linkPath);
            FolderColorStore.Copy(source, linkPath);
            created.Add(linkPath);
        }

        return (created, errors);
    }

    public static string ResolveConflict(ConflictAction action, string sourcePath, string destPath)
    {
        return ApplyConflictAction(action, sourcePath, destPath);
    }

    private static string ApplyConflictAction(ConflictAction action, string sourcePath, string destPath)
    {
        switch (action)
        {
            case ConflictAction.Cancel:
                throw new OperationCanceledException();

            case ConflictAction.Duplicate:
                return GetUniqueDestinationPath(Path.GetDirectoryName(destPath)!, Path.GetFileName(destPath));

            case ConflictAction.Replace:
                if (IsSamePath(sourcePath, destPath) || IsInside(sourcePath, destPath))
                {
                    throw new IOException($"Cannot replace \"{Path.GetFileName(destPath)}\" because it contains the item being transferred.");
                }

                DeletePath(destPath);
                return destPath;

            default:
                return destPath;
        }
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget != null;
    }

    private static bool IsSamePath(string a, string b)
    {
        return string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.Ordinal);
    }

    private static bool IsInside(string path, string container)
    {
        return NormalizePath(path).StartsWith(NormalizePath(container) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }

    private static void DeletePath(string path)
    {
        FileInfo info = new(path);
        if (info.LinkTarget != null)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }

            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
            FolderColorStore.Remove(path);
            return;
        }

        File.Delete(path);
    }

    private sealed class ConflictState
    {
        private readonly Func<TransferConflict, Task<ConflictResolution>>? _resolver;
        private readonly bool _isCopy;
        private readonly bool _hasMultipleItems;
        private readonly bool _allowMerge;
        private ConflictAction? _applyToAll;

        public ConflictState(Func<TransferConflict, Task<ConflictResolution>>? resolver, bool isCopy, bool hasMultipleItems, bool allowMerge = true)
        {
            _resolver = resolver;
            _isCopy = isCopy;
            _hasMultipleItems = hasMultipleItems;
            _allowMerge = allowMerge;
        }

        public async Task<ConflictAction> ResolveAsync(string sourcePath, string destPath)
        {
            bool sourceIsDirectory = Directory.Exists(sourcePath);
            bool destIsDirectory = Directory.Exists(destPath);
            bool canMerge = _allowMerge && sourceIsDirectory && destIsDirectory;

            if (_applyToAll is ConflictAction all && (all != ConflictAction.Merge || canMerge))
            {
                return all;
            }

            if (_resolver == null)
            {
                return ConflictAction.Duplicate;
            }

            TransferConflict conflict = new(sourcePath, destPath, sourceIsDirectory, destIsDirectory, _isCopy, _hasMultipleItems, _allowMerge);
            ConflictResolution resolution = await _resolver(conflict);
            ConflictAction action = resolution.Action == ConflictAction.Merge && !canMerge ? ConflictAction.Replace : resolution.Action;
            if (resolution.ApplyToAll && action != ConflictAction.Cancel)
            {
                _applyToAll = action;
            }

            return action;
        }
    }

    private static string GetUniqueDestinationPath(string destDir, string fileName)
    {
        string destPath = Path.Combine(destDir, fileName);
        if (!File.Exists(destPath) && !Directory.Exists(destPath))
        {
            return destPath;
        }

        string nameOnly = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        int count = 1;

        do
        {
            destPath = Path.Combine(destDir, $"{nameOnly} ({count}){ext}");
            count++;
        }
        while (File.Exists(destPath) || Directory.Exists(destPath));

        return destPath;
    }
}
