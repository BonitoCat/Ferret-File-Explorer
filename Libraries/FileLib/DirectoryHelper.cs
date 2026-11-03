namespace FileLib;

public static class DirectoryHelper
{
    public static string GetAppDataDirPath(bool create = true) =>
        GetPath("XDG_DATA_HOME", ".local/share", Environment.SpecialFolder.LocalApplicationData, create);

    public static string GetAppConfigDirPath(bool create = true) =>
        GetPath("XDG_CONFIG_HOME", ".config", Environment.SpecialFolder.ApplicationData, create);

    public static string GetCacheDirPath(bool create = true) =>
        GetPath("XDG_CACHE_HOME", ".cache", Environment.SpecialFolder.LocalApplicationData, create);

    public static void CreateDir(string path) => Directory.CreateDirectory(path);

    private static string GetPath(string variable, string fallback, Environment.SpecialFolder windowsFolder, bool create)
    {
        string? configured = Environment.GetEnvironmentVariable(variable);
        string path = configured != null && Path.IsPathFullyQualified(configured)
            ? configured
            : OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(windowsFolder)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);
        if (create)
            Directory.CreateDirectory(path);
        return path;
    }
}
