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
        string dirName = Path.GetFileName(targetPath);
        if (string.IsNullOrEmpty(dirName))
        {
            dirName = targetPath.Replace(Path.DirectorySeparatorChar, '_');
        }

        string linkPath = Path.Combine(BookmarkDir, dirName);

        if (Directory.Exists(linkPath))
        {
            Directory.Delete(linkPath);
        }
        else
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
    }

    public void RemoveBookmark(string targetPath)
    {
        if (!Directory.Exists(BookmarkDir))
        {
            return;
        }

        string dirName = Path.GetFileName(targetPath);
        string linkPath = Path.Combine(BookmarkDir, dirName);
        if (Directory.Exists(linkPath))
        {
            Directory.Delete(linkPath);
            return;
        }

        foreach (string entry in Directory.EnumerateDirectories(BookmarkDir))
        {
            try
            {
                DirectoryInfo dir = new(entry);
                FileSystemInfo? target = dir.ResolveLinkTarget(true);
                if (target != null && string.Equals(target.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(entry);
                    return;
                }
            }
            catch { }
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
}
