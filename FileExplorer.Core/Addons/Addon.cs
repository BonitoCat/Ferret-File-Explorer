using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Addons;

public sealed class Addon
{
    public const string ManifestFileName = "addon.yaml";
    public const string FallbackLanguage = "en";
    public const char TranslationPrefix = '$';

    private static readonly string[] TranslationDirNames = ["translations", "translation"];
    private static readonly Regex TranslationTokenRegex = new(@"\$[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

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
    private readonly Dictionary<AddonHook, string> _hooks = new();
    private readonly List<AddonField> _settings = new();
    private readonly List<AddonDependency> _dependencies = new();
    private readonly List<AddonPlace> _places = new();
    private readonly List<AddonDonation> _donations = new();
    private readonly List<string> _invalidDonations = new();

    public string Id { get; internal set; }
    public string RootPath { get; }
    public AddonManifest Manifest { get; }
    public bool IsEnabled { get; internal set; } = true;
    public string? ErrorKey { get; private set; }
    public string? ErrorDetail { get; private set; }
    public AddonMode Modes { get; private set; } = AddonMode.Explorer;

    public bool HasError => ErrorKey != null;
    public bool IsActive => IsEnabled && !HasError;
    public string NameKey => string.IsNullOrWhiteSpace(Manifest.Name) ? Id : Manifest.Name.Trim();
    public string Version => Manifest.Version?.Trim() ?? "";
    public string Author => Manifest.Author?.Trim() ?? "";
    public string? IconPath => ResolveExistingPath(Manifest.Icon);
    public IReadOnlyList<AddonAction> Actions => _actions;
    public IReadOnlyDictionary<AddonHook, string> Hooks => _hooks;
    public IReadOnlyList<AddonField> Settings => _settings;
    public IReadOnlyList<AddonPlace> Places => _places;
    public IReadOnlyList<AddonDependency> Dependencies => _dependencies;
    public IReadOnlyList<AddonDonation> Donations => _donations;
    public IReadOnlyList<string> InvalidDonations => _invalidDonations;
    public string? ServicePath { get; private set; }

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
        addon.LoadModes();
        addon.LoadHooks();
        addon.LoadService();
        addon.LoadSettings();
        addon.LoadPlaces();
        addon.LoadDependencies();
        addon.LoadDonations();
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

    public string TranslateText(string? text, string languageCode)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(TranslationPrefix))
        {
            return text ?? "";
        }

        return TranslationTokenRegex.Replace(text, match => Translate(match.Value, languageCode));
    }

    public bool SupportsMode(AddonMode mode)
    {
        return (Modes & mode) != 0;
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

    internal static IEnumerable<string> GetLanguageChain(string languageCode)
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

    private void LoadModes()
    {
        if (!TryParseModes(Manifest.Modes, AddonMode.Explorer, out AddonMode modes, out string? invalid))
        {
            SetError("AddonErrorUnknownMode", invalid);
            return;
        }

        Modes = modes;
    }

    private void LoadHooks()
    {
        if (Manifest.Hooks == null)
        {
            return;
        }

        foreach (KeyValuePair<string, string> entry in Manifest.Hooks)
        {
            if (!TryParseHook(entry.Key, out AddonHook hook))
            {
                SetError("AddonErrorUnknownHook", entry.Key);
                continue;
            }

            string? scriptPath = ResolvePath(entry.Value);
            if (scriptPath == null)
            {
                continue;
            }

            if (!File.Exists(scriptPath))
            {
                SetError("AddonErrorScriptMissing", Path.GetRelativePath(RootPath, scriptPath));
                continue;
            }

            _hooks[hook] = scriptPath;
        }
    }

    private void LoadService()
    {
        string? scriptPath = ResolvePath(Manifest.Service);
        if (scriptPath == null)
        {
            return;
        }

        if (!File.Exists(scriptPath))
        {
            SetError("AddonErrorScriptMissing", Path.GetRelativePath(RootPath, scriptPath));
            return;
        }

        ServicePath = scriptPath;
    }

    private void LoadSettings()
    {
        foreach (AddonSettingManifest manifest in Manifest.Settings?.Where(s => s != null) ?? [])
        {
            string id = manifest.Id?.Trim() ?? "";
            if (id.Length == 0)
            {
                SetError("AddonErrorSettingNoId", manifest.Title);
                continue;
            }

            if (!AddonField.TryParseType(manifest.Type, out AddonFieldType type))
            {
                SetError("AddonErrorUnknownSettingType", manifest.Type);
                continue;
            }

            List<string> choices = (manifest.Choices ?? new())
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToList();
            if (type == AddonFieldType.Choice && choices.Count == 0)
            {
                SetError("AddonErrorSettingNoChoices", id);
                continue;
            }

            if (_settings.Any(s => s.Id == id))
            {
                SetError("AddonErrorDuplicateSetting", id);
                continue;
            }

            string label = string.IsNullOrWhiteSpace(manifest.Title) ? id : manifest.Title.Trim();
            _settings.Add(new(id, label, manifest.Description?.Trim() ?? "", type, manifest.Default, choices, manifest.Min, manifest.Max));
        }
    }

    private void LoadPlaces()
    {
        foreach (AddonPlaceManifest manifest in Manifest.Places?.Where(p => p != null) ?? [])
        {
            if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Path))
            {
                SetError("AddonErrorPlaceIncomplete", manifest.Name ?? manifest.Path);
                continue;
            }

            string section = string.IsNullOrWhiteSpace(manifest.Section) ? NameKey : manifest.Section.Trim();
            _places.Add(new(section, manifest.Name.Trim(), ExpandHome(manifest.Path.Trim()), manifest.Icon?.Trim()));
        }
    }

    private void LoadDependencies()
    {
        foreach (object? entry in Manifest.Dependencies ?? [])
        {
            if (entry is string command)
            {
                AddDependency(command, command, new Dictionary<string, string>());
                continue;
            }

            if (entry is not IDictionary<object, object> map)
            {
                SetError("AddonErrorDependencyInvalid", entry?.ToString() ?? "");
                continue;
            }

            Dictionary<string, string> values = map
                .Where(e => e.Key is string && e.Value is string)
                .ToDictionary(e => ((string) e.Key).Trim().ToLowerInvariant(), e => ((string) e.Value).Trim());
            Dictionary<string, string> packages = values
                .Where(e => AddonDependency.PackageManagers.Contains(e.Key) && e.Value.Length > 0)
                .ToDictionary(e => e.Key, e => e.Value);
            AddDependency(values.GetValueOrDefault("command", ""), values.GetValueOrDefault("package"), packages);
        }
    }

    private void AddDependency(string command, string? package, IReadOnlyDictionary<string, string> packages)
    {
        command = command.Trim();
        if (command.Length == 0)
        {
            SetError("AddonErrorDependencyNoCommand", package ?? "");
            return;
        }

        if (_dependencies.Any(d => d.Command == command))
        {
            return;
        }

        _dependencies.Add(new(command, string.IsNullOrWhiteSpace(package) ? null : package.Trim(), packages));
    }

    private void LoadDonations()
    {
        List<object> entries = Manifest.Donate switch
        {
            null => [],
            List<object> list => list,
            _ => [Manifest.Donate],
        };

        foreach (object? entry in entries)
        {
            string url = entry?.ToString()?.Trim() ?? "";
            if (!AddonDonation.TryParse(url, out AddonDonation? donation) || donation == null)
            {
                _invalidDonations.Add(url);
                continue;
            }

            if (_donations.Any(d => d.Provider == donation.Provider))
            {
                continue;
            }

            _donations.Add(donation);
        }
    }

    public static string ExpandHome(string path)
    {
        return PathManager.ExpandHome(path);
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

            if (!TryParseModes(manifest.Modes, Modes, out AddonMode modes, out string? invalidMode))
            {
                SetError("AddonErrorUnknownMode", invalidMode);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(manifest.After) && !string.IsNullOrWhiteSpace(manifest.Before))
            {
                SetError("AddonErrorBothAnchors", manifest.Id ?? i.ToString());
                continue;
            }

            AddonAction action = new(this, manifest, location, targets, modes, separatorBefore, separatorAfter, i);
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

    private static bool TryParseModes(List<string>? values, AddonMode fallback, out AddonMode modes, out string? invalid)
    {
        invalid = null;
        if (values == null || values.Count == 0)
        {
            modes = fallback;
            return true;
        }

        modes = AddonMode.None;
        foreach (string value in values)
        {
            AddonMode mode = Normalize(value) switch
            {
                "explorer" => AddonMode.Explorer,
                "picker" or "pickers" => AddonMode.Picker,
                "filepicker" or "openpicker" => AddonMode.FilePicker,
                "folderpicker" => AddonMode.FolderPicker,
                "savepicker" => AddonMode.SavePicker,
                "all" => AddonMode.All,
                _ => AddonMode.None,
            };
            if (mode == AddonMode.None)
            {
                invalid = value;
                return false;
            }

            modes |= mode;
        }

        return true;
    }

    private static bool TryParseHook(string? value, out AddonHook hook)
    {
        switch (Normalize(value))
        {
            case "postinstall":
                hook = AddonHook.PostInstall;
                return true;
            case "preuninstall":
                hook = AddonHook.PreUninstall;
                return true;
            case "postload":
                hook = AddonHook.PostLoad;
                return true;
            case "poststart":
                hook = AddonHook.PostStart;
                return true;
            case "preclose":
                hook = AddonHook.PreClose;
                return true;
            default:
                hook = AddonHook.PostInstall;
                return false;
        }
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
