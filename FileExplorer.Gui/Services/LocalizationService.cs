using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Platform;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Gui.Services;

public class LocalizationService : INotifyPropertyChanged
{
    private const string DefaultLanguage = "en";
    private const string ResourceRoot = "avares://FileExplorer.Gui/Assets/Languages/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    public string this[string key] => Get(key);

    public string LanguageCode { get; private set; } = DefaultLanguage;

    public static string UserLanguageDir => Path.Combine(AppPaths.DataDir, "languages");

    public LocalizationService()
    {
        LoadDefaultStrings();
        LoadSystemLanguage();
    }

    public static string Get(string key)
    {
        return Instance.GetString(key);
    }

    public static string Format(string key, params object?[] args)
    {
        string format = Instance.GetString(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return $"{format} {string.Join(" ", args)}";
        }
    }

    public static string Plural(string key, long count)
    {
        return Format(count == 1 ? $"{key}One" : $"{key}Many", count);
    }

    public string GetString(string key, string? fallback = null)
    {
        if (_strings.TryGetValue(key, out string? value))
        {
            return value;
        }

        return fallback ?? key;
    }

    public void LoadLanguage(string jsonContent)
    {
        if (!Merge(jsonContent))
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public static List<string> GetLanguageCandidates()
    {
        List<string> names = new();
        string? languageList = Environment.GetEnvironmentVariable("LANGUAGE");
        if (!string.IsNullOrWhiteSpace(languageList))
        {
            names.Add(languageList.Split(':', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
        }

        names.Add(CultureInfo.CurrentUICulture.Name);

        foreach (string raw in names)
        {
            string name = raw.Split('.', '@')[0].Replace('_', '-').Trim();
            if (string.IsNullOrEmpty(name) || name is "C" or "POSIX")
            {
                continue;
            }

            List<string> candidates = new();
            string language = name.Split('-')[0].ToLowerInvariant();
            candidates.Add(language);
            if (name.Contains('-'))
            {
                candidates.Add(name);
            }

            return candidates;
        }

        return new() { DefaultLanguage };
    }

    private void LoadDefaultStrings()
    {
        LoadFrom(DefaultLanguage);
    }

    private void LoadSystemLanguage()
    {
        foreach (string candidate in GetLanguageCandidates())
        {
            if (string.Equals(candidate, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (LoadFrom(candidate))
            {
                LanguageCode = candidate;
            }
        }
    }

    private bool LoadFrom(string language)
    {
        bool loaded = false;
        try
        {
            Uri uri = new($"{ResourceRoot}{language}.json");
            if (AssetLoader.Exists(uri))
            {
                using Stream stream = AssetLoader.Open(uri);
                using StreamReader reader = new(stream);
                loaded |= Merge(reader.ReadToEnd());
            }
        }
        catch
        {
        }

        try
        {
            string userFile = Path.Combine(UserLanguageDir, $"{language}.json");
            if (File.Exists(userFile))
            {
                loaded |= Merge(File.ReadAllText(userFile));
            }
        }
        catch
        {
        }

        return loaded;
    }

    private bool Merge(string jsonContent)
    {
        Dictionary<string, string>? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (loaded is null)
        {
            return false;
        }

        foreach (KeyValuePair<string, string> kvp in loaded)
        {
            if (!string.IsNullOrEmpty(kvp.Value))
            {
                _strings[kvp.Key] = kvp.Value;
            }
        }

        return true;
    }
}
