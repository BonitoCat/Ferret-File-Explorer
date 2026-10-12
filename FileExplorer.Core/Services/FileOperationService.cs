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

    public async Task TransferAsync(
        string destDir,
        ClipboardMode mode,
        IEnumerable<string> sourcePaths,
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

        void ReportProgress(string currentItemName)
        {
            double elapsed = stopwatch.Elapsed.TotalSeconds;
            double timeDelta = elapsed - lastReportTime;
            double speed = 0;
            if (timeDelta > 0.1)
            {
                long bytesDelta = transferredBytes - lastReportBytes;
                speed = bytesDelta / timeDelta;
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

            destPath = GetUniqueDestinationPath(destDir, fileName);

            if (File.Exists(path))
            {
                await TransferSingleFileAsync(path, destPath, mode, buffer, () => transferredBytes, v => transferredBytes = v, () => transferredItems, v => transferredItems = v, ReportProgress, cancellationToken);
            }
            else if (Directory.Exists(path))
            {
                await TransferDirectoryRecursiveAsync(path, destPath, mode, buffer, () => transferredBytes, v => transferredBytes = v, () => transferredItems, v => transferredItems = v, ReportProgress, cancellationToken);
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

    private async Task TransferDirectoryRecursiveAsync(
        string sourceDir,
        string destDir,
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

        if (mode == ClipboardMode.Cut)
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
                return;
            }
            catch (IOException)
            {
            }
        }

        Directory.CreateDirectory(destDir);
        setItems(getItems() + 1);
        reportProgress(Path.GetFileName(sourceDir));

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string subDest = Path.Combine(destDir, Path.GetFileName(subDir));
            await TransferDirectoryRecursiveAsync(subDir, subDest, mode, buffer, getBytes, setBytes, getItems, setItems, reportProgress, cancellationToken);
        }

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileDest = Path.Combine(destDir, Path.GetFileName(file));
            await TransferSingleFileAsync(file, fileDest, mode, buffer, getBytes, setBytes, getItems, setItems, reportProgress, cancellationToken);
        }

        if (mode == ClipboardMode.Cut)
        {
            try
            {
                Directory.Delete(sourceDir, true);
            }
            catch
            {
            }
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
