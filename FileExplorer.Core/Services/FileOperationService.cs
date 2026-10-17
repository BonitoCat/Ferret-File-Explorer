using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Models;
using LoggerLib;
using SearchOption = System.IO.SearchOption;

namespace FileExplorer.Core.Services;

public class FileOperationService
{
    public void Delete(IEnumerable<string> paths, bool permanent = false)
    {
        List<string> pathList = paths.ToList();
        if (pathList.Count == 0) return;

        foreach (string path in pathList)
        {
            if (Directory.Exists(path))
            {
                if (permanent)
                {
                    Directory.Delete(path, true);
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "gio",
                        Arguments = $"trash \"{path}\"",
                        UseShellExecute = false
                    })?.WaitForExit();
                }
            }
            else if (File.Exists(path))
            {
                if (permanent)
                {
                    File.Delete(path);
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "gio",
                        Arguments = $"trash \"{path}\"",
                        UseShellExecute = false
                    })?.WaitForExit();
                }
            }
        }

        _ = Logger.LogI($"{(permanent ? "Permanently deleted" : "Moved to trash")} {pathList.Count} items");
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
        if (File.Exists(path))
        {
            string ext = Path.GetExtension(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string? dir = Path.GetDirectoryName(path);
            if (dir == null) return;

            int count = 1;
            string newPath;
            string destDir = Directory.GetParent(path)?.FullName ?? "/";

            do
            {
                newPath = Path.Combine(destDir, $"{name} ({count}){ext}");
                count++;
            }
            while (File.Exists(newPath) || Directory.Exists(newPath));

            File.Copy(path, newPath);
            _ = Logger.LogI("Duplicated item");
        }
        else if (Directory.Exists(path))
        {
            string name = Path.GetFileName(path);
            string? dir = Path.GetDirectoryName(path);
            if (dir == null) return;

            int count = 1;
            string newPath;
            string destDir = Directory.GetParent(path)?.FullName ?? "/";

            do
            {
                newPath = Path.Combine(destDir, $"{name} ({count})");
                count++;
            }
            while (File.Exists(newPath) || Directory.Exists(newPath));

            CopyDirectory(path, newPath);
            _ = Logger.LogI("Duplicated item");
        }
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
                }
                else
                {
                    CopyDirectory(path, destPath);
                }

                count++;
            }
        }

        _ = Logger.LogI($"Pasted {count} items (mode: {mode})");
    }

    public Task TransferAsync(
        string destDir,
        ClipboardMode mode,
        IEnumerable<string> sourcePaths,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return TransferAsync(destDir, mode, sourcePaths, (Func<TransferConflict, Task<ConflictResolution>>?) null, progress, cancellationToken);
    }

    public async Task TransferAsync(
        string destDir,
        ClipboardMode mode,
        IEnumerable<string> sourcePaths,
        Func<TransferConflict, Task<ConflictResolution>>? resolveConflict,
        IProgress<FileTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(destDir) || !Directory.Exists(destDir))
        {
            return;
        }

        List<string> pathList = sourcePaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (pathList.Count == 0)
        {
            return;
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
        }

        return complete;
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
            return;
        }

        File.Delete(path);
    }

    private sealed class ConflictState
    {
        private readonly Func<TransferConflict, Task<ConflictResolution>>? _resolver;
        private readonly bool _isCopy;
        private readonly bool _hasMultipleItems;
        private ConflictAction? _applyToAll;

        public ConflictState(Func<TransferConflict, Task<ConflictResolution>>? resolver, bool isCopy, bool hasMultipleItems)
        {
            _resolver = resolver;
            _isCopy = isCopy;
            _hasMultipleItems = hasMultipleItems;
        }

        public async Task<ConflictAction> ResolveAsync(string sourcePath, string destPath)
        {
            bool sourceIsDirectory = Directory.Exists(sourcePath);
            bool destIsDirectory = Directory.Exists(destPath);
            bool canMerge = sourceIsDirectory && destIsDirectory;

            if (_applyToAll is ConflictAction all && (all != ConflictAction.Merge || canMerge))
            {
                return all;
            }

            if (_resolver == null)
            {
                return ConflictAction.Duplicate;
            }

            TransferConflict conflict = new(sourcePath, destPath, sourceIsDirectory, destIsDirectory, _isCopy, _hasMultipleItems);
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
