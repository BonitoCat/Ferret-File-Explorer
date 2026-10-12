using System.Diagnostics;

using FileExplorer.Cli.Context;
using FileExplorer.Cli.Keybinds;
using FileExplorer.Cli.Models;
using FileExplorer.Cli.Utilities;
using FileExplorer.Core.Services;
using FileExplorer.Core.States;

using Generated;

using FileLib;
using LoggerLib;

using InputLib;
using InputLib.EventArgs;
using InputLib.PlatformListener;

using TuiLib;
using TuiLib.Components;
using TuiLib.Context;
using TuiLib.Controls;
using TuiLib.Helpers;
using TuiLib.Rendering;

namespace FileExplorer.Cli;

public static class Program
{
    public static readonly TuiRenderer Renderer = new();
    private static List<EntryContext> _contexts = new();
    private static int _selectedContextIndex = 0;
    public static EntryContext SelectedContext => _contexts[_selectedContextIndex];
    public static InputListener Listener { get; set; } = null!;
    public static bool PauseRendering { get; set; } = false;
    public static Action<KeyDownEventArgs>? ModalKeyHandler { get; set; }
    public static Action? ModalResizeHandler { get; set; }
    private static List<Keybind> _keybinds = new();
    
    private static readonly FileOperationService _operationService = new();
    private static readonly FileExecutionService _executionService = new();
    private static readonly BookmarkService _bookmarkService = new();
    
    public static readonly string HelpStr =
        $"""
         
          Usage:
           {Process.GetCurrentProcess().ProcessName} [OPTIONS]
          
          Options:
           -h / --help - Show this menu
           -v / --version - Show installed version
           -o / --open - Open current directory in the default file explorer
           -d / --directory (path) - The directory to start in
           -t / --tty - Force {Process.GetCurrentProcess().ProcessName} to start with tty input
          
          Controls:
           Navigation:
           | Up / Down Arrow - Navigate items
           | Shift + Up / Down Arrow - Select multiple items upwards / downwards
           | Page Up / Page Down - Navigate items page-wise
           | Home / Ctrl + Up Arrow - Go to first item
           | End / Ctrl + Down Arrow - Go to last item
           | F6 / Ctrl + Left / Right Arrow - Switch between split panes
           | Enter / Arrow Right - Open selected directory / file
           | Ctrl + Enter - Open selected file in nano
           | Shift + Enter / Alt + Enter - Open properties of directory / file
           | Escape / Arrow Left / Alt + Up Arrow - Go up one directory
           | Alt + Left Arrow - Return to previous directory
           | Alt + Right Arrow - Go forward to next directory
           | Ctrl + L / Ctrl + W - Go to specific directory by path
           | F4 - Switch to Quick Access / Bookmarks
           | Ctrl + D - Open terminal in current directory
           
           Editing:
           | F2 - Rename selected item
           | Delete - Move item to trash
           | Shift + Delete - Permanently delete item
           | Space - Select / deselect item
           | Shift + Space - Select a region of items
           | Ctrl + A - Select all directories and files
           | Shift + A - Deselect all directories and files
           | Ctrl + B - Add / remove current folder in bookmarks
           | Ctrl + C - Copy item
           | Ctrl + X - Cut item
           | Ctrl + V - Paste item
           | Ctrl + N - Create new file
           | Shift + N - Create new directory
           | Shift + D - Duplicate directory / file
           
           Misc:
           | F3 - Toggle split pane
           | F5 / Ctrl + R - Reload current directory
           | Ctrl + F - Search in current directory
           | Ctrl + H - Toggle visibility of hidden files / directories
           | Alt + S / Ctrl + J - Toggle visibility of file sizes
           | Ctrl + O - Open current directory in OS file explorer
           | Shift + O - Open current directory with higher privileges
           | Shift + C - Copy current directory path to clipboard
           | F7 / Shift + R - Connect to network server (SFTP, SMB, FTP, WebDAV)
           | F8 / Shift + U - Dismount current network share or device
           | F1 - Show this help menu
           | F10 - Exit file explorer
         
         """;

    public static readonly ManualResetEventSlim ExitEvent = new(false);
    
    public static void Main(string[] args)
    {
        string? _startDir = null;
        bool _forceTtyInput = false;

        for (int i = 0; i < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    Console.WriteLine(HelpStr);
                    return;
                
                case "--version":
                case "-v":
                    string version = BuildInfo.Version;
                    Console.WriteLine($"v{version}\n");
                    
                    return;
                
                case "--open":
                case "-o":
                    if (OperatingSystem.IsLinux())
                    {
                        Process proc = new()
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "nemo",
                                Arguments = Environment.CurrentDirectory,
                                UseShellExecute = false,
                            },
                        };
                        
                        proc.Start();
                    }
                    
                    return;

                case "--directory":
                case "-d":
                    if (args.Length > i + 1)
                    {
                        _startDir = args[i + 1];
                    }
                    else
                    {
                        i--;
                    }
                break;
                
                case "--tty":
                case "-t":
                    _forceTtyInput = true;
                    i--;
                break;
            }
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("dav://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("davs://", StringComparison.OrdinalIgnoreCase))
            {
                NetworkService netService = new();
                Task<(bool Success, string MountPath, string ErrorMessage)> mountTask = netService.MountUriAsync(arg);
                mountTask.Wait();
                if (mountTask.Result.Success && Directory.Exists(mountTask.Result.MountPath))
                {
                    _startDir = mountTask.Result.MountPath;
                }
            }
        }

        if (_startDir != null && Directory.Exists(_startDir))
        {
            Directory.SetCurrentDirectory(_startDir);
        }

        try
        {
            Logger.LogDir = Path.Combine(DirectoryHelper.GetAppDataDirPath(), "fe", "logs");
            Logger.KeepLogs = 20;
            Logger.LogDebug = false;
            Logger.CreateFile();
            Logger.LogS($"{AppDomain.CurrentDomain.FriendlyName} CLI started");
        }
        catch
        {
        }

        InputListener.DisableEcho();
        if (_forceTtyInput)
        {
            Listener = new TtyInputListener();
        }
        else
        {
            Listener = InputListener.New() ?? new TtyInputListener();
        }
        
        _executionService.OnOpenTerminalForExecutable = path =>
        {
            Window window = new()
            {
                Title = Path.GetFileName(path),
                WorkingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path),
            };

            WindowManager.Instance.Add(window);

            if (Directory.Exists(path))
            {
                window.Launch();
                return;
            }

            string extraArgs = window.LaunchInfo.ExecutablePath == "cmd.exe"
                ? $"/c \"{path}\""
                : $"\"{path}\"";

            window.Launch(extraArgs);
        };

        Console.CursorVisible = false;
        WindowManager.Instance.MainWindow.Title = Environment.IsPrivilegedProcess
            ? "Terminal File-Explorer (Privileged)"
            : "Terminal File-Explorer";
        Console.Title = WindowManager.Instance.MainWindow.Title;

        _contexts.Add(CreateContext());
        UpdateContexts();
        
        Listener.RepeatIntervalMs = 30;
        Listener.OnKeyDown += OnKeyDown;
        
        Listener.StartListening();
        
        Renderer.ClearScreen();
        
        int lastWidth = Console.WindowWidth;
        int lastHeight = Console.WindowHeight;
        
        while (!ExitEvent.IsSet)
        {
            if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
            {
                lastWidth = Console.WindowWidth;
                lastHeight = Console.WindowHeight;
                Renderer.ClearScreen();
                UpdateContexts();
                ModalResizeHandler?.Invoke();
            }

            if (PauseRendering)
            {
                Thread.Sleep(16);
                continue;
            }
        
            RenderExplorer();
            Thread.Sleep(16);
        }

        Renderer.ClearScreen();
        InputListener.EnableEcho();
    }

    public static void RenderExplorer()
    {
        Renderer.Render(GetExplorerRenderParts());
    }

    public static List<RenderPart> GetExplorerRenderParts()
    {
        if (_contexts.Count == 1)
        {
            EntryListItem? selectedItem = SelectedContext.Menu.SelectedItem?.Value;
            SelectedContext.HasActiveImagePreview = ImagePreviewRenderer.IsImage(selectedItem) && Console.WindowWidth >= 60;
        }
        else
        {
            SelectedContext.HasActiveImagePreview = false;
        }

        List<RenderPart> parts = new();
        for (int ctxIndex = 0; ctxIndex < _contexts.Count; ctxIndex++)
        {
            EntryContext context = _contexts[ctxIndex];
            parts.AddRange(context.Menu.Render());
            
            bool isSelected = ctxIndex == _selectedContextIndex;
            Color topBarColor = isSelected ? Color.White : Color.LightGray;
            
            int maxWidth = context.Menu.MaxWidth;
            string cwdText = $" File-Explorer ({(context.Cwd == _bookmarkService.BookmarkDir ? "Bookmarks" : context.Cwd)})";
            if (cwdText.Length > maxWidth)
            {
                cwdText = " ..." + cwdText.Substring(cwdText.Length - maxWidth + 4);
            }
            string pathFill = cwdText + new string(' ', Math.Max(0, maxWidth - cwdText.Length));
            
            parts.Add(new RenderPart 
            {
                X = context.Menu.X,
                Y = 0,
                Text = pathFill,
                Foreground = topBarColor,
                Background = Environment.IsPrivilegedProcess ? Color.Red.Transform(-50, 40, 50) : Color.Reset,
                ZIndex = -2,
            });
            
            int emptyLineWidth = context.HasActiveImagePreview ? context.Menu.MaxWidth / 2 - 1 : context.Menu.MaxWidth - 1;
            int menuCount = context.Menu.GetViewItems().Count;
            
            for (int i = menuCount; i < context.Menu.MaxHeight; i++)
            {
                parts.Add(new RenderPart 
                {
                    X = context.Menu.X + 1, 
                    Y = context.Menu.Y + i, 
                    Text = new string(' ', Math.Max(0, emptyLineWidth)), 
                    Foreground = Color.Reset, 
                    Background = Color.Reset, 
                    ZIndex = -1,
                });
            }
        }

        if (SelectedContext.HasActiveImagePreview)
        {
            EntryListItem? selectedItem = SelectedContext.Menu.SelectedItem?.Value;
            parts.AddRange(ImagePreviewRenderer.GetPreviewParts(
                selectedItem!.Entry.FullPath,
                SelectedContext.Menu.Y + 1,
                SelectedContext.Menu.MaxHeight,
                Console.WindowWidth,
                Console.WindowHeight));
        }
        else
        {
            ImagePreviewRenderer.ClearCache();
        }

        return parts;
    }

    private static EntryContext CreateContext()
    {
        TuiListBox<EntryListItem> entries = new()
        {
            MaxWidth = Console.WindowWidth,
            MaxHeight = Math.Max(Console.WindowHeight - 4, 0),
            UseDefaultMarker = false,
            ScrollOvershoot = 3,
        };
        
        entries.Height = entries.MaxHeight;
        entries.ViewRange = entries.MaxHeight;
        entries.Y = 2;

        EntryContext ctx = new()
        {
            Menu = entries,
            Navigation = new NavigationState(Directory.GetCurrentDirectory()),
        };
        
        entries.ItemRenderer = entry => EntryItemRenderer.RenderModel(ctx, entry, ctx == SelectedContext);
        FolderUtils.LoadFolderContent(ctx, ctx.Cwd);

        ctx.Listener = Listener;
        
        return ctx;
    }

    private static void UpdateContexts()
    {
        for (int i = 0; i < _contexts.Count; i++)
        {
            int index = i;
            _contexts[index].Menu.MaxWidth = Console.WindowWidth / _contexts.Count;
            _contexts[index].Menu.MaxHeight = Math.Max(Console.WindowHeight - 4, 0);
            _contexts[index].Menu.Height = _contexts[index].Menu.MaxHeight;
            _contexts[index].Menu.ViewRange = _contexts[index].Menu.MaxHeight;
            _contexts[index].Menu.X = Console.WindowWidth / _contexts.Count * index;
            _contexts[index].Menu.Y = 2;
            _contexts[index].Menu.ItemRenderer = entry => EntryItemRenderer.RenderModel(_contexts[index], entry, _contexts[index] == SelectedContext);
        }
        
        MapKeybinds(SelectedContext);
        Directory.SetCurrentDirectory(SelectedContext.Cwd);
    }

    private static void SwitchContext(int indexOffset)
    {
        _selectedContextIndex = (_selectedContextIndex + indexOffset + _contexts.Count) % _contexts.Count;
        MapKeybinds(SelectedContext);
        
        for (int i = 0; i < _contexts.Count; i++)
        {
            int index = i;
            _contexts[index].Menu.ItemRenderer = entry => EntryItemRenderer.RenderModel(_contexts[index], entry, _contexts[index] == SelectedContext);
        }
        
        Directory.SetCurrentDirectory(SelectedContext.Cwd);
    }

    private static void ToggleSplitPane()
    {
        if (_contexts.Count == 1)
        {
            EntryContext newCtx = CreateContext();
            newCtx.Navigation.NavigateTo(SelectedContext.Cwd);
            FolderUtils.LoadFolderContent(newCtx, newCtx.Cwd);
            _contexts.Add(newCtx);
        }
        else
        {
            _contexts.RemoveAll(c => c != SelectedContext);
            _selectedContextIndex = 0;
        }
        
        UpdateContexts();
    }

    private static void MapKeybinds(EntryContext context)
    {
        _keybinds.Clear();
        _keybinds.Add(new NavUpKeybind { Context = context, Keys = new() { Key.ArrowUp } });
        _keybinds.Add(new NavDownKeybind { Context = context, Keys = new() { Key.ArrowDown } });
        _keybinds.Add(new ExecuteKeybind(_executionService) { Context = context, Keys = new() { Key.Enter } });
        _keybinds.Add(new ExecuteKeybind(_executionService) { Context = context, Keys = new() { Key.ArrowRight } });
        _keybinds.Add(new NanoKeybind { Context = context, Keys = new() { Key.Ctrl, Key.Enter } });
        _keybinds.Add(new NavigateUpKeybind { Context = context, Keys = new() { Key.ArrowLeft } });
        _keybinds.Add(new NavigateUpKeybind { Context = context, Keys = new() { Key.Escape } });
        _keybinds.Add(new NavigateUpKeybind { Context = context, Keys = new() { Key.Alt, Key.ArrowUp } });
        _keybinds.Add(new GoBackKeybind { Context = context, Keys = new() { Key.Alt, Key.ArrowLeft } });
        _keybinds.Add(new GoForwardKeybind { Context = context, Keys = new() { Key.Alt, Key.ArrowRight } });
        _keybinds.Add(new SelectKeybind { Context = context, Keys = new() { Key.Space } });
        _keybinds.Add(new RegionSelectKeybind { Context = context, Keys = new() { Key.Shift, Key.Space } });
        _keybinds.Add(new DeleteKeybind(_operationService, permanent: false) { Context = context, Keys = new() { Key.Delete } });
        _keybinds.Add(new DeleteKeybind(_operationService, permanent: true) { Context = context, Keys = new() { Key.Shift, Key.Delete } });
        _keybinds.Add(new CopyKeybind { Context = context, Keys = new() { Key.Ctrl, Key.C } });
        _keybinds.Add(new CutKeybind { Context = context, Keys = new() { Key.Ctrl, Key.X } });
        _keybinds.Add(new PasteKeybind(_operationService) { Context = context, Keys = new() { Key.Ctrl, Key.V } });
        _keybinds.Add(new SearchKeybind { Context = context, Keys = new() { Key.Ctrl, Key.F } });
        _keybinds.Add(new CmdKeybind(_executionService) { Context = context, Keys = new() { Key.Ctrl, Key.D } });
        _keybinds.Add(new RenameKeybind(_operationService) { Context = context, Keys = new() { Key.F2 } });
        _keybinds.Add(new DuplicateKeybind(_operationService) { Context = context, Keys = new() { Key.Shift, Key.D } });
        _keybinds.Add(new NewFolderKeybind(_operationService) { Context = context, Keys = new() { Key.Shift, Key.N } });
        _keybinds.Add(new NewFileKeybind(_operationService) { Context = context, Keys = new() { Key.Ctrl, Key.N } });
        _keybinds.Add(new BookmarkMenuKeybind(_bookmarkService) { Context = context, Keys = new() { Key.F4 } });
        _keybinds.Add(new AddBookmarkKeybind(_bookmarkService) { Context = context, Keys = new() { Key.Ctrl, Key.B } });
        _keybinds.Add(new CopyPathKeybind { Context = context, Keys = new() { Key.Shift, Key.C } });
        _keybinds.Add(new ReloadKeybind { Context = context, Keys = new() { Key.Ctrl, Key.R } });
        _keybinds.Add(new ReloadKeybind { Context = context, Keys = new() { Key.F5 } });
        _keybinds.Add(new PageUpKeybind { Context = context, Keys = new() { Key.PageUp } });
        _keybinds.Add(new PageDownKeybind { Context = context, Keys = new() { Key.PageDown } });
        _keybinds.Add(new HomeKeybind { Context = context, Keys = new() { Key.Home } });
        _keybinds.Add(new HomeKeybind { Context = context, Keys = new() { Key.Ctrl, Key.ArrowUp } });
        _keybinds.Add(new EndKeybind { Context = context, Keys = new() { Key.End } });
        _keybinds.Add(new EndKeybind { Context = context, Keys = new() { Key.Ctrl, Key.ArrowDown } });
        _keybinds.Add(new HideKeybind { Context = context, Keys = new() { Key.Ctrl, Key.H } });
        _keybinds.Add(new SizeKeybind { Context = context, Keys = new() { Key.Alt, Key.S } });
        _keybinds.Add(new SizeKeybind { Context = context, Keys = new() { Key.Ctrl, Key.J } });
        _keybinds.Add(new MetadataKeybind { Context = context, Keys = new() { Key.Alt, Key.Enter } });
        _keybinds.Add(new MetadataKeybind { Context = context, Keys = new() { Key.Shift, Key.Enter } });
        _keybinds.Add(new PrivilegedKeybind { Context = context, Keys = new() { Key.Shift, Key.O } });
        _keybinds.Add(new NemoKeybind { Context = context, Keys = new() { Key.Ctrl, Key.O } });
        _keybinds.Add(new HelpKeybind { Context = context, Keys = new() { Key.F1 } });
        _keybinds.Add(new DirPathKeybind { Context = context, Keys = new() { Key.Ctrl, Key.L } });
        _keybinds.Add(new DirPathKeybind { Context = context, Keys = new() { Key.Ctrl, Key.W } });
        _keybinds.Add(new MultiSelectUpKeybind { Context = context, Keys = new() { Key.Shift, Key.ArrowUp } });
        _keybinds.Add(new MultiSelectDownKeybind { Context = context, Keys = new() { Key.Shift, Key.ArrowDown } });
        _keybinds.Add(new SelectAllKeybind { Context = context, Keys = new() { Key.Ctrl, Key.A } });
        _keybinds.Add(new DeselectAllKeybind { Context = context, Keys = new() { Key.Shift, Key.A } });
        _keybinds.Add(new ActionKeybind(ToggleSplitPane) { Context = context, Keys = new() { Key.F3 } });
        _keybinds.Add(new ActionKeybind(() => SwitchContext(-1)) { Context = context, Keys = new() { Key.Ctrl, Key.ArrowLeft } });
        _keybinds.Add(new ActionKeybind(() => SwitchContext(1)) { Context = context, Keys = new() { Key.Ctrl, Key.ArrowRight } });
        _keybinds.Add(new ActionKeybind(() => SwitchContext(_selectedContextIndex == 0 ? 1 : -1)) { Context = context, Keys = new() { Key.F6 } });
        _keybinds.Add(new ConnectServerKeybind { Context = context, Keys = new() { Key.F7 } });
        _keybinds.Add(new ConnectServerKeybind { Context = context, Keys = new() { Key.Shift, Key.R } });
        _keybinds.Add(new DismountKeybind { Context = context, Keys = new() { Key.F8 } });
        _keybinds.Add(new DismountKeybind { Context = context, Keys = new() { Key.Shift, Key.U } });
    }

    private static void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        if (ModalKeyHandler != null)
        {
            ModalKeyHandler(e);
            return;
        }

        if (e.Key == Key.F10)
        {
            ExitEvent.Set();
            return;
        }

        List<Key> heldKeys = Listener.GetHeldKeys().ToList();
        if (heldKeys.Contains(Key.LeftCtrl) || heldKeys.Contains(Key.RightCtrl))
        {
            heldKeys.Add(Key.Ctrl);
        }

        if (heldKeys.Contains(Key.LeftShift) || heldKeys.Contains(Key.RightShift))
        {
            heldKeys.Add(Key.Shift);
        }
        
        Key mappedKey = e.Key;
        if (mappedKey == Key.LeftCtrl || mappedKey == Key.RightCtrl)
        {
            mappedKey = Key.Ctrl;
        }

        if (mappedKey == Key.LeftShift || mappedKey == Key.RightShift)
        {
            mappedKey = Key.Shift;
        }

        Keybind? bestMatch = _keybinds
            .Where(kb => kb.Keys.All(k => heldKeys.Contains(k)))
            .Where(kb => kb.Keys.Contains(e.Key) || kb.Keys.Contains(mappedKey))
            .OrderByDescending(kb => kb.Keys.Count)
            .FirstOrDefault();

        bestMatch?.OnKeyDown(e);
    }
}
        
