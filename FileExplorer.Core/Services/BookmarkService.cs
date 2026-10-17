using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public class BookmarkService
{
    public string BookmarkDir { get; }

    public BookmarkService()
    {
        BookmarkDir = AppPaths.GetDataDir("bookmarks");
    }

    public void AddOrRemoveBookmark(string targetPath)
    {
        try
        {
            string? existing = FindLink(targetPath);
            if (existing != null)
            {
                DeleteLink(existing);
                return;
            }

            AppPaths.EnsureDir(BookmarkDir);
            Directory.CreateSymbolicLink(GetUniqueLinkPath(targetPath), targetPath);
        }
        catch
        {
        }
    }

    public void RemoveBookmark(string targetPath)
    {
        try
        {
            string? link = FindLink(targetPath);
            if (link != null)
            {
                DeleteLink(link);
            }
        }
        catch
        {
        }
    }

    public IEnumerable<string> GetBookmarks()
    {
        if (!Directory.Exists(BookmarkDir))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(BookmarkDir);
    }

    public static string GetDisplayName(string targetPath)
    {
        string normalized = Normalize(targetPath);
        string name = Path.GetFileName(normalized);
        return string.IsNullOrEmpty(name) ? normalized : name;
    }

    private string? FindLink(string targetPath)
    {
        if (!Directory.Exists(BookmarkDir))
        {
            return null;
        }

        string target = Normalize(targetPath);
        foreach (string entry in Directory.EnumerateFileSystemEntries(BookmarkDir))
        {
            try
            {
                FileInfo link = new(entry);
                if (link.LinkTarget == null)
                {
                    continue;
                }

                string raw = Path.GetFullPath(link.LinkTarget, BookmarkDir);
                if (string.Equals(Normalize(raw), target, StringComparison.Ordinal))
                {
                    return entry;
                }

                FileSystemInfo? resolved = link.ResolveLinkTarget(true);
                if (resolved != null && string.Equals(Normalize(resolved.FullName), target, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private string GetUniqueLinkPath(string targetPath)
    {
        string name = Path.GetFileName(Normalize(targetPath));
        if (string.IsNullOrEmpty(name))
        {
            name = "root";
        }

        string linkPath = Path.Combine(BookmarkDir, name);
        for (int i = 2; File.Exists(linkPath) || Directory.Exists(linkPath) || new FileInfo(linkPath).LinkTarget != null; i++)
        {
            linkPath = Path.Combine(BookmarkDir, $"{name} ({i})");
        }

        return linkPath;
    }

    private void DeleteLink(string linkPath)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(linkPath));
        if (!string.Equals(parent, Path.GetFullPath(BookmarkDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
        {
            return;
        }

        if (Directory.Exists(linkPath))
        {
            Directory.Delete(linkPath);
        }
        else
        {
            File.Delete(linkPath);
        }
    }

    private static string Normalize(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return string.IsNullOrEmpty(full) ? Path.DirectorySeparatorChar.ToString() : full;
    }
}
