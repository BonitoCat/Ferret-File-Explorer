using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Core.Models;
using FileExplorer.Core.Utilities;
using LoggerLib;

namespace FileExplorer.Core.Services;

public enum ElevatedStatus
{
    Success,
    Cancelled,
    Failed,
}

public sealed record ElevatedResult(ElevatedStatus Status, string Error)
{
    public bool IsSuccess => Status == ElevatedStatus.Success;
}

public sealed class ElevatedScript
{
    private readonly StringBuilder _script = new("set -e\n");
    private readonly List<string> _arguments = new();

    public bool IsEmpty => _arguments.Count == 0;
    public string Script => _script.ToString();
    public IReadOnlyList<string> Arguments => _arguments;

    public ElevatedScript Add(string command, params string[] values)
    {
        string[] references = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            _arguments.Add(values[i]);
            references[i] = $"\"${{{_arguments.Count}}}\"";
        }

        _script.Append(string.Format(command, references)).Append('\n');
        return this;
    }

    public ElevatedScript Delete(string path)
    {
        return Add("rm -rf -- {0}", path);
    }

    public ElevatedScript CreateFolder(string path)
    {
        return Add("mkdir -p -- {0}", path);
    }

    public ElevatedScript CreateFile(string path)
    {
        return Add("(set -C; : > {0})", path);
    }

    public ElevatedScript Rename(string source, string target)
    {
        return Add("mv -T -n -- {0} {1}", source, target);
    }

    public ElevatedScript Copy(string source, string target)
    {
        string parent = Path.GetDirectoryName(target) ?? "/";
        return Add("cp -R --preserve=mode,timestamps -T -- {0} {1}", source, target)
            .Add("chown -R -h --reference={0} -- {1}", parent, target);
    }

    public ElevatedScript Move(string source, string target)
    {
        return Add("mv -T -- {0} {1} 2>/dev/null || {{ cp -a -T -- {2} {3} && rm -rf -- {4}; }}", source, target, source, target, source);
    }

    public ElevatedScript Transfer(TransferStep step, bool isCopy)
    {
        if (step.ReplaceExisting)
        {
            Delete(step.Target);
        }

        return isCopy ? Copy(step.Source, step.Target) : Move(step.Source, step.Target);
    }

    public ElevatedScript Symlink(string target, string link)
    {
        return Add("ln -s -T -- {0} {1}", target, link);
    }
}

public static class ElevatedFileService
{
    private const int PkexecDismissed = 126;
    private const int PkexecNotAuthorized = 127;

    private static readonly Lazy<string?> Pkexec = new(() => FindExecutable("pkexec"));

    public static bool IsAvailable => OperatingSystem.IsLinux() && !IsRoot && Pkexec.Value != null;

    private static bool IsRoot => Environment.IsPrivilegedProcess || Environment.UserName == "root";

    public static bool CanModify(string? directory)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(directory) || PathManager.IsRemote(directory) || IsRoot)
        {
            return true;
        }

        return !Directory.Exists(directory) || FileUtils.HasAccess(directory, false, true, true);
    }

    public static bool NeedsElevation(string? directory)
    {
        return IsAvailable && !CanModify(directory);
    }

    public static bool NeedsElevation(IEnumerable<string> paths)
    {
        return IsAvailable && paths.Any(path => !CanModify(Path.GetDirectoryName(path.TrimEnd('/'))));
    }

    public static bool IsPermissionError(Exception ex)
    {
        return IsAvailable && ex is UnauthorizedAccessException;
    }

    public static async Task<ElevatedResult> RunAsync(ElevatedScript script, CancellationToken token = default)
    {
        string? pkexec = Pkexec.Value;
        if (script.IsEmpty)
        {
            return new(ElevatedStatus.Success, "");
        }

        if (pkexec == null)
        {
            return new(ElevatedStatus.Failed, "pkexec not found");
        }

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = pkexec,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/bin/sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(script.Script);
            psi.ArgumentList.Add("ferret");
            foreach (string argument in script.Arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return new(ElevatedStatus.Failed, "");
            }

            await using CancellationTokenRegistration registration = token.Register(() => TryKill(process));
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(CancellationToken.None);
            await outputTask;
            string error = await errorTask;
            token.ThrowIfCancellationRequested();

            _ = Logger.LogI($"Elevated file operation finished with exit code {process.ExitCode}");
            return process.ExitCode switch
            {
                0 => new(ElevatedStatus.Success, ""),
                PkexecDismissed or PkexecNotAuthorized => new(ElevatedStatus.Cancelled, ""),
                _ => new(ElevatedStatus.Failed, error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? ""),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(ElevatedStatus.Failed, ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch
        {
        }
    }

    private static string? FindExecutable(string name)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin";
        return path
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Append("/usr/sbin")
            .Select(dir => Path.Combine(dir, name))
            .FirstOrDefault(File.Exists);
    }
}
