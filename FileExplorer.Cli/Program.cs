using System.Diagnostics;

using FileExplorer.Cli.Context;
using FileExplorer.Cli.Keybinds;
using FileExplorer.Cli.Models;
using FileExplorer.Cli.Sidebar;
using FileExplorer.Cli.Utilities;
using FileExplorer.Core.Services;
using FileExplorer.Core.States;
using FileExplorer.Core.Utilities;

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
    private static readonly object ContextsLock = new();
    public static EntryContext SelectedContext
    {
        get
        {
            lock (ContextsLock)
            {
                return _contexts[Math.Clamp(_selectedContextIndex, 0, _contexts.Count - 1)];
            }
        }
    }
    public static InputListener Listener { get; set; } = null!;
    public static bool PauseRendering { get; set; } = false;
    public static Action<KeyDownEventArgs>? ModalKeyHandler { get; set; }
    public static Action? ModalResizeHandler { get; set; }
    private static List<Keybind> _keybinds = new();
    public static readonly SidebarPane Sidebar = new();
    private static DateTime _lastSidebarReload = DateTime.MinValue;
    
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
           | Ctrl + Left Arrow (in the left pane) - Focus the sidebar
           | Ctrl + S - Focus the sidebar, keeping the current pane as its target
           | Ctrl + Right Arrow / Escape / Ctrl + S (in the sidebar) - Return to the file pane
           | Enter / Arrow Right - Open selected directory / file
           | Ctrl + Enter - Open selected file in nano
           | Alt + O - Open selected file with… (optionally always use that app)
           | Type a name - Jump to the first matching item
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
           | F9 - Toggle sidebar (places, bookmarks, devices, network), remembered between sessions
           | F5 / Ctrl + R - Reload current directory
           | Ctrl + F - Search in current directory
           | Ctrl + H - Toggle visibility of hidden files / directories, remembered between sessions
           | Alt + S / Ctrl + J - Toggle visibility of file sizes
           | Ctrl + O - Open current directory in OS file explorer
           | Shift + O - Open current directory with higher privileges
           | Shift + C - Copy current directory path to clipboard
           | F7 / Shift + R - Connect to network server (SFTP, SMB, FTP, WebDAV)
           | F8 / Shift + U - Dismount current network share or device
           
           Sidebar:
           | Enter / Arrow Right - Open place, mount device or connect to saved server
           | Network - Browse computers and shares nearby (open shares connect directly, others ask for a login)
           | Delete - Remove bookmark / saved server
           | Shift + Up / Down Arrow - Move entry within its section (saved, shared with the GUI)
           | Ctrl + Shift + Up / Down Arrow - Move whole section (saved, shared with the GUI)
           | F7 - Connect to a new server (saved for next time)
           | F8 - Unmount selected device / server
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

        AppPaths.MigrateLegacyConfig();

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

        TerminalHelper.BeginSession();
        Console.CancelKeyPress += (_, _) => TerminalHelper.EndSession();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => TerminalHelper.EndSession();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TerminalHelper.EndSession();
        WindowManager.Instance.MainWindow.Title = Environment.IsPrivilegedProcess
            ? "Terminal File-Explorer (Privileged)"
            : "Terminal File-Explorer";
        Console.Title = WindowManager.Instance.MainWindow.Title;

        _contexts.Add(CreateContext());
        UpdateContexts();
        Sidebar.Reload();
        _lastSidebarReload = DateTime.UtcNow;
        
        Listener.RepeatIntervalMs = 30;
        Listener.OnKeyDown += OnKeyDown;
        
        Listener.StartListening();
        
        Renderer.ClearScreen();
        
        int lastWidth = Console.WindowWidth;
        int lastHeight = Console.WindowHeight;
        bool lastSidebarShown = Sidebar.ShouldShow;

        try
        {
            while (!ExitEvent.IsSet)
            {
                if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight || Sidebar.ShouldShow != lastSidebarShown)
                {
                    lastWidth = Console.WindowWidth;
                    lastHeight = Console.WindowHeight;
                    lastSidebarShown = Sidebar.ShouldShow;
                    Renderer.ClearScreen();
                    UpdateContexts();
                    ModalResizeHandler?.Invoke();
                }

                if (Sidebar.ShouldShow && DateTime.UtcNow - _lastSidebarReload > TimeSpan.FromSeconds(5))
                {
                    _lastSidebarReload = DateTime.UtcNow;
                    Sidebar.Reload();
                }

                if (PauseRendering)
                {
                    Thread.Sleep(16);
                    continue;
                }

                RenderExplorer();
                Thread.Sleep(16);
            }
        }
        finally
        {
            Renderer.ClearScreen();
            TerminalHelper.EndSession();
            InputListener.EnableEcho();
        }
    }

    public static void RenderExplorer()
    {
        Renderer.Render(GetExplorerRenderParts());
    }

    public static List<RenderPart> GetExplorerRenderParts()
    {
        lock (ContextsLock)
        {
            return GetExplorerRenderPartsCore();
        }
    }

    private static List<RenderPart> GetExplorerRenderPartsCore()
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
        if (Sidebar.ShouldShow)
        {
            Sidebar.CurrentPath = SelectedContext.Cwd;
            Sidebar.TargetLabel = _contexts.Count > 1 ? (_selectedContextIndex == 0 ? "left pane" : "right pane") : null;
            parts.AddRange(Sidebar.Render(Environment.IsPrivilegedProcess));
        }

        parts.Add(new RenderPart
        {
            X = 0,
            Y = Math.Max(Console.WindowHeight - 1, 0),
            Text = GetStatusText().Length > Console.WindowWidth - 1
                ? GetStatusText()[..Math.Max(Console.WindowWidth - 1, 0)]
                : GetStatusText().PadRight(Math.Max(Console.WindowWidth - 1, 0)),
            Foreground = Color.Gray,
            ZIndex = -1,
        });

        parts.Add(GetPathBarPart());

        for (int ctxIndex = 0; ctxIndex < _contexts.Count; ctxIndex++)
        {
            EntryContext context = _contexts[ctxIndex];
            parts.AddRange(context.Menu.Render());

            if (context.IsLoading)
            {
                parts.Add(new RenderPart
                {
                    X = context.Menu.X,
                    Y = context.Menu.Y,
                    Text = "   Loading…".PadRight(Math.Max(context.Menu.MaxWidth - 1, 0)),
                    Foreground = Color.Gray,
                });
            }

            int emptyLineWidth = context.HasActiveImagePreview ? context.Menu.MaxWidth / 2 - 1 : context.Menu.MaxWidth;
            int menuCount = context.Menu.GetViewItems().Count;
            
            for (int i = menuCount; i < context.Menu.MaxHeight; i++)
            {
                parts.Add(new RenderPart 
                {
                    X = context.Menu.X, 
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

    private static RenderPart GetPathBarPart()
    {
        EntryContext context = SelectedContext;
        int x = _contexts[0].Menu.X;
        int width = Math.Max(Console.WindowWidth - x, 0);

        string location = context.Cwd == _bookmarkService.BookmarkDir ? "Bookmarks"
            : FolderUtils.IsNetworkView(context.Cwd) ? NetworkUtils.GetTitle(context.Cwd)
            : Sidebar.GetDisplayPath(context.Cwd);
        string text = $" File-Explorer ({location})";
        if (text.Length > width)
        {
            text = " ..." + text[Math.Max(text.Length - width + 4, 0)..];
        }

        return new RenderPart
        {
            X = x,
            Y = 0,
            Text = text.Length > width ? text[..width] : text.PadRight(width),
            Foreground = Sidebar.IsFocused ? new Color(120, 180, 255) : Color.White,
            Background = Environment.IsPrivilegedProcess ? Color.Red.Transform(-50, 40, 50) : Color.Reset,
            ZIndex = -2,
        };
    }

    private static EntryContext CreateContext()
    {
        TuiListBox<EntryListItem> entries = new()
        {
            MaxWidth = Console.WindowWidth,
            MaxHeight = Math.Max(Console.WindowHeight - 3, 0),
            UseDefaultMarker = false,
            ScrollOvershoot = 3,
        };
        
        entries.Height = entries.MaxHeight;
        entries.ViewRange = entries.MaxHeight;
        entries.Y = 1;

        EntryContext ctx = new()
        {
            Menu = entries,
            Navigation = new NavigationState(Directory.GetCurrentDirectory()),
        };
        
        entries.ItemRenderer = entry => EntryItemRenderer.RenderModel(ctx, entry, IsContextFocused(ctx));
        FolderUtils.LoadFolderContent(ctx, ctx.Cwd);

        ctx.Listener = Listener;
        
        return ctx;
    }

    private static bool IsContextFocused(EntryContext context)
    {
        return context == SelectedContext && !Sidebar.IsFocused;
    }

    private static void UpdateContexts()
    {
        lock (ContextsLock)
        {
            int sidebarWidth = Sidebar.ShouldShow ? Math.Clamp(Console.WindowWidth / 5, 18, 30) : 0;
            int paneWidth = (Console.WindowWidth - sidebarWidth) / _contexts.Count;
            int listHeight = Math.Max(Console.WindowHeight - 3, 0);
            Sidebar.Layout(sidebarWidth, listHeight);
            if (!Sidebar.ShouldShow)
            {
                Sidebar.IsFocused = false;
            }

            for (int i = 0; i < _contexts.Count; i++)
            {
                TuiListBox<EntryListItem> menu = _contexts[i].Menu;
                menu.MaxWidth = paneWidth;
                menu.MaxHeight = listHeight;
                menu.Height = menu.MaxHeight;
                menu.ViewRange = menu.MaxHeight;
                menu.X = sidebarWidth + paneWidth * i;
                menu.Y = 1;
            }

            AssignItemRenderers();
            MapKeybinds(SelectedContext);
            FolderUtils.SetWorkingDirectory(SelectedContext.Cwd);
        }
    }

    private static void SwitchContext(int indexOffset)
    {
        lock (ContextsLock)
        {
            _selectedContextIndex = (_selectedContextIndex + indexOffset + _contexts.Count) % _contexts.Count;
            MapKeybinds(SelectedContext);
            AssignItemRenderers();
            FolderUtils.SetWorkingDirectory(SelectedContext.Cwd);
        }
    }

    private static void AssignItemRenderers()
    {
        foreach (EntryContext context in _contexts)
        {
            EntryContext ctx = context;
            ctx.Menu.ItemRenderer = entry => EntryItemRenderer.RenderModel(ctx, entry, IsContextFocused(ctx));
        }
    }

    private static void FocusLeft()
    {
        if (_selectedContextIndex == 0 && Sidebar.ShouldShow)
        {
            Sidebar.IsFocused = true;
            return;
        }

        SwitchContext(-1);
    }

    private static void FocusSidebar()
    {
        if (!Sidebar.ShouldShow)
        {
            return;
        }

        Sidebar.IsFocused = true;
    }

    private static void ToggleSidebar()
    {
        Sidebar.IsVisible = !Sidebar.IsVisible;
        Sidebar.IsFocused = Sidebar.IsVisible && Sidebar.IsFocused;
        Sidebar.SaveVisibility();
        if (Sidebar.IsVisible)
        {
            Sidebar.Reload();
        }
    }

    private static void ToggleSplitPane()
    {
        lock (ContextsLock)
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
                EntryContext keep = SelectedContext;
                _contexts.RemoveAll(c => c != keep);
                _selectedContextIndex = 0;
            }

            Renderer.ClearScreen();
            UpdateContexts();
        }
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
        _keybinds.Add(new OpenWithKeybind { Context = context, Keys = new() { Key.Alt, Key.O } });
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
        _keybinds.Add(new ActionKeybind(FocusLeft) { Context = context, Keys = new() { Key.Ctrl, Key.ArrowLeft } });
        _keybinds.Add(new ActionKeybind(ToggleSidebar) { Context = context, Keys = new() { Key.F9 } });
        _keybinds.Add(new ActionKeybind(FocusSidebar) { Context = context, Keys = new() { Key.Ctrl, Key.S } });
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
        
        if (Sidebar.IsFocused)
        {
            HandleSidebarKey(e, heldKeys);
            return;
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

        if (bestMatch != null)
        {
            if (FolderUtils.IsNetworkView(bestMatch.Context.Cwd) && !bestMatch.WorksInNetworkView)
            {
                return;
            }

            bestMatch.OnKeyDown(e);
            return;
        }

        bool modified = heldKeys.Contains(Key.Ctrl) || heldKeys.Contains(Key.Alt) || heldKeys.Contains(Key.AltGr);
        if (!e.Continuous && !modified && e.Key.Char != '\0')
        {
            TypeAhead(SelectedContext, e.Key.Char);
        }
    }

    private static string _typeAheadBuffer = "";
    private static DateTime _typeAheadTime = DateTime.MinValue;

    private static void TypeAhead(EntryContext context, char character)
    {
        DateTime now = DateTime.UtcNow;
        bool continuing = now - _typeAheadTime < TimeSpan.FromSeconds(1);
        _typeAheadTime = now;

        string candidate = continuing ? _typeAheadBuffer + character : character.ToString();
        lock (context.Menu.Lock)
        {
            List<TuiListBoxItem<EntryListItem>> items = context.Menu.Items.ToList();
            int match = items.FindIndex(item => IsTypeAheadCandidate(item) && item.Value!.DisplayName.Text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

            if (match < 0 && continuing && _typeAheadBuffer.Length > 0 && char.ToLowerInvariant(_typeAheadBuffer[0]) == char.ToLowerInvariant(character) && _typeAheadBuffer.All(c => char.ToLowerInvariant(c) == char.ToLowerInvariant(character)))
            {
                candidate = character.ToString();
                for (int offset = 1; offset <= items.Count; offset++)
                {
                    int index = (context.Menu.SelectedIndex + offset) % items.Count;
                    if (IsTypeAheadCandidate(items[index]) && items[index].Value!.DisplayName.Text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        match = index;
                        break;
                    }
                }
            }

            _typeAheadBuffer = candidate;
            _typeAheadDisplay = match >= 0 ? $"Find: {candidate}" : $"Find: {candidate} (no match)";
            if (match < 0)
            {
                return;
            }

            context.Menu.SelectedIndex = match;
            if (match < context.Menu.ViewIndex || match >= context.Menu.ViewIndex + context.Menu.ViewRange)
            {
                context.Menu.ViewIndex = Math.Clamp(match - context.Menu.ViewRange / 2, 0, Math.Max(items.Count - context.Menu.ViewRange, 0));
            }
        }
    }

    private static bool IsTypeAheadCandidate(TuiListBoxItem<EntryListItem> item)
    {
        return item.Value != null && item.Value.DisplayName.Text != "..";
    }

    private static string _typeAheadDisplay = "";

    private static string GetStatusText()
    {
        if (DateTime.UtcNow - _typeAheadTime < TimeSpan.FromSeconds(1.5) && _typeAheadDisplay.Length > 0)
        {
            return _typeAheadDisplay;
        }

        return Sidebar.StatusText;
    }

    private static void HandleSidebarKey(KeyDownEventArgs e, List<Key> heldKeys)
    {
        bool ctrl = heldKeys.Contains(Key.Ctrl);
        if (e.Key == Key.Escape || (ctrl && (e.Key == Key.ArrowRight || e.Key == Key.S)))
        {
            Sidebar.IsFocused = false;
            return;
        }

        if (e.Key == Key.F9)
        {
            ToggleSidebar();
            return;
        }

        if (e.Key == Key.F3)
        {
            ToggleSplitPane();
            return;
        }

        if (Sidebar.HandleKey(e.Key, ctrl, heldKeys.Contains(Key.Shift), SelectedContext))
        {
            Sidebar.IsFocused = false;
        }
    }
}
        
