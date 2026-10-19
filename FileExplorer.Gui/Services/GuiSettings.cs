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

    public static string? PrimaryColor
    {
        get => GetString("primary_color");
        set => SetString("primary_color", value);
    }

    public static string? AccentColor
    {
        get => GetString("accent_color");
        set => SetString("accent_color", value);
    }

    public static string? SelectionColor
    {
        get => GetString("selection_color");
        set => SetString("selection_color", value);
    }

    private static string? GetString(string key)
    {
        lock (SaveLock)
        {
            return Load().TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value) ? value : null;
        }
    }

    private static void SetString(string key, string? value)
    {
        lock (SaveLock)
        {
            Dictionary<string, string> values = Load();
            if (value == null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value;
            }

            Save(values);
        }
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
            Save(values);
        }
    }

    private static void Save(Dictionary<string, string> values)
    {
        try
        {
            AppPaths.EnsureDir(AppPaths.DataDir);
            File.WriteAllLines(SettingsFile, values.Select(pair => $"{pair.Key}={pair.Value}"));
        }
        catch
        {
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
