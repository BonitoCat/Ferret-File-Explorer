using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace FileExplorer.Gui.Services;

public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    public string this[string key] => Get(key);

    public LocalizationService()
    {
        LoadDefaultStrings();
    }

    public static string Get(string key)
    {
        return Instance.GetString(key);
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
        Dictionary<string, string>? loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
        if (loaded is null)
        {
            return;
        }
        
        foreach (KeyValuePair<string, string> kvp in loaded)
        {
            _strings[kvp.Key] = kvp.Value;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private void LoadDefaultStrings()
    {
        _strings["File"] = "File";
        _strings["Edit"] = "Edit";
        _strings["View"] = "View";
        _strings["Go"] = "Go";
        _strings["Help"] = "Help";

        _strings["NewWindow"] = "New Window";
        _strings["NewFolder"] = "New Folder";
        _strings["NewFile"] = "New File";
        _strings["Properties"] = "Properties";
        _strings["Close"] = "Close";

        _strings["Cut"] = "Cut";
        _strings["Copy"] = "Copy";
        _strings["Paste"] = "Paste";
        _strings["Duplicate"] = "Duplicate";
        _strings["Rename"] = "Rename...";
        _strings["Compress"] = "Compress...";
        _strings["SelectAll"] = "Select All";
        _strings["Preferences"] = "Preferences";

        _strings["Reload"] = "Reload";
        _strings["ShowHidden"] = "Show Hidden Files";
        _strings["IconView"] = "Icon View";
        _strings["ListView"] = "List View";
        _strings["DualPane"] = "Dual Pane";
        _strings["PreviewPane"] = "Preview Pane";

        _strings["Back"] = "Back";
        _strings["Forward"] = "Forward";
        _strings["Up"] = "Up";
        _strings["Home"] = "Home";
        _strings["Root"] = "File System";
        _strings["Location"] = "Enter Location...";

        _strings["About"] = "About";
        _strings["Shortcuts"] = "Keyboard Shortcuts";

        _strings["Open"] = "Open";
        _strings["OpenWith"] = "Open With...";
        _strings["OpenTerminal"] = "Open in Terminal";
        _strings["Trash"] = "Trash";
        _strings["DeletePermanent"] = "Delete Permanently";
        _strings["BookmarkFolder"] = "Bookmark this Folder";

        _strings["Places"] = "PLACES";
        _strings["Bookmarks"] = "BOOKMARKS";
        _strings["Devices"] = "DEVICES";
        _strings["Network"] = "NETWORK";

        _strings["Name"] = "Name";
        _strings["Size"] = "Size";
        _strings["Type"] = "Type";
        _strings["Modified"] = "Modified";
        _strings["LocationLabel"] = "Location";
        _strings["Accessed"] = "Accessed";
        _strings["Permissions"] = "Permissions";
        _strings["Owner"] = "Owner";

        _strings["SearchWatermark"] = "Search...";
        _strings["ItemsCount"] = "items";
        _strings["SelectedCount"] = "selected";
        _strings["FreeSpace"] = "free";
        _strings["AccessDenied"] = "Access Denied";
        _strings["Hidden"] = "Hidden";
        _strings["PreviewDetails"] = "Preview & Details";

        _strings["AlwaysOpenWith"] = "Always open this file type with this program";
        _strings["ChooseProgram"] = "Choose Program";
        _strings["CommandOrExecutable"] = "Application command or path:";
        _strings["OK"] = "OK";
        _strings["Cancel"] = "Cancel";

        _strings["OpenAsRoot"] = "Open as Root";
        _strings["RemoveBookmark"] = "Remove Bookmark";
        _strings["RemoveServer"] = "Remove Server";
        _strings["Loading"] = "Loading";

        _strings["ConnectToServer"] = "Connect to Server...";
        _strings["Dismount"] = "Dismount";
        _strings["Server"] = "Server";
        _strings["Protocol"] = "Type";
        _strings["Port"] = "Port";
        _strings["Folder"] = "Folder";
        _strings["Username"] = "Username";
        _strings["Password"] = "Password";
        _strings["Domain"] = "Domain name";
        _strings["Anonymous"] = "Connect as guest / anonymous";
        _strings["Connect"] = "Connect";
        _strings["Connecting"] = "Connecting...";
        _strings["DismountSuccess"] = "dismounted successfully";
        _strings["DismountFailed"] = "Failed to dismount";
        _strings["ExtractHere"] = "Extract Here";
        _strings["ResetLayout"] = "Reset to default layout";
        _strings["Mounting"] = "Mounting";
        _strings["Mounted"] = "Mounted";
        _strings["MountFailed"] = "Failed to mount";
    }
}
