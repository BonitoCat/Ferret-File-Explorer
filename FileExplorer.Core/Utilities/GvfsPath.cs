using System.Runtime.InteropServices;
using System.Text;

namespace FileExplorer.Core.Utilities;

public static partial class GvfsPath
{
    private const string GvfsSegment = "/gvfs/";
    private const int DirentNameOffset = 19;

    [LibraryImport("libc.so.6", EntryPoint = "getuid")]
    private static partial uint GetUid();

    [LibraryImport("libc.so.6", EntryPoint = "opendir", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr OpenDir(string path);

    [LibraryImport("libc.so.6", EntryPoint = "readdir64")]
    private static partial IntPtr ReadDir(IntPtr dir);

    [LibraryImport("libc.so.6", EntryPoint = "closedir")]
    private static partial int CloseDir(IntPtr dir);

    public static string MountDirectory
    {
        get
        {
            string? runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(runtimeDir) && OperatingSystem.IsLinux())
            {
                runtimeDir = $"/run/user/{GetUid()}";
            }

            return Path.Combine(runtimeDir ?? "", "gvfs");
        }
    }

    public static List<string> GetMountRoots()
    {
        return GetMountRoots(MountDirectory);
    }

    public static List<string> GetMountRoots(string gvfsDirectory)
    {
        List<string> roots = new();
        if (!OperatingSystem.IsLinux())
        {
            return roots;
        }

        IntPtr dir = OpenDir(gvfsDirectory);
        if (dir == IntPtr.Zero)
        {
            return roots;
        }

        try
        {
            for (IntPtr entry = ReadDir(dir); entry != IntPtr.Zero; entry = ReadDir(dir))
            {
                string? name = Marshal.PtrToStringUTF8(entry + DirentNameOffset);
                if (!string.IsNullOrEmpty(name) && name is not "." and not "..")
                {
                    roots.Add(Path.Combine(gvfsDirectory, name));
                }
            }
        }
        finally
        {
            CloseDir(dir);
        }

        return roots;
    }

    public static bool TryGetMountRoot(string path, out string mountRoot)
    {
        mountRoot = "";
        if (!PathManager.IsRemote(path))
        {
            return false;
        }

        int gvfsIndex = path.IndexOf(GvfsSegment, StringComparison.Ordinal);
        int nameStart = gvfsIndex + GvfsSegment.Length;
        int nameEnd = path.IndexOf('/', nameStart);
        if (nameStart >= path.Length || nameEnd == nameStart)
        {
            return false;
        }

        mountRoot = nameEnd < 0 ? path : path[..nameEnd];
        return true;
    }

    public static bool IsMounted(string path)
    {
        if (!TryGetMountRoot(path, out string mountRoot))
        {
            return false;
        }

        string gvfsDirectory = Path.GetDirectoryName(mountRoot) ?? MountDirectory;
        return GetMountRoots(gvfsDirectory).Contains(mountRoot, StringComparer.Ordinal);
    }

    public static string? ToUri(string path)
    {
        if (!TryGetMountRoot(path, out string mountRoot))
        {
            return null;
        }

        string? rootUri = MountNameToUri(Path.GetFileName(mountRoot));
        if (rootUri == null)
        {
            return null;
        }

        string rest = path[mountRoot.Length..].Trim('/');
        if (rest.Length == 0)
        {
            return rootUri;
        }

        string escaped = string.Join('/', rest.Split('/').Select(Uri.EscapeDataString));
        return rootUri.TrimEnd('/') + "/" + escaped;
    }

    public static string? MountNameToUri(string mountName)
    {
        int colon = mountName.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        string type = mountName[..colon];
        Dictionary<string, string> spec = ParseSpec(mountName[(colon + 1)..]);
        spec.TryGetValue("user", out string? user);
        spec.TryGetValue("port", out string? port);
        spec.TryGetValue("prefix", out string? prefix);

        return type switch
        {
            "sftp" or "ftp" or "ftps" or "nfs" => BuildUri(type, spec.GetValueOrDefault("host"), user, port, prefix),
            "dav" => BuildUri(spec.GetValueOrDefault("ssl") == "true" ? "davs" : "dav", spec.GetValueOrDefault("host"), user, port, prefix),
            "smb-share" => BuildUri("smb", spec.GetValueOrDefault("server"), JoinDomainUser(spec.GetValueOrDefault("domain"), user), port, spec.GetValueOrDefault("share")),
            "afp-volume" => BuildUri("afp", spec.GetValueOrDefault("host"), user, port, spec.GetValueOrDefault("volume")),
            _ => null,
        };
    }

    private static Dictionary<string, string> ParseSpec(string spec)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (string pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            result[pair[..equals]] = Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return result;
    }

    private static string? JoinDomainUser(string? domain, string? user)
    {
        if (string.IsNullOrEmpty(user))
        {
            return null;
        }

        return string.IsNullOrEmpty(domain) ? user : $"{domain};{user}";
    }

    private static string? BuildUri(string scheme, string? host, string? user, string? port, string? path)
    {
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        StringBuilder builder = new();
        builder.Append(scheme).Append("://");
        if (!string.IsNullOrEmpty(user))
        {
            builder.Append(Uri.EscapeDataString(user).Replace("%3B", ";")).Append('@');
        }

        builder.Append(host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host);
        if (!string.IsNullOrEmpty(port))
        {
            builder.Append(':').Append(port);
        }

        builder.Append('/');
        string trimmed = (path ?? "").Trim('/');
        if (trimmed.Length > 0)
        {
            builder.Append(string.Join('/', trimmed.Split('/').Select(Uri.EscapeDataString))).Append('/');
        }

        return builder.ToString();
    }
}
