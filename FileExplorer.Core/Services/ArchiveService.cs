using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;
using FileExplorer.Core.Models;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public class ArchiveService
{
    public static readonly string[] SupportedFormats =
    [
        ".zip",
        ".7z",
        ".tar.gz",
        ".tar.xz",
        ".tar.bz2",
        ".tar.zst",
        ".tar"
    ];

    private const string PartialSuffix = ".ferret-partial";
    private const string TarCheckpointMarker = "#ferret-checkpoint#";
    private const long TarRecordBytes = 10240;
    private const long TarBlockBytes = 512;
    private const long MinTarCheckpointRecords = 100;
    private const long TarCheckpointsPerRun = 200;

    private static readonly Regex SevenZipProgress = new(@"^\s*(\d+)%(?:\s+\d+)?(?:\s+[+U=]\s+(.*))?$", RegexOptions.Compiled);

    private static readonly EnumerationOptions MeasureEnumeration = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        ReturnSpecialDirectories = false,
    };

    public static bool IsPartialArchive(string path)
    {
        return path.EndsWith(PartialSuffix, StringComparison.Ordinal);
    }

    public async Task<(bool Success, string Error)> CompressAsync(
        IEnumerable<string> sourcePaths,
        string destinationArchivePath,
        string format,
        string? password = null,
        CancellationToken cancellationToken = default,
        IProgress<FileTransferProgress>? progress = null)
    {
        List<string> paths = sourcePaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (paths.Count == 0)
        {
            return (false, "No valid files or folders selected to compress.");
        }

        if (string.IsNullOrWhiteSpace(destinationArchivePath))
        {
            return (false, "Destination path cannot be empty.");
        }

        format = format.ToLowerInvariant();
        if (!format.StartsWith('.'))
        {
            format = "." + format;
        }

        string? destDir = Path.GetDirectoryName(destinationArchivePath);
        string partialPath = Path.Combine(destDir ?? string.Empty, $".{Guid.NewGuid():N}{PartialSuffix}");

        try
        {
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            (long inputBytes, long tarBytes) = MeasureSources(paths, cancellationToken);
            CompressionTracker tracker = new(progress, inputBytes);

            (bool success, string error) = format switch
            {
                ".7z" => await CompressWith7zAsync(paths, partialPath, "7z", password, tracker, cancellationToken),
                ".zip" => await CompressWith7zAsync(paths, partialPath, "zip", password, tracker, cancellationToken),
                _ => await CompressWithTarAsync(paths, partialPath, format, tarBytes, tracker, cancellationToken),
            };
            if (!success)
            {
                return (false, error);
            }

            File.Move(partialPath, destinationArchivePath, true);
            return (true, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, ex.Message);
        }
        finally
        {
            TryDeleteFile(partialPath);
        }
    }

    public async Task<bool> IsEncryptedArchiveAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            return false;
        }

        string lower = archivePath.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tar.xz") || lower.EndsWith(".tar.bz2") || lower.EndsWith(".tar.zst") || lower.EndsWith(".tar") || lower.EndsWith(".tgz"))
        {
            return false;
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "7z",
            Arguments = $"t -p \"{archivePath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        (bool success, string output) = await RunProcessAsync(startInfo, cancellationToken);
        if (!success && (output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) ||
                         output.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
                         output.Contains("Enter password", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public async Task<(bool Success, string Error)> ExtractHereAsync(
        string archivePath,
        string destinationDirectory,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            return (false, "Archive file does not exist.");
        }

        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        string lower = archivePath.ToLowerInvariant();
        ProcessStartInfo startInfo;

        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tar.xz") || lower.EndsWith(".tar.bz2") || lower.EndsWith(".tar.zst") || lower.EndsWith(".tar") || lower.EndsWith(".tgz"))
        {
            startInfo = new()
            {
                FileName = "tar",
                Arguments = $"-xf \"{archivePath}\" -C \"{destinationDirectory}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            string passwordArg = password != null ? $"-p\"{password.Replace("\"", "\\\"")}\"" : "-p";
            startInfo = new()
            {
                FileName = "7z",
                Arguments = $"x -y {passwordArg} -bso0 -bsp0 \"-o{destinationDirectory}\" \"{archivePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        return await RunProcessAsync(startInfo, cancellationToken);
    }

    private static async Task<(bool Success, string Error)> CompressWith7zAsync(
        List<string> paths,
        string destinationArchive,
        string type,
        string? password,
        CompressionTracker tracker,
        CancellationToken cancellationToken)
    {
        List<string> args = ["a", $"-t{type}", "-bso0", "-bsp1"];

        if (!string.IsNullOrEmpty(password))
        {
            args.Add($"-p{password}");
            if (type == "7z")
            {
                args.Add("-mhe=on");
            }
            else if (type == "zip")
            {
                args.Add("-mem=AES256");
            }
        }

        args.Add(destinationArchive);
        args.AddRange(paths);

        tracker.Start();
        return await RunCompressorAsync("7z", args, line => ParseSevenZipProgress(line, tracker), _ => false, cancellationToken);
    }

    private static void ParseSevenZipProgress(string line, CompressionTracker tracker)
    {
        Match match = SevenZipProgress.Match(line);
        if (!match.Success)
        {
            return;
        }

        tracker.Update(double.Parse(match.Groups[1].Value) / 100.0, match.Groups[2].Value.Trim());
    }

    private static async Task<(bool Success, string Error)> CompressWithTarAsync(
        List<string> paths,
        string destinationArchive,
        string format,
        long tarBytes,
        CompressionTracker tracker,
        CancellationToken cancellationToken)
    {
        string parentDir = PathManager.GetParent(paths[0]);
        List<string> relPaths = new();

        foreach (string p in paths)
        {
            string pDir = PathManager.GetParent(p);
            if (pDir == parentDir)
            {
                relPaths.Add(Path.GetFileName(p));
            }
            else
            {
                relPaths.Add(p);
            }
        }

        string[] tarFlags = format switch
        {
            ".tar.gz" => ["-czf"],
            ".tar.xz" => ["-cJf"],
            ".tar.bz2" => ["-cjf"],
            ".tar.zst" => ["--zstd", "-cf"],
            _ => ["-cf"]
        };

        List<string> args = new();
        bool canTrack = OperatingSystem.IsLinux();
        if (canTrack)
        {
            long checkpointRecords = Math.Max(MinTarCheckpointRecords, tarBytes / TarRecordBytes / TarCheckpointsPerRun);
            args.AddRange(["-v", "--quoting-style=literal", $"--checkpoint={checkpointRecords}", $"--checkpoint-action=echo={TarCheckpointMarker}%u"]);
            tracker.Start();
        }

        args.AddRange(tarFlags);
        args.AddRange([destinationArchive, "-C", parentDir]);
        args.AddRange(relPaths);

        return await RunCompressorAsync(
            "tar",
            args,
            line =>
            {
                if (canTrack)
                {
                    tracker.UpdateItem(line);
                }
            },
            line => TryParseTarCheckpoint(line, tarBytes, tracker),
            cancellationToken);
    }

    private static bool TryParseTarCheckpoint(string line, long tarBytes, CompressionTracker tracker)
    {
        int markerIndex = line.IndexOf(TarCheckpointMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        if (tarBytes > 0 && long.TryParse(line.AsSpan(markerIndex + TarCheckpointMarker.Length), out long records))
        {
            tracker.Update((double) (records * TarRecordBytes) / tarBytes);
        }

        return true;
    }

    private static (long InputBytes, long TarBytes) MeasureSources(List<string> paths, CancellationToken cancellationToken)
    {
        long inputBytes = 0;
        long tarBytes = 0;

        foreach (string path in paths)
        {
            if (!Directory.Exists(path))
            {
                AddMeasuredFile(new FileInfo(path).Length, ref inputBytes, ref tarBytes);
                continue;
            }

            tarBytes += TarBlockBytes;
            FileSystemEnumerable<long> sizes = new(path, (ref FileSystemEntry entry) => entry.IsDirectory ? -1 : entry.Length, MeasureEnumeration);
            foreach (long size in sizes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (size < 0)
                {
                    tarBytes += TarBlockBytes;
                    continue;
                }

                AddMeasuredFile(size, ref inputBytes, ref tarBytes);
            }
        }

        return (inputBytes, tarBytes);
    }

    private static void AddMeasuredFile(long size, ref long inputBytes, ref long tarBytes)
    {
        inputBytes += size;
        tarBytes += TarBlockBytes + (size + TarBlockBytes - 1) / TarBlockBytes * TarBlockBytes;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task<(bool Success, string Error)> RunCompressorAsync(
        string fileName,
        List<string> args,
        Action<string> onOutput,
        Func<string, bool> consumeError,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (false, ex.Message);
        }

        ProcessUtils.TryLowerPriority(process);
        using CancellationTokenRegistration registration = cancellationToken.Register(() => ProcessUtils.TryKillTree(process));

        StringBuilder errors = new();
        void OnError(string line)
        {
            if (!consumeError(line))
            {
                errors.AppendLine(line);
            }
        }

        await Task.WhenAll(PumpLinesAsync(process.StandardOutput, onOutput), PumpLinesAsync(process.StandardError, OnError));
        await process.WaitForExitAsync(CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode == 0)
        {
            return (true, string.Empty);
        }

        string message = errors.ToString().Trim();
        return (false, string.IsNullOrEmpty(message) ? $"Process exited with code {process.ExitCode}" : message);
    }

    private static async Task PumpLinesAsync(StreamReader reader, Action<string> onLine)
    {
        char[] buffer = new char[4096];
        StringBuilder line = new();

        while (true)
        {
            int read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (c is not ('\r' or '\n' or '\b'))
                {
                    line.Append(c);
                    continue;
                }

                FlushLine(line, onLine);
            }
        }

        FlushLine(line, onLine);
    }

    private static void FlushLine(StringBuilder line, Action<string> onLine)
    {
        string text = line.ToString();
        line.Clear();
        if (!string.IsNullOrWhiteSpace(text))
        {
            onLine(text);
        }
    }

    private sealed class CompressionTracker
    {
        private const double MinFractionForEstimate = 0.005;

        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan MinElapsedForEstimate = TimeSpan.FromSeconds(2);

        private readonly IProgress<FileTransferProgress>? _progress;
        private readonly long _totalBytes;
        private readonly object _lock = new();
        private readonly Stopwatch _elapsed = new();
        private TimeSpan _lastReport;
        private double _fraction;
        private string _currentItem = string.Empty;

        public CompressionTracker(IProgress<FileTransferProgress>? progress, long totalBytes)
        {
            _progress = progress;
            _totalBytes = totalBytes;
        }

        public void Start()
        {
            lock (_lock)
            {
                _elapsed.Start();
                Report();
            }
        }

        public void Update(double fraction, string item = "")
        {
            lock (_lock)
            {
                _fraction = Math.Max(_fraction, Math.Clamp(fraction, 0.0, 1.0));
                if (!string.IsNullOrEmpty(item))
                {
                    _currentItem = item;
                }

                ReportIfDue();
            }
        }

        public void UpdateItem(string item)
        {
            lock (_lock)
            {
                _currentItem = item;
                ReportIfDue();
            }
        }

        private void ReportIfDue()
        {
            if (_elapsed.Elapsed - _lastReport >= ReportInterval)
            {
                Report();
            }
        }

        private void Report()
        {
            _lastReport = _elapsed.Elapsed;

            long transferred = (long) (_fraction * _totalBytes);
            double seconds = _elapsed.Elapsed.TotalSeconds;
            bool canEstimate = _fraction >= MinFractionForEstimate && _elapsed.Elapsed >= MinElapsedForEstimate;

            _progress?.Report(new()
            {
                TransferredBytes = transferred,
                TotalBytes = _totalBytes,
                BytesPerSecond = seconds > 0 ? transferred / seconds : 0,
                EstimatedRemaining = canEstimate ? TimeSpan.FromSeconds(seconds * (1.0 - _fraction) / _fraction) : null,
                CurrentItemName = _currentItem,
            });
        }
    }

    private static async Task<(bool Success, string Error)> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        try
        {
            Process process = new() { StartInfo = startInfo };
            process.Start();

            Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            string[] results = await Task.WhenAll(stdOutTask, stdErrTask);
            await process.WaitForExitAsync(cancellationToken);

            string stdOut = results[0];
            string stdErr = results[1];

            if (process.ExitCode == 0)
            {
                return (true, string.Empty);
            }

            string combined = $"{stdErr}{Environment.NewLine}{stdOut}".Trim();
            return (false, string.IsNullOrWhiteSpace(combined) ? $"Process exited with code {process.ExitCode}" : combined);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
