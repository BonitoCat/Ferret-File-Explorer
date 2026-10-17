using System.Diagnostics;
using System.Text;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Addons;

public sealed record AddonLaunch(IReadOnlyList<string> Command, IReadOnlyDictionary<string, string> Environment, string WorkingDirectory);

public static class AddonRunner
{
    public static AddonLaunch Prepare(AddonAction action, AddonContext context, string languageCode)
    {
        List<string> command = new();
        if (action.Command != null)
        {
            command.Add(action.Command);
        }
        else if (action.ScriptPath != null)
        {
            command.AddRange(GetInterpreter(action.ScriptPath, action.Interpreter));
            command.Add(action.ScriptPath);
        }

        command.AddRange(ExpandArgs(action, context));

        Dictionary<string, string> environment = new()
        {
            ["FE_ADDON_ID"] = action.Addon.Id,
            ["FE_ADDON_DIR"] = action.Addon.RootPath,
            ["FE_ACTION_ID"] = action.Id,
            ["FE_CURRENT_DIR"] = context.CurrentDirectory,
            ["FE_SELECTION"] = string.Join('\n', context.SelectedPaths),
            ["FE_TARGET"] = GetTargetName(context.Target),
            ["FE_LANGUAGE"] = languageCode,
            ["FE_PID"] = Environment.ProcessId.ToString(),
        };

        string workingDirectory = Directory.Exists(context.CurrentDirectory) ? context.CurrentDirectory : action.Addon.RootPath;
        return new(command, environment, workingDirectory);
    }

    public static Process Start(AddonLaunch launch, Action<AddonCommand> onCommand, Action<int, string> onExited)
    {
        ProcessStartInfo psi = new()
        {
            FileName = launch.Command[0],
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string arg in launch.Command.Skip(1))
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (KeyValuePair<string, string> variable in launch.Environment)
        {
            psi.Environment[variable.Key] = variable.Value;
        }

        StringBuilder errors = new();
        Process process = new()
        {
            StartInfo = psi,
            EnableRaisingEvents = true,
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (AddonCommand.TryParse(e.Data, out AddonCommand? command) && command != null)
            {
                onCommand(command);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            lock (errors)
            {
                errors.AppendLine(e.Data);
            }
        };
        process.Exited += (_, _) =>
        {
            string stderr;
            lock (errors)
            {
                stderr = errors.ToString().Trim();
            }

            onExited(process.ExitCode, stderr);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static IEnumerable<string> ExpandArgs(AddonAction action, AddonContext context)
    {
        IReadOnlyList<string> paths = context.TargetPaths;
        string first = paths.Count > 0 ? paths[0] : context.CurrentDirectory;

        foreach (string arg in action.Args)
        {
            if (arg == "{paths}")
            {
                foreach (string path in paths)
                {
                    yield return path;
                }

                continue;
            }

            yield return arg
                .Replace("{path}", first)
                .Replace("{name}", Path.GetFileName(first))
                .Replace("{dir}", context.CurrentDirectory)
                .Replace("{addon}", action.Addon.RootPath);
        }
    }

    private static List<string> GetInterpreter(string scriptPath, string? interpreter)
    {
        if (!string.IsNullOrWhiteSpace(interpreter))
        {
            return interpreter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        bool isWindows = OperatingSystem.IsWindows();
        if (!isWindows && FileUtils.IsFileExecutable(scriptPath))
        {
            return new();
        }

        return Path.GetExtension(scriptPath).ToLowerInvariant() switch
        {
            ".sh" or ".bash" => ["bash"],
            ".py" => [isWindows ? "python" : "python3"],
            ".js" or ".mjs" => ["node"],
            ".rb" => ["ruby"],
            ".pl" => ["perl"],
            ".lua" => ["lua"],
            ".php" => ["php"],
            ".ps1" => [isWindows ? "powershell" : "pwsh", "-ExecutionPolicy", "Bypass", "-File"],
            ".bat" or ".cmd" => ["cmd", "/c"],
            _ => isWindows ? new() : ["sh"],
        };
    }

    private static string GetTargetName(AddonTarget target)
    {
        return target switch
        {
            AddonTarget.Background => "background",
            AddonTarget.File => "file",
            AddonTarget.Folder => "folder",
            _ => "mixed",
        };
    }
}
