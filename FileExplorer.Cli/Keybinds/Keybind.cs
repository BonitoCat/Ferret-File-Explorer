using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using FileExplorer.Cli.Context;
using FileExplorer.Cli.Models;
using FileExplorer.Cli.Utilities;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Services;

using InputLib;
using InputLib.EventArgs;

using TuiLib;
using TuiLib.Controls;

namespace FileExplorer.Cli.Keybinds;

public abstract class Keybind
{
    public required List<Key> Keys { get; init; }
    public required EntryContext Context { get; init; }

    public abstract void OnKeyDown(KeyDownEventArgs e);
}

public class NavUpKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (e.Continuous && Context.Menu.SelectedIndex == 0) return;
        Context.Menu.MoveSelected(-1);
    }
}

public class NavDownKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (e.Continuous && Context.Menu.SelectedIndex == Context.Menu.Items.Count - 1) return;
        Context.Menu.MoveSelected(1);
    }
}

public class GoBackKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Navigation.GoBack();
        
        FolderUtils.LoadFolderContent(Context, Context.Cwd);
        Directory.SetCurrentDirectory(Context.Cwd);
    }
}

public class GoForwardKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Navigation.GoForward();
        FolderUtils.LoadFolderContent(Context, Context.Cwd);
        Directory.SetCurrentDirectory(Context.Cwd);
    }
}

public class NavigateUpKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Navigation.NavigateUp();
        FolderUtils.LoadFolderContent(Context, Context.Cwd);
        Directory.SetCurrentDirectory(Context.Cwd);
        Context.Menu.SelectedIndex = 0;
    }
}

public class ExecuteKeybind : Keybind
{
    private readonly FileExecutionService _executionService;

    public ExecuteKeybind(FileExecutionService executionService)
    {
        _executionService = executionService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        EntryListItem? selected = Context.Menu.SelectedItem?.Value;
        if (selected?.Entry != null)
        {
            if (selected.Entry is FolderEntry folder)
            {
                if (FolderUtils.RequiresElevatedAccess(folder.FullPath) && !Environment.IsPrivilegedProcess && Environment.UserName != "root")
                {
                    return;
                }

                Context.Navigation.NavigateTo(folder.FullPath);
                FolderUtils.LoadFolderContent(Context, Context.Cwd);
                Directory.SetCurrentDirectory(Context.Cwd);
                if (selected.DisplayName.Text == "..")
                {
                    Context.Menu.SelectedIndex = 0;
                }
            }
            else
            {
                _executionService.Execute(selected.Entry);
            }
        }
    }
}

public class DeleteKeybind : Keybind
{
    private readonly FileOperationService _operationService;
    private readonly bool _permanent;

    public DeleteKeybind(FileOperationService operationService, bool permanent = false)
    {
        _operationService = operationService;
        _permanent = permanent;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        IEnumerable<string> paths = Context.SelectedItems.Count > 0 
            ? Context.SelectedItems.Where(i => i.Entry.FullName != "..").Select(i => i.Entry.FullPath) 
            : new[] { Context.Menu.SelectedItem?.Value }.Where(i => i != null && i.Entry.FullName != "..").Select(i => i!.Entry.FullPath).Where(p => p != null).Cast<string>();

        if (paths.Any())
        {
            string actionText = _permanent ? "permanently delete" : "delete";
            string prompt = paths.Count() == 1 
                ? $"Are you sure you want to {actionText} '{System.IO.Path.GetFileName(paths.First())}' [Y/n]: "
                : $"Are you sure you want to {actionText} {paths.Count()} items [Y/n]: ";
                
            string? answer = InputUtils.Input(Context, prompt, "");
            if (answer != null && answer.Trim().ToLower() != "y" && answer.Trim() != "") return;
            if (answer == null) return;
            
            _operationService.Delete(paths, _permanent);
            Context.SelectedItems.Clear();
            Utilities.FolderUtils.LoadFolderContent(Context, Context.Cwd, Context.SearchPattern);
        }
    }
}

public class CopyKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Clipboard.Items.Clear();
        IEnumerable<string> paths = Context.SelectedItems.Count > 0 
            ? Context.SelectedItems.Where(i => i.Entry.FullName != "..").Select(i => i.Entry.FullPath) 
            : new[] { Context.Menu.SelectedItem?.Value }.Where(i => i != null && i.Entry.FullName != "..").Select(i => i!.Entry.FullPath).Where(p => p != null).Cast<string>();

        Context.Clipboard.Items.AddRange(paths);
        Context.Clipboard.Mode = ClipboardMode.Copy;
    }
}

public class CutKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Clipboard.Items.Clear();
        IEnumerable<string> paths = Context.SelectedItems.Count > 0 
            ? Context.SelectedItems.Where(i => i.Entry.FullName != "..").Select(i => i.Entry.FullPath) 
            : new[] { Context.Menu.SelectedItem?.Value }.Where(i => i != null && i.Entry.FullName != "..").Select(i => i!.Entry.FullPath).Where(p => p != null).Cast<string>();

        Context.Clipboard.Items.AddRange(paths);
        Context.Clipboard.Mode = ClipboardMode.Cut;
    }
}

public class PasteKeybind : Keybind
{
    private readonly FileOperationService _operationService;

    public PasteKeybind(FileOperationService operationService)
    {
        _operationService = operationService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (Context.Clipboard.Items.Any())
        {
            string lastItemName = Context.Clipboard.Items[^1];
            
            _operationService.Paste(Context.Cwd, Context.Clipboard.Mode, Context.Clipboard.Items);
            if (Context.Clipboard.Mode == ClipboardMode.Cut)
            {
                Context.Clipboard.Items.Clear();
            }
            
            FolderUtils.LoadFolderContent(Context, Context.Cwd);

            TuiListBoxItem<EntryListItem> item = Context.Menu.Items.First(item => lastItemName == item.Value?.Entry.FullPath);
            Context.Menu.TryScrollTo(item);
        }
    }
}

public class SelectKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        EntryListItem? selected = Context.Menu.SelectedItem?.Value;
        if (selected != null)
        {
            if (selected.Entry.FullName == "..") return;
            if (Context.SelectedItems.Contains(selected))
            {
                Context.SelectedItems.Remove(selected);
            }
            else
            {
                Context.SelectedItems.Add(selected);
            }
        }
    }
}

public class SearchKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        string? searchPattern = Context.Input("Search pattern: ");
        
        if (string.IsNullOrWhiteSpace(searchPattern))
        {
            FolderUtils.LoadFolderContent(Context, Context.Cwd);
        }
        else
        {
            FolderUtils.LoadFolderContent(Context, Context.Cwd, searchPattern);
        }
    }
}

public class CmdKeybind : Keybind
{
    private readonly FileExecutionService? _executionService;

    public CmdKeybind(FileExecutionService? executionService = null)
    {
        _executionService = executionService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Program.PauseRendering = true;
        InputListener.EnableEcho();
        if (Context.Listener != null)
        {
            Context.Listener.PauseListening = true;
        }

        Console.CursorVisible = true;
        Console.Clear();

        string shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
        Process proc = new()
        {
            StartInfo = new()
            {
                FileName = shell,
                Arguments = "-i",
                WorkingDirectory = Context.Cwd,
                UseShellExecute = false,
            },
        };

        proc.Start();
        proc.WaitForExit();

        Console.CursorVisible = false;
        InputListener.DisableEcho();
        Console.Clear();

        if (Context.Listener != null)
        {
            Context.Listener.PauseListening = false;
        }

        Program.PauseRendering = false;
        FolderUtils.LoadFolderContent(Context, Context.Cwd, Context.SearchPattern);
    }
}

public class NanoKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        EntryListItem? selected = Context.Menu.SelectedItem?.Value;
        if (selected == null || selected.Entry is not FileEntry)
        {
            return;
        }

        Program.PauseRendering = true;
        InputListener.EnableEcho();
        if (Context.Listener != null)
        {
            Context.Listener.PauseListening = true;
        }

        Console.CursorVisible = true;
        Console.Clear();

        Process proc = new()
        {
            StartInfo = new()
            {
                FileName = "nano",
                Arguments = $"\"{selected.Entry.FullPath}\"",
                UseShellExecute = false,
            },
        };

        proc.Start();
        proc.WaitForExit();

        Console.CursorVisible = false;
        InputListener.DisableEcho();
        Console.Clear();

        if (Context.Listener != null)
        {
            Context.Listener.PauseListening = false;
        }

        Program.PauseRendering = false;
        FolderUtils.LoadFolderContent(Context, Context.Cwd, Context.SearchPattern);
    }
}

public class RegionSelectKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (Context.Menu.Items.Count == 0)
        {
            return;
        }

        EntryListItem? lastSelected = Context.SelectedItems.LastOrDefault();
        int lastIndex = -1;
        if (lastSelected != null)
        {
            lastIndex = Context.Menu.Items.ToList().FindIndex(item => item.Value == lastSelected);
        }

        if (lastIndex == -1 || lastIndex == Context.Menu.SelectedIndex)
        {
            EntryListItem? current = Context.Menu.SelectedItem?.Value;
            if (current != null && current.Entry.FullName != "..")
            {
                Context.SelectedItems.Add(current);
            }

            return;
        }

        int start = Math.Min(lastIndex, Context.Menu.SelectedIndex);
        int end = Math.Max(lastIndex, Context.Menu.SelectedIndex);

        for (int i = start; i <= end; i++)
        {
            EntryListItem? item = Context.Menu.Items[i].Value;
            if (item != null && item.Entry.FullName != "..")
            {
                Context.SelectedItems.Add(item);
            }
        }
    }
}

public class RenameKeybind : Keybind
{
    private readonly FileOperationService _operationService;

    public RenameKeybind(FileOperationService operationService)
    {
        _operationService = operationService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        EntryListItem? selected = Context.Menu.SelectedItem?.Value;
        
        if (selected == null) return;

        string? newName = Context.Input($"Rename '{selected.Entry.FullName}' to: ", selected.Entry.FullName);
        
        if (!string.IsNullOrWhiteSpace(newName) && newName != selected.Entry.FullName)
        {
            _operationService.Rename(selected.Entry.FullPath, newName);
            FolderUtils.LoadFolderContent(Context, Context.Cwd);
        }
    }
}

public class DuplicateKeybind : Keybind
{
    private readonly FileOperationService _operationService;

    public DuplicateKeybind(FileOperationService operationService)
    {
        _operationService = operationService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        EntryListItem? selected = Context.Menu.SelectedItem?.Value;
        
        if (selected == null) return;

        _operationService.Duplicate(selected.Entry.FullPath);
        FolderUtils.LoadFolderContent(Context, Context.Cwd);
    }
}

public class NewFolderKeybind : Keybind
{
    private readonly FileOperationService _operationService;

    public NewFolderKeybind(FileOperationService operationService)
    {
        _operationService = operationService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        string? folderName = Context.Input("New folder name: ");
        
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            _operationService.CreateFolder(System.IO.Path.Combine(Context.Cwd, folderName));
            FolderUtils.LoadFolderContent(Context, Context.Cwd);
        }
    }
}

public class NewFileKeybind : Keybind
{
    private readonly FileOperationService _operationService;

    public NewFileKeybind(FileOperationService operationService)
    {
        _operationService = operationService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        string? fileName = Context.Input("New file name: ");
        
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            _operationService.CreateFile(System.IO.Path.Combine(Context.Cwd, fileName));
            FolderUtils.LoadFolderContent(Context, Context.Cwd);
        }
    }
}

public class BookmarkMenuKeybind : Keybind
{
    private readonly BookmarkService _bookmarkService;

    public BookmarkMenuKeybind(BookmarkService bookmarkService)
    {
        _bookmarkService = bookmarkService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Navigation.NavigateTo(_bookmarkService.BookmarkDir);
        FolderUtils.LoadFolderContent(Context, Context.Cwd);
    }
}

public class AddBookmarkKeybind : Keybind
{
    private readonly BookmarkService _bookmarkService;

    public AddBookmarkKeybind(BookmarkService bookmarkService)
    {
        _bookmarkService = bookmarkService;
    }

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        EntryListItem? selected = Context.Menu.SelectedItem?.Value;
        string targetPath = selected != null ? selected.Entry.FullPath : Context.Cwd;

        _bookmarkService.AddOrRemoveBookmark(targetPath);
    }
}


public class CopyPathKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        EntryListItem? selected = Context.Menu.SelectedItem?.Value;
        string targetPath = selected != null ? selected.Entry.FullPath : Context.Cwd;

        ClipboardService.Copy(targetPath);
    }
}

public class ReloadKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        FolderUtils.LoadFolderContent(Context, Context.Cwd);
    }
}

public class PageUpKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Menu.MoveSelected(-Context.Menu.ViewRange);
    }
}

public class PageDownKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Menu.MoveSelected(Context.Menu.ViewRange);
    }
}

public class HomeKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Menu.MoveSelected(-Context.Menu.Items.Count);
    }
}

public class EndKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.Menu.MoveSelected(Context.Menu.Items.Count);
    }
}

public class SelectAllKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        foreach (TuiListBoxItem<EntryListItem> item in Context.Menu.Items)
        {
            if (item.Value == null || item.Value.Entry.FullName == "..") continue;
            Context.SelectedItems.Add(item.Value);
        }
    }
}

public class DeselectAllKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.SelectedItems.Clear();
    }
}

public class ActionKeybind : Keybind
{
    private readonly Action _action;
    
    public ActionKeybind(Action action)
    {
        _action = action;
    }

    public override void OnKeyDown(InputLib.EventArgs.KeyDownEventArgs e)
    {
        _action();
    }
}

public class HideKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.ShowHiddenFiles = !Context.ShowHiddenFiles;
        FolderUtils.LoadFolderContent(Context, Context.Cwd, Context.SearchPattern);
    }
}

public class SizeKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Context.ShowFileSizes = !Context.ShowFileSizes;
    }
}

public class PrivilegedKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (Environment.IsPrivilegedProcess)
        {
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        Window window = new();
        WindowManager.Instance.Add(window);

        window.Launch($"bash -c \"sudo '{exe}'\"");
    }
}

public class NemoKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "nemo",
            Arguments = $"\"{Context.Cwd}\"",
            UseShellExecute = false,
        });
    }
}

public class HelpKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        while (Console.KeyAvailable)
        {
            Console.ReadKey(true);
        }

        Program.PauseRendering = true;
        if (Context.Listener != null)
        {
            Context.Listener.PauseListening = true;
        }
        
        Console.Clear();
        Console.WriteLine(Program.HelpStr);
        Console.WriteLine("\n\n Press Escape to return...");
        
        Thread.Sleep(200);

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape) break;
        }

        Console.Clear();
        Program.PauseRendering = false;
        
        InputListener.Flush();
        if (Context.Listener != null)
        {
            Context.Listener.PauseListening = false;
        }
    }
}

public class DirPathKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        string? newPath = InputUtils.Input(Context, "Path:", Context.Cwd);
        if (string.IsNullOrEmpty(newPath) || !Directory.Exists(newPath))
        {
            return;
        }

        if (FolderUtils.RequiresElevatedAccess(newPath) && !Environment.IsPrivilegedProcess && Environment.UserName != "root")
        {
            return;
        }

        Context.Navigation.NavigateTo(newPath);
        FolderUtils.LoadFolderContent(Context, newPath, Context.SearchPattern);
        Directory.SetCurrentDirectory(newPath);
    }
}

public class MultiSelectUpKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (Context.Menu.SelectedItem?.Value is null)
        {
            return;
        }

        if (Context.Menu.SelectedItem.Value.Entry.FullName != "..")
        {
            if (!Context.SelectedItems.Add(Context.Menu.SelectedItem.Value))
            {
                Context.SelectedItems.Remove(Context.Menu.SelectedItem.Value);
            }
        }

        Context.Menu.MoveSelected(-1);
    }
}

public class MultiSelectDownKeybind : Keybind
{
    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (Context.Menu.SelectedItem?.Value is null)
        {
            return;
        }

        if (Context.Menu.SelectedItem.Value.Entry.FullName != "..")
        {
            if (!Context.SelectedItems.Add(Context.Menu.SelectedItem.Value))
            {
                Context.SelectedItems.Remove(Context.Menu.SelectedItem.Value);
            }
        }

        Context.Menu.MoveSelected(1);
    }
}

public class ConnectServerKeybind : Keybind
{
    private readonly NetworkService _networkService = new();

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        string? server = InputUtils.Input(Context, "Server (IP/hostname):", "");
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        string? protocolStr = InputUtils.Input(Context, "Protocol (sftp/smb/ftp/dav/davs):", "sftp");
        if (string.IsNullOrWhiteSpace(protocolStr))
        {
            return;
        }

        NetworkProtocol protocol = protocolStr.Trim().ToLowerInvariant() switch
        {
            "smb" => NetworkProtocol.Smb,
            "ftp" => NetworkProtocol.Ftp,
            "dav" => NetworkProtocol.Dav,
            "davs" => NetworkProtocol.Davs,
            _ => NetworkProtocol.Sftp
        };

        string defaultPort = protocol switch
        {
            NetworkProtocol.Smb => "445",
            NetworkProtocol.Ftp => "21",
            NetworkProtocol.Dav => "80",
            NetworkProtocol.Davs => "443",
            _ => "22"
        };

        string? portStr = InputUtils.Input(Context, $"Port [{defaultPort}]:", defaultPort);
        int? port = null;
        if (int.TryParse(portStr?.Trim(), out int parsedPort) && parsedPort > 0)
        {
            port = parsedPort;
        }

        string? path = InputUtils.Input(Context, "Share / Path (optional):", "");

        string? domain = null;
        if (protocol == NetworkProtocol.Smb)
        {
            domain = InputUtils.Input(Context, "Domain (optional):", "");
        }

        string? user = InputUtils.Input(Context, "Username (optional):", "");
        string? pass = null;
        if (!string.IsNullOrEmpty(user))
        {
            pass = InputUtils.Input(Context, "Password (optional):", "");
        }

        NetworkConnectionInfo info = new()
        {
            Protocol = protocol,
            Host = server.Trim(),
            Port = port,
            Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Username = string.IsNullOrWhiteSpace(user) ? null : user.Trim(),
            Password = string.IsNullOrWhiteSpace(pass) ? null : pass,
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim(),
            Anonymous = string.IsNullOrEmpty(user)
        };

        Task.Run(async () =>
        {
            (bool success, string mountPath, string error) = await _networkService.MountAsync(info);
            if (success && Directory.Exists(mountPath))
            {
                Context.Navigation.NavigateTo(mountPath);
                FolderUtils.LoadFolderContent(Context, mountPath);
                Directory.SetCurrentDirectory(mountPath);
            }
        });
    }
}

public class DismountKeybind : Keybind
{
    private readonly NetworkService _networkService = new();

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        string currentPath = Context.Cwd;
        string gvfsDir = $"/run/user/{NetworkService.GetUserId()}/gvfs";
        bool isGvfs = currentPath.StartsWith(gvfsDir, StringComparison.OrdinalIgnoreCase);
        bool isMedia = currentPath.StartsWith("/media", StringComparison.OrdinalIgnoreCase) || currentPath.StartsWith("/run/media", StringComparison.OrdinalIgnoreCase) || currentPath.StartsWith("/mnt", StringComparison.OrdinalIgnoreCase);

        if (!isGvfs && !isMedia)
        {
            return;
        }

        string mountRoot = currentPath;
        if (isGvfs)
        {
            string rel = Path.GetRelativePath(gvfsDir, currentPath);
            string firstPart = rel.Split(Path.DirectorySeparatorChar)[0];
            mountRoot = Path.Combine(gvfsDir, firstPart);
        }

        Task.Run(async () =>
        {
            (bool success, string error) = await _networkService.UnmountAsync(mountRoot);
            if (success)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Context.Navigation.NavigateTo(home);
                FolderUtils.LoadFolderContent(Context, home);
                Directory.SetCurrentDirectory(home);
            }
        });
    }
}
