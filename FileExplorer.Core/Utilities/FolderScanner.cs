using System.Diagnostics;

namespace FileExplorer.Core.Utilities;

public sealed record FolderScanProgress(long Files, long Folders, long Bytes, long Unreadable, bool Completed);

public static class FolderScanner
{
    public static Task ScanAsync(string root, Action<FolderScanProgress> report, CancellationToken token)
    {
        return Task.Run(() => Scan(root, report, token), token);
    }

    private static void Scan(string root, Action<FolderScanProgress> report, CancellationToken token)
    {
        long files = 0;
        long folders = 0;
        long bytes = 0;
        long unreadable = 0;
        Stopwatch sinceReport = Stopwatch.StartNew();
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        Stack<DirectoryInfo> pending = new();
        pending.Push(new(root));

        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            DirectoryInfo current = pending.Pop();

            try
            {
                foreach (FileSystemInfo info in current.EnumerateFileSystemInfos("*", options))
                {
                    if (info is DirectoryInfo dir)
                    {
                        folders++;
                        if ((dir.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(dir);
                        }

                        continue;
                    }

                    files++;
                    try
                    {
                        bytes += ((FileInfo) info).Length;
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                unreadable++;
            }

            if (sinceReport.ElapsedMilliseconds < 120)
            {
                continue;
            }

            report(new(files, folders, bytes, unreadable, false));
            sinceReport.Restart();
        }

        report(new(files, folders, bytes, unreadable, true));
    }
}
