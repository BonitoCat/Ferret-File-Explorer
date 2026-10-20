using System.Diagnostics;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public enum SmbShareAvailability
{
    Available,
    NotInstalled,
    NotPermitted,
}

public enum SmbSetupResult
{
    Success,
    Cancelled,
    Unsupported,
    Failed,
}

public sealed record SmbShareInfo(string Name, string Path, string Comment, bool Writable, bool GuestOk);

public sealed class SmbShareService
{
    public const int MaxNameLength = 80;

    private const string UsershareDir = "/var/lib/samba/usershares";
    private const string UsershareGroup = "sambashare";
    private const int PkexecDismissed = 126;
    private const int PkexecNotAuthorized = 127;
    private const string WriteAcl = "S-1-1-0:F";
    private const string ReadAcl = "S-1-1-0:R";
    private const string ShareNameSuffix = "-share";

    private static readonly char[] InvalidNameChars = ['%', '<', '>', '*', '?', '|', '/', '\\', '+', '=', ';', ':', '"', ',', '[', ']'];

    public static bool IsValidName(string name)
    {
        return name.Length > 0 && name.Length <= MaxNameLength && name.IndexOfAny(InvalidNameChars) < 0;
    }

    public static string SuggestName(string path)
    {
        string name = PathManager.GetName(path);
        foreach (char invalid in InvalidNameChars)
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        if (!IsUserName(name))
        {
            return name;
        }

        string suffixed = name + ShareNameSuffix;
        return suffixed.Length > MaxNameLength ? name[..(MaxNameLength - ShareNameSuffix.Length)] + ShareNameSuffix : suffixed;
    }

    public static bool IsUserName(string name)
    {
        if (name.Length == 0 || !OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            return File.ReadLines("/etc/passwd")
                .Select(l => l.Split(':')[0])
                .Any(user => string.Equals(user, name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public (SmbShareAvailability Availability, string Detail) GetAvailability()
    {
        if (!OperatingSystem.IsLinux() || !IsSambaServerInstalled())
        {
            return (SmbShareAvailability.NotInstalled, "");
        }

        (int exitCode, string output, string error) = Run("usershare", "info");
        string message = output + "\n" + error;
        if (exitCode != 0 || message.Contains("cannot open usershare directory", StringComparison.OrdinalIgnoreCase))
        {
            string detail = message
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => l.Contains("usershare", StringComparison.OrdinalIgnoreCase)) ?? FirstLine(message);
            return (SmbShareAvailability.NotPermitted, detail);
        }

        return (SmbShareAvailability.Available, "");
    }

    public static bool CanSetup => OperatingSystem.IsLinux() && FindExecutable("pkexec") != null && GetInstallCommand() != null;

    public static bool IsInUsershareGroup()
    {
        try
        {
            string? line = File.ReadLines("/etc/group").FirstOrDefault(l => l.StartsWith(UsershareGroup + ":", StringComparison.Ordinal));
            string members = line?.Split(':').ElementAtOrDefault(3) ?? "";
            return members.Split(',').Contains(Environment.UserName);
        }
        catch
        {
            return false;
        }
    }

    public async Task<(SmbSetupResult Result, string Detail)> SetupAsync(bool install, CancellationToken token = default)
    {
        string? installCommand = install ? GetInstallCommand() : "true";
        string? pkexec = FindExecutable("pkexec");
        if (installCommand == null || pkexec == null)
        {
            return (SmbSetupResult.Unsupported, "");
        }

        string script = string.Join("\n",
            "set -e",
            installCommand,
            $"getent group {UsershareGroup} >/dev/null || groupadd {UsershareGroup}",
            $"usermod -aG {UsershareGroup} {Quote(Environment.UserName)}",
            $"mkdir -p {UsershareDir}",
            $"chgrp {UsershareGroup} {UsershareDir}",
            $"chmod 1770 {UsershareDir}");

        return await RunElevatedAsync(pkexec, script, token);
    }

    public async Task<(SmbSetupResult Result, string Detail)> DisconnectShareAsync(string name, CancellationToken token = default)
    {
        await UnmountLocalAsync(name, token);
        if (!HasActiveConnections())
        {
            return (SmbSetupResult.Success, "");
        }

        string? pkexec = FindExecutable("pkexec");
        string? smbcontrol = FindExecutable("smbcontrol");
        if (pkexec == null || smbcontrol == null)
        {
            return (SmbSetupResult.Unsupported, "");
        }

        return await RunElevatedAsync(pkexec, $"{Quote(smbcontrol)} smbd close-share {Quote(name)}", token);
    }

    private static bool HasActiveConnections()
    {
        string? ss = FindExecutable("ss");
        if (ss == null)
        {
            return true;
        }

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = ss,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in new[] { "-Htn", "state", "established", "( sport = :445 or sport = :139 )" })
            {
                psi.ArgumentList.Add(arg);
            }

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return true;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode != 0 || !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return true;
        }
    }

    private static async Task UnmountLocalAsync(string name, CancellationToken token)
    {
        string? gio = FindExecutable("gio");
        if (gio == null)
        {
            return;
        }

        foreach (string uri in await GetLocalMountsAsync(gio, name, token))
        {
            await RunToolAsync(gio, ["mount", "-u", uri], token);
        }
    }

    private static async Task<List<string>> GetLocalMountsAsync(string gio, string name, CancellationToken token)
    {
        (int exitCode, string output) = await RunToolAsync(gio, ["mount", "-l"], token);
        if (exitCode != 0)
        {
            return new();
        }

        HashSet<string> localHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "localhost",
            "127.0.0.1",
            "[::1]",
            Environment.MachineName,
            Environment.MachineName + ".local",
        };

        List<string> mounts = new();
        foreach (string line in output.Split('\n'))
        {
            int arrow = line.IndexOf("-> smb://", StringComparison.OrdinalIgnoreCase);
            if (arrow < 0)
            {
                continue;
            }

            string uri = line[(arrow + 3)..].Trim();
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed))
            {
                continue;
            }

            string share = Uri.UnescapeDataString(parsed.AbsolutePath.Trim('/').Split('/')[0]);
            bool isLocal = localHosts.Contains(parsed.Host) || localHosts.Contains(parsed.Host.Split('.')[0]);
            if (isLocal && string.Equals(share, name, StringComparison.OrdinalIgnoreCase))
            {
                mounts.Add(uri);
            }
        }

        return mounts;
    }

    private static async Task<(int ExitCode, string Output)> RunToolAsync(string fileName, string[] args, CancellationToken token)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            psi.Environment["LC_ALL"] = "C";

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return (-1, "");
            }

            process.StandardInput.Close();
            Task<string> errorTask = process.StandardError.ReadToEndAsync(token);
            string output = await process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            await errorTask;
            return (process.ExitCode, output);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (-1, "");
        }
    }

    private static string Quote(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }

    private static async Task<(SmbSetupResult Result, string Detail)> RunElevatedAsync(string pkexec, string script, CancellationToken token)
    {
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
            psi.ArgumentList.Add("sh");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(script);
            psi.Environment["DEBIAN_FRONTEND"] = "noninteractive";

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return (SmbSetupResult.Failed, "");
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            await outputTask;
            string error = await errorTask;

            return process.ExitCode switch
            {
                0 => (SmbSetupResult.Success, ""),
                PkexecDismissed or PkexecNotAuthorized => (SmbSetupResult.Cancelled, ""),
                _ => (SmbSetupResult.Failed, error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? ""),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (SmbSetupResult.Failed, ex.Message);
        }
    }

    private static string? GetInstallCommand()
    {
        if (FindExecutable("apt-get") != null)
        {
            return "apt-get install -y samba";
        }

        if (FindExecutable("dnf") != null)
        {
            return "dnf install -y samba";
        }

        if (FindExecutable("zypper") != null)
        {
            return "zypper --non-interactive install samba";
        }

        if (FindExecutable("pacman") != null)
        {
            return "pacman -S --noconfirm --needed samba";
        }

        return null;
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

    public List<SmbShareInfo> GetShares()
    {
        (int exitCode, string output, _) = Run("usershare", "info");
        return exitCode == 0 ? Parse(output) : new();
    }

    public SmbShareInfo? GetShareForPath(string path)
    {
        string normalized = NormalizePath(path);
        return GetShares().FirstOrDefault(s => NormalizePath(s.Path) == normalized);
    }

    public string? SetShare(string name, string path, string comment, bool writable, bool guestOk, string? previousName)
    {
        if (!IsValidName(name))
        {
            return null;
        }

        if (previousName != null && !string.Equals(previousName, name, StringComparison.OrdinalIgnoreCase))
        {
            RemoveShare(previousName);
        }

        (int exitCode, string output, string error) = Run(
            "usershare",
            "add",
            name,
            NormalizePath(path),
            comment,
            writable ? WriteAcl : ReadAcl,
            guestOk ? "guest_ok=y" : "guest_ok=n");

        return exitCode == 0 ? null : FirstLine(string.IsNullOrWhiteSpace(error) ? output : error);
    }

    public string? RemoveShare(string name)
    {
        (int exitCode, string output, string error) = Run("usershare", "delete", name);
        return exitCode == 0 ? null : FirstLine(string.IsNullOrWhiteSpace(error) ? output : error);
    }

    public static UnixFileMode GetMissingPermissions(string path, bool writable)
    {
        if (OperatingSystem.IsWindows())
        {
            return UnixFileMode.None;
        }

        UnixFileMode required = UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        if (writable)
        {
            required |= UnixFileMode.OtherWrite;
        }

        UnixFileMode current = File.GetUnixFileMode(path);
        return required & ~current;
    }

    public static string? FindBlockingParent(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        string? dir = Path.GetDirectoryName(NormalizePath(path));
        while (!string.IsNullOrEmpty(dir) && !PathManager.IsRoot(dir))
        {
            try
            {
                if ((File.GetUnixFileMode(dir) & UnixFileMode.OtherExecute) == 0)
                {
                    return dir;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    public static void AddPermissions(string path, UnixFileMode permissions)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | permissions);
    }

    private static List<SmbShareInfo> Parse(string output)
    {
        List<SmbShareInfo> shares = new();
        string? name = null;
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (name == null)
            {
                return;
            }

            string acl = values.GetValueOrDefault("usershare_acl", "");
            shares.Add(new(
                name,
                values.GetValueOrDefault("path", ""),
                values.GetValueOrDefault("comment", ""),
                acl.Contains(":F", StringComparison.OrdinalIgnoreCase),
                values.GetValueOrDefault("guest_ok", "n").StartsWith('y')));
        }

        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                name = line[1..^1];
                values.Clear();
                continue;
            }

            int separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator]] = line[(separator + 1)..];
        }

        Flush();
        return shares;
    }

    private static bool IsSambaServerInstalled()
    {
        string[] candidates = ["/usr/sbin/smbd", "/usr/bin/smbd", "/sbin/smbd"];
        return candidates.Any(File.Exists) || Directory.Exists(UsershareDir);
    }

    private static string NormalizePath(string path)
    {
        return PathManager.TrimEnd(path);
    }

    private static string FirstLine(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
    }

    private static (int ExitCode, string Output, string Error) Run(params string[] args)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "net",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            psi.Environment["LC_ALL"] = "C";

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return (-1, "", "");
            }

            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output, errorTask.Result);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }
}
