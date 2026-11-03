using System.Diagnostics;
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

    public async Task<(bool Success, string Error)> CompressAsync(
        IEnumerable<string> sourcePaths,
        string destinationArchivePath,
        string format,
        string? password = null,
        CancellationToken cancellationToken = default)
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

        string? destDir = Path.GetDirectoryName(destinationArchivePath);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(destinationArchivePath))
        {
            File.Delete(destinationArchivePath);
        }

        format = format.ToLowerInvariant();
        if (!format.StartsWith('.'))
        {
            format = "." + format;
        }

        if (format == ".7z")
        {
            return await CompressWith7zAsync(paths, destinationArchivePath, "7z", password, cancellationToken);
        }

        if (format == ".zip")
        {
            return await CompressWith7zAsync(paths, destinationArchivePath, "zip", password, cancellationToken);
        }

        return await CompressWithTarAsync(paths, destinationArchivePath, format, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        List<string> argsList = ["a", $"-t{type}"];

        if (!string.IsNullOrEmpty(password))
        {
            argsList.Add($"-p\"{password.Replace("\"", "\\\"")}\"");
            if (type == "7z")
            {
                argsList.Add("-mhe=on");
            }
            else if (type == "zip")
            {
                argsList.Add("-mem=AES256");
            }
        }

        argsList.Add($"\"{destinationArchive}\"");

        foreach (string path in paths)
        {
            argsList.Add($"\"{path}\"");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "7z",
            Arguments = string.Join(" ", argsList),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return await RunProcessAsync(startInfo, cancellationToken);
    }

    private static async Task<(bool Success, string Error)> CompressWithTarAsync(
        List<string> paths,
        string destinationArchive,
        string format,
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

        string tarFlags = format switch
        {
            ".tar.gz" => "-czf",
            ".tar.xz" => "-cJf",
            ".tar.bz2" => "-cjf",
            ".tar.zst" => "--zstd -cf",
            _ => "-cf"
        };

        List<string> argsList = [tarFlags, $"\"{destinationArchive}\"", "-C", $"\"{parentDir}\""];
        foreach (string r in relPaths)
        {
            argsList.Add($"\"{r}\"");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "tar",
            Arguments = string.Join(" ", argsList),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return await RunProcessAsync(startInfo, cancellationToken);
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
