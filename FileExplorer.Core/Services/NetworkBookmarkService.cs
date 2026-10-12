using System.Text.Json;
using FileExplorer.Core.Models;

namespace FileExplorer.Core.Services;

public class NetworkBookmarkService
{
    private readonly string _filePath;
    private readonly CredentialService _credentialService = new();

    public NetworkBookmarkService()
    {
        string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fe");
        Directory.CreateDirectory(configDir);
        _filePath = Path.Combine(configDir, "network_bookmarks.json");
    }

    public List<NetworkBookmark> GetBookmarks()
    {
        if (!File.Exists(_filePath))
        {
            return new();
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            List<NetworkBookmark>? list = JsonSerializer.Deserialize<List<NetworkBookmark>>(json);
            if (list == null)
            {
                return new();
            }

            bool migrated = false;
            foreach (NetworkBookmark b in list)
            {
                if (!string.IsNullOrEmpty(b.Password))
                {
                    _credentialService.StoreCredential(GetCredentialKey(b.Uri), b.Password);
                    b.Password = null;
                    migrated = true;
                }
            }

            if (migrated)
            {
                SaveBookmarks(list);
            }

            return list;
        }
        catch
        {
            return new();
        }
    }

    public void AddBookmark(NetworkBookmark bookmark)
    {
        if (!string.IsNullOrEmpty(bookmark.Password))
        {
            _credentialService.StoreCredential(GetCredentialKey(bookmark.Uri), bookmark.Password);
        }

        List<NetworkBookmark> list = GetBookmarks();
        int existingIndex = list.FindIndex(b => string.Equals(b.Uri, bookmark.Uri, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            list[existingIndex] = bookmark;
        }
        else
        {
            list.Add(bookmark);
        }

        SaveBookmarks(list);
    }

    public void RemoveBookmark(string uri)
    {
        _credentialService.DeleteCredential(GetCredentialKey(uri));

        List<NetworkBookmark> list = GetBookmarks();
        int removedCount = list.RemoveAll(b => string.Equals(b.Uri, uri, StringComparison.OrdinalIgnoreCase));
        if (removedCount > 0)
        {
            SaveBookmarks(list);
        }
    }

    public string? GetPassword(string uri)
    {
        return _credentialService.GetCredential(GetCredentialKey(uri));
    }

    public void SetPassword(string uri, string password)
    {
        _credentialService.StoreCredential(GetCredentialKey(uri), password);
    }

    public void SaveBookmarks(IEnumerable<NetworkBookmark> bookmarks)
    {
        try
        {
            JsonSerializerOptions options = new() { WriteIndented = true };
            string json = JsonSerializer.Serialize(bookmarks, options);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
        }
    }

    private static string GetCredentialKey(string uri)
    {
        return $"fe:network:{uri.TrimEnd('/')}";
    }
}
