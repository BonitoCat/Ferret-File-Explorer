using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public sealed partial class UnixStat
{
    public uint Uid { get; init; }
    public uint Gid { get; init; }
    public string Owner { get; init; } = "";
    public string Group { get; init; } = "";
    public int Mode { get; init; }
    public long Inode { get; init; }
    public long HardLinks { get; init; }
    public long Blocks { get; init; }
    public long BlockSize { get; init; }
    public long Size { get; init; }
    public DateTime? Birth { get; init; }
    public string MountPoint { get; init; } = "";
    public string FileSystemType { get; init; } = "";
    public long SizeOnDisk => Blocks * BlockSize;

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();

    public static uint CurrentUid => OperatingSystem.IsLinux() ? GetUid() : uint.MaxValue;

    public static UnixStat? Load(string path)
    {
        string? output = Run("stat", "-L", "-c", "%u|%g|%U|%G|%a|%i|%h|%b|%B|%s|%W|%m", "--", path);
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        string[] parts = output.Trim().Split('|');
        if (parts.Length < 12)
        {
            return null;
        }

        long birthSeconds = long.TryParse(parts[10], out long parsedBirth) ? parsedBirth : 0;
        string fileSystemType = Run("findmnt", "-n", "-o", "FSTYPE", "-T", path)?.Trim() ?? "";
        if (string.IsNullOrEmpty(fileSystemType))
        {
            fileSystemType = Run("stat", "-f", "-L", "-c", "%T", "--", path)?.Trim() ?? "";
        }

        return new()
        {
            Uid = uint.TryParse(parts[0], out uint uid) ? uid : 0,
            Gid = uint.TryParse(parts[1], out uint gid) ? gid : 0,
            Owner = parts[2],
            Group = parts[3],
            Mode = ParseOctal(parts[4]),
            Inode = long.TryParse(parts[5], out long inode) ? inode : 0,
            HardLinks = long.TryParse(parts[6], out long links) ? links : 0,
            Blocks = long.TryParse(parts[7], out long blocks) ? blocks : 0,
            BlockSize = long.TryParse(parts[8], out long blockSize) ? blockSize : 512,
            Size = long.TryParse(parts[9], out long size) ? size : 0,
            Birth = birthSeconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(birthSeconds).LocalDateTime : null,
            MountPoint = parts[11],
            FileSystemType = fileSystemType,
        };
    }

    public static List<string> GetCurrentUserGroups()
    {
        string? output = Run("id", "-Gn");
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return output
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public static bool ChangeGroup(string path, string group, bool recursive, out string error)
    {
        List<string> args = recursive ? ["-R", group, "--", path] : [group, "--", path];
        return RunChecked("chgrp", args, out error);
    }

    public static string FormatMode(int mode, bool isDirectory)
    {
        char[] chars = new char[10];
        chars[0] = isDirectory ? 'd' : '-';
        string flags = "rwxrwxrwx";
        for (int i = 0; i < 9; i++)
        {
            chars[i + 1] = (mode & (1 << (8 - i))) != 0 ? flags[i] : '-';
        }

        if ((mode & 0x800) != 0)
        {
            chars[3] = chars[3] == 'x' ? 's' : 'S';
        }

        if ((mode & 0x400) != 0)
        {
            chars[6] = chars[6] == 'x' ? 's' : 'S';
        }

        if ((mode & 0x200) != 0)
        {
            chars[9] = chars[9] == 'x' ? 't' : 'T';
        }

        return new(chars);
    }

    public static int ParseOctal(string text)
    {
        try
        {
            return Convert.ToInt32(text.Trim(), 8);
        }
        catch
        {
            return -1;
        }
    }

    private static string? Run(string fileName, params string[] args)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using Process proc = Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return proc.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool RunChecked(string fileName, List<string> args, out string error)
    {
        error = "";
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using Process proc = Process.Start(psi)!;
            proc.StandardOutput.ReadToEnd();
            error = proc.StandardError.ReadToEnd().Trim();
            proc.WaitForExit();

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
