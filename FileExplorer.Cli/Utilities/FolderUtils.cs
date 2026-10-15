using System.IO.Enumeration;

using FileExplorer.Cli.Context;
using FileExplorer.Cli.Models;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;

using TuiLib;
using TuiLib.Controls;

namespace FileExplorer.Cli.Utilities;

public class FolderUtils
{
    private static readonly FileSystemService FileService = new();
    private static readonly NameComparer NameComparer = new();
    private static readonly Color FolderColor = new(132, 180, 250);
    private static readonly Color FileColor = Color.White;
    
    public static Task LoadFolderContent(EntryContext context, string path, string? searchPattern = null, Action? onLoaded = null)
    {
        context.LoadCts?.Cancel();
        CancellationTokenSource cts = new();
        context.LoadCts = cts;
        CancellationToken token = cts.Token;
        bool showHidden = context.ShowHiddenFiles;

        Task<List<EntryListItem>> buildTask = Task.Run(() => BuildItems(context, path, searchPattern, showHidden, token), token);
        if (buildTask.Wait(150))
        {
            Apply(context, cts, buildTask, path, searchPattern, onLoaded);
            return Task.CompletedTask;
        }

        lock (context.Menu.Lock)
        {
            context.Menu.ClearItems();
            context.SelectedItems.Clear();
            context.IsLoading = true;
        }

        return buildTask.ContinueWith(task => Apply(context, cts, task, path, searchPattern, onLoaded), TaskScheduler.Default);
    }

    private static void Apply(EntryContext context, CancellationTokenSource cts, Task<List<EntryListItem>> task, string path, string? searchPattern, Action? onLoaded)
    {
        if (context.LoadCts != cts || task.IsCanceled)
        {
            return;
        }

        List<EntryListItem> items = task.IsFaulted ? new() : task.Result;
        bool hasParent = Directory.GetParent(path) != null && string.IsNullOrEmpty(searchPattern);

        lock (context.Menu.Lock)
        {
            context.Menu.ClearItems();
            context.SelectedItems.Clear();
            context.Menu.AddItemRange(items);
            context.Menu.SelectedIndex = items.Count > 1 && hasParent && searchPattern == null ? 1 : 0;
            context.Menu.ViewIndex = 0;
            context.IsLoading = false;
        }

        onLoaded?.Invoke();
    }

    private static List<EntryListItem> BuildItems(EntryContext context, string path, string? searchPattern, bool showHidden, CancellationToken token)
    {
        List<EntryListItem> items = new();
        bool isRemote = IsRemotePath(path);

        DirectoryInfo? parent = Directory.GetParent(path);
        if (parent != null && string.IsNullOrEmpty(searchPattern))
        {
            FolderEntry parentEntry = new FolderEntry { FullPath = parent.FullName, FullName = ".." };
            EntryListItem parentListItem = new()
            {
                Entry = parentEntry,
                DisplayName = new ColoredText("..", Color.Reset),
                Icon = new ColoredText("   ", Color.Reset),
            };
            
            parentListItem.OnClick = () => OnFolderClick(context, parentListItem);
            items.Add(parentListItem);
        }

        List<EntryListItem> entries = new();
        foreach (IFileSystemEntry entry in FileService.EnumerateFolderEntries(path, searchPattern))
        {
            token.ThrowIfCancellationRequested();
            if (!showHidden && entry.IsHidden)
            {
                continue;
            }

            entries.Add(CreateListItem(context, entry, isRemote));
        }

        entries.Sort((a, b) =>
        {
            int folderOrder = (a.Entry is FolderEntry ? 0 : 1).CompareTo(b.Entry is FolderEntry ? 0 : 1);
            return folderOrder != 0 ? folderOrder : NameComparer.Compare(a.DisplayName.Text, b.DisplayName.Text);
        });

        items.AddRange(entries);
        return items;
    }

    private static EntryListItem CreateListItem(EntryContext context, IFileSystemEntry entry, bool isRemote)
    {
        bool accessDenied = !isRemote && IsAccessDenied(entry.FullPath, entry is FolderEntry);
        if (entry is FolderEntry)
        {
            EntryListItem listEntry = new()
            {
                Entry = entry,
                DisplayName = new ColoredText(entry.FullName, FolderColor),
                Icon = new ColoredText("\x1b[1m🗁  \x1b[0m", FolderColor),
            };
            listEntry.OnClick = () => OnFolderClick(context, listEntry);
            AddSuffixes(listEntry, entry.IsHidden, accessDenied);

            return listEntry;
        }

        FileEntry file = (FileEntry) entry;
        (Color color, string iconText) = file.FileType switch
        {
            FileType.Executable => (new Color(0, 255, 0), "\x1b[1mᐅ  \x1b[0m"),
            FileType.Archive => (new Color(255, 127, 0).Transform(-50, -20, -20), "\x1b[1m🗀  \x1b[0m"),
            FileType.Image => (Color.Yellow, "\x1b[1m🖼  \x1b[0m"),
            FileType.Video => (Color.Orange, "\x1b[1m🎞  \x1b[0m"),
            FileType.Audio => (new Color(255, 0, 0), "\x1b[1m♪  \x1b[0m"),
            FileType.Deb => (Color.Yellow.Transform(-20, -20, -20), "\x1b[1mᐅ  \x1b[0m"),
            _ => (FileColor, "\x1b[1m🗏︎  \x1b[0m"),
        };

        EntryListItem fileListEntry = new()
        {
            Entry = entry,
            DisplayName = new ColoredText(entry.FullName, color),
            Icon = new ColoredText(iconText, color),
        };
        AddSuffixes(fileListEntry, entry.IsHidden, accessDenied);

        return fileListEntry;
    }

    private static void AddSuffixes(EntryListItem item, bool isHidden, bool accessDenied)
    {
        if (isHidden)
        {
            item.Suffixes.Add(new ColoredText(" (Hidden)", Color.Gray));
        }

        if (accessDenied)
        {
            item.Suffixes.Add(new ColoredText(" (Access Denied)", Color.Orange));
        }
    }

    private static bool IsAccessDenied(string path, bool isDirectory)
    {
        if (Environment.IsPrivilegedProcess || Environment.UserName == "root")
        {
            return false;
        }

        return OperatingSystem.IsLinux() ? !FileUtils.CanRead(path, isDirectory) : RequiresElevatedAccess(path);
    }

    public static bool IsRemotePath(string path)
    {
        return path.StartsWith("/run/user/", StringComparison.Ordinal) && path.Contains("/gvfs/", StringComparison.Ordinal);
    }

    public static void OnFolderClick(EntryContext context, EntryListItem entry)
    {
        if (RequiresElevatedAccess(entry.Entry.FullPath) && !Environment.IsPrivilegedProcess && Environment.UserName != "root")
        {
            return;
        }

        context.Navigation.NavigateTo(entry.Entry.FullPath);
        LoadFolderContent(context, entry.Entry.FullPath, null, entry.DisplayName.Text == ".." ? () => context.Menu.SelectedIndex = 0 : null);
        Directory.SetCurrentDirectory(entry.Entry.FullPath);
    }

    public static bool RequiresElevatedAccess(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return false;
            }

            if (Directory.Exists(path))
            {
                using IEnumerator<string> e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                e.MoveNext();
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
