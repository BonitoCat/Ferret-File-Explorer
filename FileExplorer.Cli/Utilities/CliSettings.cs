using FileExplorer.Core.Utilities;

namespace FileExplorer.Cli.Utilities;

public static class CliSettings
{
    private static readonly string SettingsFile = Path.Combine(AppPaths.DataDir, "cli_settings");
    private static readonly string LegacySidebarFile = Path.Combine(AppPaths.DataDir, "cli_sidebar");
    private static readonly Dictionary<string, string> Values = Load();
    private static readonly object SaveLock = new();

    public static bool SidebarVisible
    {
        get => GetBool("sidebar", true);
        set => SetBool("sidebar", value);
    }

    public static bool ShowHiddenFiles
    {
        get => GetBool("hidden_files", true);
        set => SetBool("hidden_files", value);
    }

    private static bool GetBool(string key, bool fallback)
    {
        lock (SaveLock)
        {
            return Values.TryGetValue(key, out string? value) ? value == "true" : fallback;
        }
    }

    private static void SetBool(string key, bool value)
    {
        lock (SaveLock)
        {
            Values[key] = value ? "true" : "false";
            try
            {
                AppPaths.EnsureDir(AppPaths.DataDir);
                File.WriteAllLines(SettingsFile, Values.Select(pair => $"{pair.Key}={pair.Value}"));
            }
            catch
            {
            }
        }
    }

    private static Dictionary<string, string> Load()
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        try
        {
            if (File.Exists(LegacySidebarFile))
            {
                values["sidebar"] = File.ReadAllText(LegacySidebarFile).Trim() == "hidden" ? "false" : "true";
            }

            if (File.Exists(SettingsFile))
            {
                foreach (string line in File.ReadAllLines(SettingsFile))
                {
                    int separator = line.IndexOf('=');
                    if (separator > 0)
                    {
                        values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                    }
                }
            }
        }
        catch
        {
        }

        return values;
    }
}
