using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public sealed partial class FileMetadata
{
    private const int AtFdCwd = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const uint StatxBasicStats = 0x7ff;
    private const uint StatxBirthTime = 0x800;
    private const int StatxSize = 256;
    private const int UidOffset = 20;
    private const int GidOffset = 24;
    private const int ModeOffset = 28;
    private const int AccessTimeOffset = 64;
    private const int BirthTimeOffset = 80;

    private static readonly ConcurrentDictionary<uint, string> UserNames = new();
    private static readonly ConcurrentDictionary<uint, string> GroupNames = new();
    private static bool _statxUnavailable;

    public string Owner { get; init; } = "";
    public string Group { get; init; } = "";
    public string Permissions { get; init; } = "";
    public DateTime? Created { get; init; }
    public DateTime? Accessed { get; init; }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int dirFd, string path, int flags, uint mask, byte[] buffer);

    [LibraryImport("libc", EntryPoint = "getpwuid")]
    private static partial IntPtr GetPasswordEntry(uint uid);

    [LibraryImport("libc", EntryPoint = "getgrgid")]
    private static partial IntPtr GetGroupEntry(uint gid);

    public static FileMetadata Read(string path)
    {
        if (OperatingSystem.IsLinux() && !_statxUnavailable)
        {
            FileMetadata? metadata = ReadStatx(path);
            if (metadata != null)
            {
                return metadata;
            }
        }

        return ReadPortable(path);
    }

    private static FileMetadata? ReadStatx(string path)
    {
        byte[] buffer = new byte[StatxSize];
        try
        {
            if (Statx(AtFdCwd, path, 0, StatxBasicStats | StatxBirthTime, buffer) != 0 && Statx(AtFdCwd, path, AtSymlinkNoFollow, StatxBasicStats | StatxBirthTime, buffer) != 0)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            _statxUnavailable = true;
            return null;
        }

        uint mask = BitConverter.ToUInt32(buffer, 0);
        uint uid = BitConverter.ToUInt32(buffer, UidOffset);
        uint gid = BitConverter.ToUInt32(buffer, GidOffset);
        int mode = BitConverter.ToUInt16(buffer, ModeOffset);
        return new()
        {
            Owner = UserNames.GetOrAdd(uid, id => LookupName(GetPasswordEntry(id), id)),
            Group = GroupNames.GetOrAdd(gid, id => LookupName(GetGroupEntry(id), id)),
            Permissions = FormatPermissions(mode),
            Accessed = ReadTimestamp(buffer, AccessTimeOffset),
            Created = (mask & StatxBirthTime) != 0 ? ReadTimestamp(buffer, BirthTimeOffset) : null,
        };
    }

    private static FileMetadata ReadPortable(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return new()
            {
                Permissions = OperatingSystem.IsWindows() ? "" : FormatPermissions((int) info.UnixFileMode | (info is DirectoryInfo ? 0x4000 : 0x8000)),
                Created = info.CreationTime,
                Accessed = info.LastAccessTime,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new();
        }
    }

    private static string LookupName(IntPtr entry, uint id)
    {
        if (entry == IntPtr.Zero)
        {
            return id.ToString();
        }

        return Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(entry)) ?? id.ToString();
    }

    private static DateTime? ReadTimestamp(byte[] buffer, int offset)
    {
        long seconds = BitConverter.ToInt64(buffer, offset);
        if (seconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
    }

    public static string FormatPermissions(int mode)
    {
        char type = (mode & 0xF000) switch
        {
            0x4000 => 'd',
            0xA000 => 'l',
            _ => '-',
        };
        char[] chars = [type, '-', '-', '-', '-', '-', '-', '-', '-', '-'];
        string letters = "rwxrwxrwx";
        for (int i = 0; i < 9; i++)
        {
            if ((mode & (1 << (8 - i))) != 0)
            {
                chars[i + 1] = letters[i];
            }
        }

        ApplySpecialBit(chars, 3, (mode & 0x800) != 0, 's');
        ApplySpecialBit(chars, 6, (mode & 0x400) != 0, 's');
        ApplySpecialBit(chars, 9, (mode & 0x200) != 0, 't');
        return new string(chars);
    }

    private static void ApplySpecialBit(char[] chars, int index, bool isSet, char letter)
    {
        if (!isSet)
        {
            return;
        }

        chars[index] = chars[index] == 'x' ? letter : char.ToUpperInvariant(letter);
    }
}
