namespace FileExplorer.Core.Utilities;

public static class PathManager
{
    private const string UnixRoot = "/";

    private static readonly Lazy<string> NativeRoot = new(GetNativeRoot);

    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsCaseInsensitive => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static char Separator => Path.DirectorySeparatorChar;
    public static char[] Separators { get; } = OperatingSystem.IsWindows() ? ['\\', '/'] : ['/'];

    public static StringComparison Comparison => IsCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer Comparer => IsCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string RootPath => NativeRoot.Value;
    public static string HomePath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string ToNative(string path)
    {
        if (!IsWindows || path.Length == 0 || path.Contains("://", StringComparison.Ordinal))
        {
            return path;
        }

        if (path is UnixRoot or "\\")
        {
            return RootPath;
        }

        string native = path.Replace('/', '\\');
        if (native.StartsWith('\\') && !native.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return RootPath + native.TrimStart('\\');
        }

        return native.Length == 2 && native[1] == ':' ? native + '\\' : native;
    }

    public static string ExpandHome(string path)
    {
        if (path != "~" && !path.StartsWith("~/", StringComparison.Ordinal) && !(IsWindows && path.StartsWith("~\\", StringComparison.Ordinal)))
        {
            return path;
        }

        return Path.Combine(HomePath, path[1..].TrimStart(Separators));
    }

    public static string TrimEnd(string path)
    {
        string trimmed = path.TrimEnd(Separators);
        if (trimmed.Length == 0)
        {
            return path.Length == 0 || !IsWindows ? UnixRoot : path[..1];
        }

        string root = GetRoot(path);
        return trimmed.Length < root.Length ? root : trimmed;
    }

    public static string GetRoot(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return RootPath;
        }

        return IsWindows && root.Length == 2 && root[1] == ':' ? root + '\\' : root;
    }

    public static bool IsRoot(string path)
    {
        if (path.Length == 0)
        {
            return false;
        }

        string trimmed = TrimEnd(path);
        return string.Equals(trimmed, GetRoot(trimmed), Comparison);
    }

    public static string GetName(string path)
    {
        return Path.GetFileName(TrimEnd(path));
    }

    public static string GetParent(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (parent != null)
        {
            return parent;
        }

        return path.Length == 0 ? RootPath : GetRoot(path);
    }

    public static string[] GetSegments(string path)
    {
        string root = GetRoot(path);
        string relative = path.StartsWith(root, Comparison) ? path[root.Length..] : path;
        return relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool AreEqual(string a, string b)
    {
        return string.Equals(TrimEnd(a), TrimEnd(b), Comparison);
    }

    public static bool IsSameOrAncestor(string ancestor, string path)
    {
        string normAncestor = TrimEnd(ancestor);
        string normPath = TrimEnd(path);
        if (string.Equals(normAncestor, normPath, Comparison))
        {
            return true;
        }

        return normPath.StartsWith(WithTrailingSeparator(normAncestor), Comparison);
    }

    public static bool IsInside(string container, string path)
    {
        return !AreEqual(container, path) && IsSameOrAncestor(container, path);
    }

    public static string WithTrailingSeparator(string path)
    {
        return path.EndsWith(Separator) || (IsWindows && path.EndsWith('/')) ? path : path + Separator;
    }

    private static string GetNativeRoot()
    {
        if (!IsWindows)
        {
            return UnixRoot;
        }

        string? systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        return string.IsNullOrEmpty(systemRoot) ? "C:\\" : systemRoot;
    }
}
