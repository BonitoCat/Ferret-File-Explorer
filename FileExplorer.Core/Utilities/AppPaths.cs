using FileLib;

namespace FileExplorer.Core.Utilities;

public static class AppPaths
{
    private const string AppFolderName = "fe";

    public static string DataDir => Path.Combine(DirectoryHelper.GetAppDataDirPath(), AppFolderName);
    public static string CacheDir => Path.Combine(DirectoryHelper.GetCacheDirPath(), AppFolderName);

    private static string LegacyConfigDir => Path.Combine(DirectoryHelper.GetAppConfigDirPath(), AppFolderName);

    public static string GetDataPath(string name)
    {
        EnsureDir(DataDir);
        return Path.Combine(DataDir, name);
    }

    public static string GetDataDir(string name)
    {
        string path = Path.Combine(DataDir, name);
        EnsureDir(path);
        return path;
    }

    public static string GetCacheDir(string name)
    {
        string path = Path.Combine(CacheDir, name);
        EnsureDir(path);
        return path;
    }

    public static void EnsureDir(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            EnsureDir(parent);
        }

        DirectoryHelper.CreateDir(path);
    }

    public static void MigrateLegacyConfig()
    {
        string legacy = LegacyConfigDir;
        if (!Directory.Exists(legacy) || legacy == DataDir)
        {
            return;
        }

        EnsureDir(DataDir);
        foreach (string entry in Directory.EnumerateFileSystemEntries(legacy))
        {
            string target = Path.Combine(DataDir, Path.GetFileName(entry));
            try
            {
                if (Directory.Exists(entry) && !File.Exists(entry))
                {
                    MergeDirectory(entry, target);
                }
                else if (!File.Exists(target))
                {
                    File.Move(entry, target);
                }
            }
            catch
            {
            }
        }

        DeleteIfEmpty(legacy);
    }

    private static void MergeDirectory(string source, string target)
    {
        if (!Directory.Exists(target))
        {
            Directory.Move(source, target);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            string destination = Path.Combine(target, Path.GetFileName(entry));
            if (File.Exists(destination) || Directory.Exists(destination))
            {
                continue;
            }

            if (new FileInfo(entry).LinkTarget != null || File.Exists(entry))
            {
                File.Move(entry, destination);
            }
            else
            {
                Directory.Move(entry, destination);
            }
        }

        DeleteIfEmpty(source);
    }

    private static void DeleteIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }
}
