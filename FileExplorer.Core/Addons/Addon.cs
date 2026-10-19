using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FileExplorer.Core.Addons;

public sealed class Addon
{
    public const string ManifestFileName = "addon.yaml";
    public const string FallbackLanguage = "en";
    public const char TranslationPrefix = '$';

    private static readonly string[] TranslationDirNames = ["translations", "translation"];

    private static readonly IDeserializer ManifestDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, Dictionary<string, string>> _translations = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AddonAction> _actions = new();

    public string Id { get; internal set; }
    public string RootPath { get; }
    public AddonManifest Manifest { get; }
    public bool IsEnabled { get; internal set; } = true;
    public string? ErrorKey { get; private set; }
    public string? ErrorDetail { get; private set; }

    public bool HasError => ErrorKey != null;
    public bool IsActive => IsEnabled && !HasError;
    public string NameKey => string.IsNullOrWhiteSpace(Manifest.Name) ? Id : Manifest.Name.Trim();
    public string Version => Manifest.Version?.Trim() ?? "";
    public string Author => Manifest.Author?.Trim() ?? "";
    public string? IconPath => ResolveExistingPath(Manifest.Icon);
    public IReadOnlyList<AddonAction> Actions => _actions;

    private Addon(string rootPath, AddonManifest manifest)
    {
        RootPath = rootPath;
        Manifest = manifest;
        Id = string.IsNullOrWhiteSpace(manifest.Id) ? Path.GetFileName(rootPath) : manifest.Id.Trim();
    }

    public static Addon Load(string rootPath)
    {
        string manifestPath = Path.Combine(rootPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Failed(rootPath, "AddonErrorManifestMissing", ManifestFileName);
        }

        AddonManifest manifest;
        try
        {
            manifest = ReadManifest(File.ReadAllText(manifestPath));
        }
        catch (YamlException ex)
        {
            return Failed(rootPath, "AddonErrorManifestInvalid", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(rootPath, "AddonErrorManifestInvalid", ex.Message);
        }

        Addon addon = new(rootPath, manifest);
        addon.LoadTranslations();
        addon.LoadActions();
        return addon;
    }

    public static AddonManifest ReadManifest(string yaml)
    {
        AddonManifest? manifest = ManifestDeserializer.Deserialize<AddonManifest?>(yaml);
        if (manifest == null)
        {
            return new();
        }

        manifest.Actions ??= new();
        return manifest;
    }

    public string GetName(string languageCode)
    {
        return Translate(NameKey, languageCode);
    }

    public string GetDescription(string languageCode)
    {
        return Translate(Manifest.Description, languageCode);
    }

    public string Translate(string? key, string languageCode)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "";
        }

        if (!key.StartsWith(TranslationPrefix) || key.Length == 1)
        {
            return key;
        }

        string lookup = key[1..];
        foreach (string language in GetLanguageChain(languageCode))
        {
            if (_translations.TryGetValue(language, out Dictionary<string, string>? strings) && strings.TryGetValue(lookup, out string? value))
            {
                return value;
            }
        }

        return key;
    }

    public string? ResolvePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(RootPath, relativePath.Trim()));
    }

    internal void SetError(string key, string? detail = null)
    {
        if (ErrorKey != null)
        {
            return;
        }

        ErrorKey = key;
        ErrorDetail = detail ?? "";
    }

    private string? ResolveExistingPath(string? relativePath)
    {
        string? path = ResolvePath(relativePath);
        return path != null && File.Exists(path) ? path : null;
    }

    private static Addon Failed(string rootPath, string key, string detail)
    {
        Addon addon = new(rootPath, new());
        addon.SetError(key, detail);
        return addon;
    }

    private static IEnumerable<string> GetLanguageChain(string languageCode)
    {
        List<string> chain = new();
        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            chain.Add(languageCode);
            chain.Add(languageCode.Split('-', '_')[0]);
        }

        chain.Add(FallbackLanguage);
        return chain.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private void LoadTranslations()
    {
        foreach (string dirName in TranslationDirNames)
        {
            string dir = Path.Combine(RootPath, dirName);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
            {
                LoadTranslationFile(file);
            }
        }
    }

    private void LoadTranslationFile(string file)
    {
        Dictionary<string, string>? strings;
        try
        {
            strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            SetError("AddonErrorTranslationInvalid", $"{Path.GetFileName(file)}: {ex.Message}");
            return;
        }

        if (strings == null)
        {
            return;
        }

        string language = Path.GetFileNameWithoutExtension(file);
        if (!_translations.TryGetValue(language, out Dictionary<string, string>? target))
        {
            target = new(StringComparer.OrdinalIgnoreCase);
            _translations[language] = target;
        }

        foreach (KeyValuePair<string, string> entry in strings)
        {
            if (!string.IsNullOrEmpty(entry.Value))
            {
                target[entry.Key] = entry.Value;
            }
        }
    }

    private void LoadActions()
    {
        List<AddonActionManifest> manifests = Manifest.Actions.Where(a => a != null).ToList();
        if (manifests.Count == 0 && !string.IsNullOrWhiteSpace(Manifest.Script))
        {
            manifests.Add(new() { Location = "addons-menu" });
        }

        for (int i = 0; i < manifests.Count; i++)
        {
            AddonActionManifest manifest = manifests[i];
            if (!TryParseLocation(manifest.Location, out AddonLocation location))
            {
                SetError("AddonErrorUnknownLocation", manifest.Location);
                continue;
            }

            if (!TryParseTargets(manifest.Targets, out AddonTarget targets, out string? invalidTarget))
            {
                SetError("AddonErrorUnknownTarget", invalidTarget);
                continue;
            }

            if (!TryParseSeparator(manifest.Separator, out bool separatorBefore, out bool separatorAfter))
            {
                SetError("AddonErrorUnknownSeparator", manifest.Separator);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(manifest.After) && !string.IsNullOrWhiteSpace(manifest.Before))
            {
                SetError("AddonErrorBothAnchors", manifest.Id ?? i.ToString());
                continue;
            }

            AddonAction action = new(this, manifest, location, targets, separatorBefore, separatorAfter, i);
            if (location == AddonLocation.PathBar && action.Keywords.Count == 0)
            {
                SetError("AddonErrorNoKeywords", action.Id);
                continue;
            }

            if (action.Command == null && action.ScriptPath == null)
            {
                SetError("AddonErrorNoScript", action.Id);
                continue;
            }

            if (action.ScriptPath != null && !File.Exists(action.ScriptPath))
            {
                SetError("AddonErrorScriptMissing", Path.GetRelativePath(RootPath, action.ScriptPath));
                continue;
            }

            if (_actions.Any(a => a.Id == action.Id))
            {
                SetError("AddonErrorDuplicateAction", action.Id);
                continue;
            }

            _actions.Add(action);
        }
    }

    private static bool TryParseLocation(string? value, out AddonLocation location)
    {
        string normalized = Normalize(value);
        switch (normalized)
        {
            case "" or "contextmenu" or "context":
                location = AddonLocation.ContextMenu;
                return true;
            case "addonsmenu" or "addons" or "menu":
                location = AddonLocation.AddonsMenu;
                return true;
            case "pathbar" or "path" or "locationbar":
                location = AddonLocation.PathBar;
                return true;
            default:
                location = AddonLocation.ContextMenu;
                return false;
        }
    }

    private static bool TryParseTargets(List<string>? values, out AddonTarget targets, out string? invalid)
    {
        invalid = null;
        if (values == null || values.Count == 0)
        {
            targets = AddonTarget.File | AddonTarget.Folder;
            return true;
        }

        targets = AddonTarget.None;
        foreach (string value in values)
        {
            AddonTarget target = Normalize(value) switch
            {
                "file" or "files" => AddonTarget.File,
                "folder" or "folders" or "directory" or "directories" => AddonTarget.Folder,
                "background" or "empty" => AddonTarget.Background,
                _ => AddonTarget.None,
            };
            if (target == AddonTarget.None)
            {
                invalid = value;
                return false;
            }

            targets |= target;
        }

        return true;
    }

    private static bool TryParseSeparator(string? value, out bool before, out bool after)
    {
        before = false;
        after = false;
        switch (Normalize(value))
        {
            case "" or "none":
                return true;
            case "before" or "above":
                before = true;
                return true;
            case "after" or "below":
                after = true;
                return true;
            case "both" or "around":
                before = true;
                after = true;
                return true;
            default:
                return false;
        }
    }

    private static string Normalize(string? value)
    {
        return (value ?? "").Replace("-", "").Replace("_", "").Replace(" ", "").Trim().ToLowerInvariant();
    }
}
