using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public sealed record KdeConnectDevice(string Id, string Name, string MountPoint, bool IsMounted);

public static class KdeConnectService
{
    public const string UriScheme = "kdeconnect:";

    private const string Cli = "kdeconnect-cli";
    private const string DaemonProcessName = "kdeconnectd";
    private const string SshfsType = "fuse.sshfs";
    private const int ListTimeoutMs = 2000;
    private const int MountTimeoutMs = 30000;
    private const int ProbeTimeoutMs = 1500;

    private static readonly string[] AndroidStorage = ["storage", "emulated", "0"];
    private static readonly Regex MountEscape = new(@"\\([0-7]{3})", RegexOptions.Compiled);

    public static string RuntimeDirectory => Path.GetDirectoryName(GvfsPath.MountDirectory) ?? string.Empty;

    public static bool IsDeviceUri(string? path)
    {
        return path != null && path.StartsWith(UriScheme, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToUri(string deviceId)
    {
        return $"{UriScheme}//{deviceId}/";
    }

    public static List<KdeConnectDevice> GetDevices()
    {
        List<KdeConnectDevice> devices = new();
        if (!OperatingSystem.IsLinux())
        {
            return devices;
        }

        HashSet<string> mounts = new(GetMountPoints(), StringComparer.Ordinal);
        List<(string Id, string Name)> reachable = IsDaemonRunning()
            ? ParseDeviceList(RunCli(ListTimeoutMs, "-a", "--id-name-only") ?? string.Empty)
            : [];

        foreach ((string id, string name) in reachable)
        {
            string mountPoint = Path.Combine(RuntimeDirectory, id);
            devices.Add(new(id, name, mountPoint, mounts.Remove(mountPoint)));
        }

        foreach (string mountPoint in mounts)
        {
            devices.Add(new(Path.GetFileName(mountPoint), string.Empty, mountPoint, true));
        }

        return devices;
    }

    public static List<string> GetMountPoints()
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        try
        {
            return ParseMountPoints(File.ReadAllText("/proc/self/mounts"), RuntimeDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static List<string> ParseMountPoints(string mounts, string runtimeDirectory)
    {
        List<string> result = new();
        if (string.IsNullOrEmpty(runtimeDirectory))
        {
            return result;
        }

        foreach (string line in mounts.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split(' ');
            if (fields.Length < 3 || fields[2] != SshfsType)
            {
                continue;
            }

            string mountPoint = MountEscape.Replace(fields[1], m => ((char) Convert.ToInt32(m.Groups[1].Value, 8)).ToString());
            if (PathManager.AreEqual(Path.GetDirectoryName(mountPoint) ?? string.Empty, runtimeDirectory))
            {
                result.Add(mountPoint);
            }
        }

        return result;
    }

    public static List<(string Id, string Name)> ParseDeviceList(string output)
    {
        List<(string Id, string Name)> result = new();
        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int space = line.IndexOf(' ');
            result.Add(space < 0 ? (line, string.Empty) : (line[..space], line[(space + 1)..].Trim()));
        }

        return result;
    }

    public static bool IsMountRoot(string path)
    {
        return OperatingSystem.IsLinux() && GetMountPoints().Any(mount => PathManager.AreEqual(mount, path));
    }

    public static string GetBrowsePath(string mountPoint)
    {
        string storage = Path.Combine([mountPoint, .. AndroidStorage]);
        Task<bool> probe = Task.Run(() => Directory.Exists(storage));
        return probe.Wait(ProbeTimeoutMs) && probe.Result ? storage : mountPoint;
    }

    public static string? ResolveUri(string location)
    {
        string rest = Uri.UnescapeDataString(location[UriScheme.Length..]).Trim('/');
        int slash = rest.IndexOf('/');
        string id = slash < 0 ? rest : rest[..slash];
        string subPath = slash < 0 ? string.Empty : rest[(slash + 1)..];
        if (id.Length == 0)
        {
            return null;
        }

        string? mountPoint = GetMountPoints().FirstOrDefault(mount => Path.GetFileName(mount) == id);
        if (mountPoint == null)
        {
            return null;
        }

        if (subPath.Length == 0)
        {
            return GetBrowsePath(mountPoint);
        }

        string target = Path.GetFullPath(Path.Combine(mountPoint, subPath));
        return PathManager.IsSameOrAncestor(mountPoint, target) ? target : mountPoint;
    }

    public static async Task<string?> MountAsync(string deviceUri)
    {
        string id = deviceUri[UriScheme.Length..].Trim('/');
        if (id.Length == 0 || id.Contains('/'))
        {
            return null;
        }

        await Task.Run(() => RunCli(MountTimeoutMs, "--mount", "-d", id));

        string? mountPoint = GetMountPoints().FirstOrDefault(mount => Path.GetFileName(mount) == id);
        return mountPoint == null ? null : await Task.Run(() => GetBrowsePath(mountPoint));
    }

    private static bool IsDaemonRunning()
    {
        try
        {
            foreach (string dir in Directory.EnumerateDirectories("/proc"))
            {
                string name = Path.GetFileName(dir);
                if (name.Length == 0 || !char.IsDigit(name[0]))
                {
                    continue;
                }

                try
                {
                    if (File.ReadAllText(Path.Combine(dir, "comm")).Trim() == DaemonProcessName)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    private static string? RunCli(int timeoutMs, params string[] args)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Cli,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using Process process = new() { StartInfo = startInfo };
            process.Start();

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                ProcessUtils.TryKillTree(process);
                return null;
            }

            return output.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
