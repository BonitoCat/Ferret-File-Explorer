using System.IO.Enumeration;

using FileExplorer.Cli.Context;
using FileExplorer.Cli.Models;
using FileExplorer.Core.Entries;
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
    
    public static void LoadFolderContent(EntryContext context, string path, string? searchPattern = null)
    {
        Program.PauseRendering = true;
        context.Menu.ClearItems();
        context.SelectedItems.Clear();
        
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
            context.Menu.AddItem(parentListItem);
        }
        
        context.Menu.AddItemRange(
            FileService.EnumerateFolderEntries(path, searchPattern)
                       .Where(entry => context.ShowHiddenFiles || !entry.IsHidden)
                       .Select(entry =>
                       {
                           if (entry is FolderEntry folderEntry)
                           {
                               EntryListItem listEntry = new()
                               {
                                   Entry = entry,
                                   DisplayName = new ColoredText(entry.FullName, FolderColor),
                                   Icon = new ColoredText("\x1b[1m🗁  \x1b[0m", FolderColor),
                               };
                               listEntry.OnClick = () => OnFolderClick(context, listEntry);

                               if (entry.IsHidden)
                               {
                                   listEntry.Suffixes.Add(new ColoredText(" (Hidden)", Color.Gray));
                               }

                               if (RequiresElevatedAccess(entry.FullPath) && !Environment.IsPrivilegedProcess && Environment.UserName != "root")
                               {
                                   listEntry.Suffixes.Add(new ColoredText(" (Access Denied)", Color.Orange));
                               }
                               
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

                           if (entry.IsHidden)
                           {
                               fileListEntry.Suffixes.Add(new ColoredText(" (Hidden)", Color.Gray));
                           }

                           if (RequiresElevatedAccess(entry.FullPath) && !Environment.IsPrivilegedProcess && Environment.UserName != "root")
                           {
                               fileListEntry.Suffixes.Add(new ColoredText(" (Access Denied)", Color.Orange));
                           }

                           return fileListEntry;
                       })
                       .ToList()
        );
        
        context.Menu.Sort(sort =>
        {
            sort.ThenBy(item => item.DisplayName.Text == ".." ? 0 : 1)
                .ThenBy(item => item.Entry is FolderEntry ? 0 : 1)
                .ThenBy(item => item.DisplayName.Text, NameComparer);
        });
        
        context.Menu.SelectedIndex = context.Menu.Items.Count > 1 && parent != null && searchPattern == null ? 1 : 0;
        context.Menu.ViewIndex = 0;
        
        Program.PauseRendering = false;
    }

    public static void OnFolderClick(EntryContext context, EntryListItem entry)
    {
        if (RequiresElevatedAccess(entry.Entry.FullPath) && !Environment.IsPrivilegedProcess && Environment.UserName != "root")
        {
            return;
        }

        context.Navigation.NavigateTo(entry.Entry.FullPath);
        LoadFolderContent(context, entry.Entry.FullPath);
        Directory.SetCurrentDirectory(entry.Entry.FullPath);
        
        if (entry.DisplayName.Text == "..")
        {
            context.Menu.SelectedIndex = 0;
        }
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
