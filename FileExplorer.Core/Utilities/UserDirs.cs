namespace FileExplorer.Core.Utilities;

public static class UserDirs
{
    private static readonly Lazy<Dictionary<string, string>> Dirs = new(Load);

    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Desktop => Get("DESKTOP", "Desktop");
    public static string Documents => Get("DOCUMENTS", "Documents");
    public static string Downloads => Get("DOWNLOAD", "Downloads");
    public static string Music => Get("MUSIC", "Music");
    public static string Pictures => Get("PICTURES", "Pictures");
    public static string Videos => Get("VIDEOS", "Videos");

    private static string Get(string key, string fallbackName)
    {
        if (Dirs.Value.TryGetValue(key, out string? path))
        {
            return path;
        }

        return Path.Combine(Home, fallbackName);
    }

    private static Dictionary<string, string> Load()
    {
        Dictionary<string, string> dirs = new(StringComparer.Ordinal);
        string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
        {
            configHome = Path.Combine(Home, ".config");
        }

        string file = Path.Combine(configHome, "user-dirs.dirs");
        try
        {
            if (!File.Exists(file))
            {
                return dirs;
            }

            foreach (string rawLine in File.ReadLines(file))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("XDG_") || !line.Contains("_DIR="))
                {
                    continue;
                }

                int separator = line.IndexOf("_DIR=", StringComparison.Ordinal);
                string key = line[4..separator];
                string value = line[(separator + 5)..].Trim().Trim('"');
                if (value.StartsWith("$HOME"))
                {
                    value = Home + value[5..];
                }

                value = value.TrimEnd('/');
                if (!Path.IsPathRooted(value) || value == Home.TrimEnd('/'))
                {
                    continue;
                }

                dirs[key] = value;
            }
        }
        catch
        {
        }

        return dirs;
    }
}
