using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Input;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Utilities;
using LoggerLib;

namespace FileExplorer.Gui.Services;

public sealed record KeybindDefinition(string Id, string TitleKey, string CategoryKey, string DefaultGesture);

public sealed class KeybindService : INotifyPropertyChanged
{
    public const string AddonPrefix = "addon:";

    public static readonly IReadOnlyList<KeybindDefinition> Definitions =
    [
        new("NewWindow", "NewWindow", "KeybindCategoryGeneral", "Ctrl+N"),
        new("CloseTab", "KeybindCloseTab", "KeybindCategoryGeneral", "Ctrl+W"),
        new("Quit", "KeybindQuit", "KeybindCategoryGeneral", "Ctrl+Q"),
        new("Search", "KeybindSearch", "KeybindCategoryGeneral", "Ctrl+F"),
        new("Location", "KeybindLocation", "KeybindCategoryGeneral", "Ctrl+L"),
        new("ConnectToServer", "ConnectToServer", "KeybindCategoryGeneral", "Ctrl+K"),
        new("Back", "Back", "KeybindCategoryNavigation", "Alt+Left"),
        new("Forward", "Forward", "KeybindCategoryNavigation", "Alt+Right"),
        new("Up", "KeybindUp", "KeybindCategoryNavigation", "Alt+Up"),
        new("Refresh", "Reload", "KeybindCategoryNavigation", "F5"),
        new("SwitchPane", "KeybindSwitchPane", "KeybindCategoryNavigation", "F6"),
        new("NewFolder", "NewFolder", "KeybindCategoryFiles", "Ctrl+Shift+N"),
        new("NewFile", "NewFile", "KeybindCategoryFiles", "Ctrl+Alt+N"),
        new("Cut", "Cut", "KeybindCategoryFiles", "Ctrl+X"),
        new("Copy", "Copy", "KeybindCategoryFiles", "Ctrl+C"),
        new("Paste", "Paste", "KeybindCategoryFiles", "Ctrl+V"),
        new("PasteSymlink", "PasteAsSymlink", "KeybindCategoryFiles", "Ctrl+Alt+V"),
        new("Duplicate", "Duplicate", "KeybindCategoryFiles", "Ctrl+D"),
        new("Rename", "Rename", "KeybindCategoryFiles", "F2"),
        new("Trash", "Trash", "KeybindCategoryFiles", "Delete"),
        new("DeletePermanent", "DeletePermanent", "KeybindCategoryFiles", "Shift+Delete"),
        new("Properties", "Properties", "KeybindCategoryFiles", "Alt+Enter"),
        new("SelectAll", "SelectAll", "KeybindCategoryFiles", "Ctrl+A"),
        new("ToggleHidden", "ShowHidden", "KeybindCategoryView", "Ctrl+H"),
        new("DualPane", "DualPane", "KeybindCategoryView", "F3"),
    ];

    private static readonly string StorePath = Path.Combine(AppPaths.DataDir, "keybinds.json");

    private readonly Dictionary<string, string> _overrides;
    private readonly Dictionary<string, KeyGesture?> _parsed = new(StringComparer.Ordinal);
    private int _version;

    public static KeybindService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    private KeybindService()
    {
        _overrides = Load();
    }

    public int Version => _version;

    public static string GetAddonId(AddonAction action)
    {
        return AddonPrefix + action.Key;
    }

    public static string? GetDefault(string id)
    {
        return Definitions.FirstOrDefault(definition => definition.Id == id)?.DefaultGesture;
    }

    public KeyGesture? Get(string id)
    {
        return Get(id, GetDefault(id));
    }

    public KeyGesture? Get(string id, string? defaultGesture)
    {
        return Parse(_overrides.TryGetValue(id, out string? custom) ? custom : defaultGesture);
    }

    public KeyGesture? Get(AddonAction action)
    {
        return Get(GetAddonId(action), action.Shortcut);
    }

    public bool Matches(string id, KeyEventArgs e)
    {
        return Get(id)?.Matches(e) == true;
    }

    public bool IsCustomized(string id)
    {
        return _overrides.ContainsKey(id);
    }

    public void Set(string id, KeyGesture? gesture, string? defaultGesture)
    {
        string value = gesture?.ToString() ?? "";
        if (string.Equals(value, Parse(defaultGesture)?.ToString() ?? "", StringComparison.Ordinal))
        {
            _overrides.Remove(id);
        }
        else
        {
            _overrides[id] = value;
        }

        Save();
    }

    public void Reset(string id)
    {
        if (_overrides.Remove(id))
        {
            Save();
        }
    }

    public void ResetAll()
    {
        _overrides.Clear();
        Save();
    }

    public static string Format(KeyGesture? gesture)
    {
        return gesture?.ToString() ?? "";
    }

    public static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;
    }

    private KeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (_parsed.TryGetValue(text, out KeyGesture? cached))
        {
            return cached;
        }

        KeyGesture? gesture = null;
        try
        {
            gesture = KeyGesture.Parse(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            _ = Logger.LogW($"Invalid shortcut '{text}'");
        }

        _parsed[text] = gesture;
        return gesture;
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureDir(AppPaths.DataDir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_overrides, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = Logger.LogW($"Could not save shortcuts: {ex.Message}");
        }

        _version++;
        PropertyChanged?.Invoke(this, new(nameof(Version)));
        Changed?.Invoke();
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath)) ?? new();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _ = Logger.LogW($"Could not load shortcuts: {ex.Message}");
        }

        return new();
    }
}
