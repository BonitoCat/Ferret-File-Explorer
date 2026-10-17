using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Gui.Services;

public static class GuiSettings
{
    private static readonly string SettingsFile = Path.Combine(AppPaths.DataDir, "gui_settings");
    private static readonly object SaveLock = new();

    public static bool WindowMaximized
    {
        get => GetBool("window_maximized", false);
        set => SetBool("window_maximized", value);
    }

    private static bool GetBool(string key, bool fallback)
    {
        lock (SaveLock)
        {
            return Load().TryGetValue(key, out string? value) ? value == "true" : fallback;
        }
    }

    private static void SetBool(string key, bool value)
    {
        lock (SaveLock)
        {
            Dictionary<string, string> values = Load();
            values[key] = value ? "true" : "false";
            try
            {
                AppPaths.EnsureDir(AppPaths.DataDir);
                File.WriteAllLines(SettingsFile, values.Select(pair => $"{pair.Key}={pair.Value}"));
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
            if (!File.Exists(SettingsFile))
            {
                return values;
            }

            foreach (string line in File.ReadAllLines(SettingsFile))
            {
                int separator = line.IndexOf('=');
                if (separator > 0)
                {
                    values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }
            }
        }
        catch
        {
        }

        return values;
    }
}
