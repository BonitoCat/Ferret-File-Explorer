using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FileExplorer.Core.Utilities;
using FileLib;

namespace FileExplorer.Core.Addons;

public sealed record AddonLaunch(IReadOnlyList<string> Command, IReadOnlyDictionary<string, string> Environment, string WorkingDirectory);

public static partial class AddonRunner
{
    private const int SigTerm = 15;
    private const int SigKill = 9;
    private const string TempDirPrefix = "fe-addon-";
    private static readonly TimeSpan StopPollInterval = TimeSpan.FromMilliseconds(100);

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int SendSignal(int pid, int signal);

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

        Dictionary<string, string> environment = CreateEnvironment(action.Addon, context, languageCode);
        environment["FE_ACTION_ID"] = action.Id;

        string workingDirectory = Directory.Exists(context.CurrentDirectory) ? context.CurrentDirectory : action.Addon.RootPath;
        return new(command, environment, workingDirectory);
    }

    public static AddonLaunch PrepareHook(Addon addon, AddonHook hook, AddonContext context, string languageCode)
    {
        string scriptPath = addon.Hooks[hook];
        List<string> command = GetInterpreter(scriptPath, addon.Manifest.Interpreter);
        command.Add(scriptPath);

        Dictionary<string, string> environment = CreateEnvironment(addon, context, languageCode);
        environment["FE_HOOK"] = GetHookName(hook);
        return new(command, environment, addon.RootPath);
    }

    public static AddonLaunch PrepareService(Addon addon, AddonContext context, string languageCode)
    {
        string scriptPath = addon.ServicePath!;
        List<string> command = GetInterpreter(scriptPath, addon.Manifest.Interpreter);
        command.Add(scriptPath);

        Dictionary<string, string> environment = CreateEnvironment(addon, context, languageCode);
        environment["FE_SERVICE"] = "1";
        return new(command, environment, addon.RootPath);
    }

    public static string GetHookName(AddonHook hook)
    {
        return hook switch
        {
            AddonHook.PostInstall => "post_install",
            AddonHook.PreUninstall => "pre_uninstall",
            AddonHook.PostLoad => "post_load",
            AddonHook.PostStart => "post_start",
            _ => "pre_close",
        };
    }

    public static string GetModeName(AddonMode mode)
    {
        return mode switch
        {
            AddonMode.FilePicker => "file-picker",
            AddonMode.FolderPicker => "folder-picker",
            AddonMode.SavePicker => "save-picker",
            _ => "explorer",
        };
    }

    public static async Task StopAsync(Process process, TimeSpan grace)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                process.Kill(true);
                return;
            }

            List<int> tree = GetProcessTree(process.Id);
            foreach (int pid in tree)
            {
                SendSignal(pid, SigTerm);
            }

            DateTime deadline = DateTime.UtcNow + grace;
            while (DateTime.UtcNow < deadline && tree.Any(IsAlive))
            {
                await Task.Delay(StopPollInterval);
            }

            foreach (int pid in tree.Where(IsAlive))
            {
                SendSignal(pid, SigKill);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static bool IsAlive(int pid)
    {
        if (SendSignal(pid, 0) != 0)
        {
            return false;
        }

        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat.Length > stat.LastIndexOf(')') + 2 && stat[stat.LastIndexOf(')') + 2] != 'Z';
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<int> GetProcessTree(int rootPid)
    {
        Dictionary<int, List<int>> children = new();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out int pid))
                {
                    continue;
                }

                string stat;
                try
                {
                    stat = File.ReadAllText(Path.Combine(dir, "stat"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                if (fields.Length < 2 || !int.TryParse(fields[1], out int parent))
                {
                    continue;
                }

                if (!children.TryGetValue(parent, out List<int>? list))
                {
                    list = new();
                    children[parent] = list;
                }

                list.Add(pid);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        List<int> tree = [rootPid];
        for (int i = 0; i < tree.Count; i++)
        {
            if (children.TryGetValue(tree[i], out List<int>? list))
            {
                tree.AddRange(list.Where(pid => !tree.Contains(pid)));
            }
        }

        return tree;
    }

    private static Dictionary<string, string> CreateEnvironment(Addon addon, AddonContext context, string languageCode)
    {
        Dictionary<string, string> environment = new()
        {
            ["FE_ADDON_ID"] = addon.Id,
            ["FE_ADDON_DIR"] = addon.RootPath,
            ["FE_ACTION_ID"] = "",
            ["FE_HOOK"] = "",
            ["FE_MODE"] = GetModeName(context.Mode),
            ["FE_CURRENT_DIR"] = context.CurrentDirectory,
            ["FE_SELECTION"] = string.Join('\n', context.SelectedPaths),
            ["FE_TARGET"] = GetTargetName(context.Target),
            ["FE_INPUT"] = context.Input,
            ["FE_LANGUAGE"] = languageCode,
            ["FE_PID"] = Environment.ProcessId.ToString(),
            ["FE_SERVICE"] = "",
            ["FE_HOME_DIR"] = DirectoryHelper.GetUserDirPath(),
            ["FE_CACHE_DIR"] = DirectoryHelper.GetCacheDirPath(),
            ["FE_DATA_DIR"] = AppPaths.DataDir,
        };

        foreach (KeyValuePair<string, string> setting in AddonSettingsStore.GetValues(addon))
        {
            environment[AddonSettingsStore.GetEnvironmentName(setting.Key)] = setting.Value;
        }

        return environment;
    }

    public static Process Start(AddonLaunch launch, Action<AddonCommand> onCommand, Action<int, string> onExited)
    {
        ProcessStartInfo psi = new()
        {
            FileName = launch.Command[0],
            WorkingDirectory = launch.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
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

        string tempDir = Directory.CreateTempSubdirectory(TempDirPrefix).FullName;
        psi.Environment["FE_TEMP_DIR"] = tempDir;

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
                return;
            }

            if (e.Data != null && e.Data.StartsWith(AddonCommand.Prefix, StringComparison.Ordinal))
            {
                onCommand(new(AddonCommandKind.Unknown, e.Data));
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

            DeleteTempDir(tempDir);
            onExited(process.ExitCode, stderr);
        };

        try
        {
            process.Start();
        }
        catch
        {
            DeleteTempDir(tempDir);
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static void DeleteTempDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
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

            if (arg == "{args}")
            {
                foreach (string word in context.Input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    yield return word;
                }

                continue;
            }

            yield return arg
                .Replace("{input}", context.Input)
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
