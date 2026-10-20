using System.Text;
using System.Text.Json;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Addons;

public static class AddonSettingsStore
{
    private static readonly object Lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static event Action<string, IReadOnlyDictionary<string, string>>? Changed;

    private static string FilePath => AppPaths.GetDataPath("addon_settings.json");

    public static Dictionary<string, string> GetValues(Addon addon)
    {
        Dictionary<string, string> stored;
        lock (Lock)
        {
            stored = Read().GetValueOrDefault(addon.Id) ?? new();
        }

        Dictionary<string, string> values = new();
        foreach (AddonField setting in addon.Settings)
        {
            values[setting.Id] = stored.TryGetValue(setting.Id, out string? value) ? setting.Normalize(value) : setting.Default;
        }

        return values;
    }

    public static void SetValues(Addon addon, IReadOnlyDictionary<string, string> values)
    {
        Dictionary<string, string> changed = new();
        lock (Lock)
        {
            Dictionary<string, Dictionary<string, string>> all = Read();
            if (!all.TryGetValue(addon.Id, out Dictionary<string, string>? stored))
            {
                stored = new();
                all[addon.Id] = stored;
            }

            foreach (KeyValuePair<string, string> entry in values)
            {
                AddonField? setting = addon.Settings.FirstOrDefault(s => s.Id == entry.Key);
                if (setting == null)
                {
                    continue;
                }

                string value = setting.Normalize(entry.Value);
                string previous = stored.TryGetValue(setting.Id, out string? old) ? setting.Normalize(old) : setting.Default;
                stored[setting.Id] = value;
                if (previous != value)
                {
                    changed[setting.Id] = value;
                }
            }

            Write(all);
        }

        if (changed.Count > 0)
        {
            Changed?.Invoke(addon.Id, changed);
        }
    }

    public static bool TrySetValue(Addon addon, string id, string value)
    {
        if (addon.Settings.All(s => s.Id != id))
        {
            return false;
        }

        SetValues(addon, new Dictionary<string, string> { [id] = value });
        return true;
    }

    public static void Remove(string addonId)
    {
        lock (Lock)
        {
            Dictionary<string, Dictionary<string, string>> all = Read();
            if (all.Remove(addonId))
            {
                Write(all);
            }
        }
    }

    public static string GetEnvironmentName(string settingId)
    {
        StringBuilder builder = new("FE_SETTING_");
        foreach (char c in settingId)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_');
        }

        return builder.ToString();
    }

    private static Dictionary<string, Dictionary<string, string>> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new();
            }

            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    private static void Write(Dictionary<string, Dictionary<string, string>> all)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
