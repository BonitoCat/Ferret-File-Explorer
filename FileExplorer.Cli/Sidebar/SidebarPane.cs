using FileExplorer.Cli.Context;
using FileExplorer.Cli.Keybinds;
using FileExplorer.Cli.Utilities;
using FileExplorer.Core.Services;

using InputLib;

using TuiLib;
using TuiLib.Components;
using TuiLib.Controls;

namespace FileExplorer.Cli.Sidebar;

public sealed class SidebarEntry
{
    public string? Header { get; init; }
    public PlaceInfo? Place { get; init; }
    public bool IsConnectAction { get; init; }
    public bool IsSpacer { get; init; }
    public bool IsHeader => Header != null || IsSpacer;

    public string Key => IsSpacer ? $"_{Header}" : Header != null ? $"#{Header}" : IsConnectAction ? "+connect" : $"{Place?.Kind}:{Place?.Path}";
}

public sealed class SidebarPane
{
    private static readonly Dictionary<PlaceKind, string> SectionTitles = new()
    {
        [PlaceKind.Places] = "PLACES",
        [PlaceKind.Bookmarks] = "BOOKMARKS",
        [PlaceKind.Devices] = "DEVICES",
        [PlaceKind.Network] = "NETWORK",
    };

    private static readonly Color HeaderColor = new(130, 130, 130);
    private static readonly Color DimColor = new(110, 110, 110);
    private static readonly Color CurrentColor = new(120, 180, 255);

    private readonly PlacesService _placesService = new();
    private readonly BookmarkService _bookmarkService = new();
    private readonly NetworkBookmarkService _networkBookmarkService = new();
    private readonly NetworkService _networkService = new();
    private readonly SidebarOrderService _order = new();
    private List<PlaceInfo> _places = new();
    private int _reloadVersion;

    public TuiListBox<SidebarEntry> Menu { get; }
    public bool IsVisible { get; set; } = CliSettings.SidebarVisible;

    public bool IsFocused { get; set; }
    public string StatusText { get; set; } = "";
    public int Width { get; private set; }
    public string? CurrentPath { get; set; }
    public string? TargetLabel { get; set; }

    public bool ShouldShow => IsVisible && Console.WindowWidth >= 60;

    public void SaveVisibility()
    {
        CliSettings.SidebarVisible = IsVisible;
    }

    public SidebarPane()
    {
        Menu = new()
        {
            UseDefaultMarker = false,
            ScrollOvershoot = 2,
        };
        Menu.ItemRenderer = RenderEntry;
    }

    public void Layout(int width, int height)
    {
        Width = width;
        Menu.X = 0;
        Menu.Y = 1;
        Menu.MaxWidth = Math.Max(width - 1, 0);
        Menu.MaxHeight = Math.Max(height, 0);
        Menu.Height = Menu.MaxHeight;
        Menu.ViewRange = Menu.MaxHeight;
    }

    public void Reload()
    {
        int version = Interlocked.Increment(ref _reloadVersion);
        Task.Run(() =>
        {
            List<PlaceInfo> places;
            try
            {
                _order.Reload();
                places = _placesService.GetAll();
            }
            catch
            {
                return;
            }

            if (version != _reloadVersion)
            {
                return;
            }

            _places = places;
            ApplyEntries(BuildEntries(places));
        });
    }

    private List<SidebarEntry> BuildEntries(List<PlaceInfo> places)
    {
        List<SidebarEntry> entries = new();
        foreach (PlaceKind kind in _order.GetSectionOrder())
        {
            string title = SectionTitles[kind];
            List<PlaceInfo> section = _order.Sort(kind, places.Where(p => p.Kind == kind), SidebarOrderService.GetKey);
            if (section.Count == 0 && kind != PlaceKind.Network)
            {
                continue;
            }

            if (entries.Count > 0)
            {
                entries.Add(new() { IsSpacer = true, Header = title });
            }

            entries.Add(new() { Header = title });
            entries.AddRange(section.Select(p => new SidebarEntry { Place = p }));
            if (kind == PlaceKind.Network)
            {
                entries.Add(new() { IsConnectAction = true });
            }
        }

        return entries;
    }

    private void ApplyEntries(List<SidebarEntry> entries)
    {
        lock (Menu.Lock)
        {
            string? selectedKey = Menu.SelectedItem?.Value?.Key;
            Menu.ClearItems();
            Menu.AddItemRange(entries);

            int index = entries.FindIndex(e => e.Key == selectedKey);
            Menu.SelectedIndex = index >= 0 ? index : entries.FindIndex(e => !e.IsHeader);
            EnsureVisible(Menu.SelectedIndex);
        }
    }

    public void MoveSelection(int amount)
    {
        lock (Menu.Lock)
        {
            int count = Menu.Items.Count;
            if (count == 0)
            {
                return;
            }

            int direction = Math.Sign(amount);
            int index = Math.Clamp(Menu.SelectedIndex + amount, 0, count - 1);
            while (index >= 0 && index < count && Menu.Items[index].Value!.IsHeader)
            {
                index += direction == 0 ? 1 : direction;
            }

            if (index < 0 || index >= count)
            {
                index = Menu.SelectedIndex;
            }

            Menu.SelectedIndex = index;
            EnsureVisible(index);
        }
    }

    public void SelectEdge(bool first)
    {
        lock (Menu.Lock)
        {
            Menu.SelectedIndex = first ? 0 : Menu.Items.Count - 1;
            MoveSelection(first ? 1 : -1);
            MoveSelection(first ? -1 : 1);
        }
    }

    private void EnsureVisible(int index)
    {
        if (index < 0)
        {
            return;
        }

        int overshoot = Menu.ScrollOvershoot;
        if (index - overshoot < Menu.ViewIndex)
        {
            Menu.ViewIndex = Math.Max(index - overshoot, 0);
            return;
        }

        if (index + overshoot + 1 > Menu.ViewIndex + Menu.ViewRange)
        {
            Menu.ViewIndex = Math.Max(Math.Min(index - Menu.ViewRange + 1 + overshoot, Menu.Items.Count - Menu.ViewRange), 0);
        }
    }

    public bool HandleKey(Key key, bool ctrl, EntryContext context)
    {
        return HandleKey(key, ctrl, false, context);
    }

    public bool HandleKey(Key key, bool ctrl, bool shift, EntryContext context)
    {
        if (shift && (key == Key.ArrowUp || key == Key.ArrowDown))
        {
            int direction = key == Key.ArrowUp ? -1 : 1;
            if (ctrl)
            {
                MoveSelectedSection(direction);
            }
            else
            {
                MoveSelectedPlace(direction);
            }

            return false;
        }

        if (key == Key.ArrowUp)
        {
            MoveSelection(-1);
            return false;
        }

        if (key == Key.ArrowDown)
        {
            MoveSelection(1);
            return false;
        }

        if (key == Key.PageUp || key == Key.PageDown)
        {
            int page = Math.Max(Menu.ViewRange - 1, 1);
            MoveSelection(key == Key.PageUp ? -page : page);
            return false;
        }

        if (key == Key.Home || key == Key.End)
        {
            SelectEdge(key == Key.Home);
            return false;
        }

        if (key == Key.Enter || (key == Key.ArrowRight && !ctrl))
        {
            return Activate(context);
        }

        if (key == Key.Delete)
        {
            RemoveSelected(context);
            return false;
        }

        if (key == Key.F7)
        {
            ConnectServerKeybind.Connect(context);
            return true;
        }

        if (key == Key.F8)
        {
            DismountSelected(context);
            return false;
        }

        if (key == Key.F5 || (key == Key.R && ctrl))
        {
            Reload();
        }

        return false;
    }

    public void MoveSelectedPlace(int direction)
    {
        PlaceInfo? place = Menu.SelectedItem?.Value?.Place;
        if (place == null)
        {
            return;
        }

        List<PlaceInfo> section = _order.Sort(place.Kind, _places.Where(p => p.Kind == place.Kind), SidebarOrderService.GetKey);
        int index = section.IndexOf(place);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= section.Count)
        {
            return;
        }

        (section[index], section[target]) = (section[target], section[index]);
        _order.SetItemOrder(place.Kind, section.Select(SidebarOrderService.GetKey));
        ApplyEntries(BuildEntries(_places));
    }

    public void MoveSelectedSection(int direction)
    {
        SidebarEntry? entry = Menu.SelectedItem?.Value;
        PlaceKind? kind = entry?.IsConnectAction == true ? PlaceKind.Network : entry?.Place?.Kind;
        if (kind == null)
        {
            return;
        }

        List<PlaceKind> visible = _order.GetSectionOrder()
            .Where(k => k == PlaceKind.Network || _places.Any(p => p.Kind == k))
            .ToList();
        int index = visible.IndexOf(kind.Value);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= visible.Count)
        {
            return;
        }

        List<PlaceKind> order = _order.GetSectionOrder();
        int from = order.IndexOf(kind.Value);
        int to = order.IndexOf(visible[target]);
        (order[from], order[to]) = (order[to], order[from]);
        _order.SetSectionOrder(order);
        ApplyEntries(BuildEntries(_places));
    }

    public bool Activate(EntryContext context)
    {
        SidebarEntry? entry = Menu.SelectedItem?.Value;
        if (entry == null || entry.IsHeader)
        {
            return false;
        }

        if (entry.IsConnectAction)
        {
            ConnectServerKeybind.Connect(context);
            return true;
        }

        PlaceInfo place = entry.Place!;
        if (place.Kind == PlaceKind.Devices && !place.IsMounted)
        {
            MountDevice(place, context);
            return true;
        }

        if (place.Kind == PlaceKind.Network && !place.IsMounted)
        {
            ConnectBookmark(place, context);
            return true;
        }

        return NavigateTo(context, place.Path);
    }

    private bool NavigateTo(EntryContext context, string path)
    {
        if (!Directory.Exists(path))
        {
            StatusText = $"Not available: {path}";
            return false;
        }

        StatusText = "";
        context.Navigation.NavigateTo(path);
        FolderUtils.LoadFolderContent(context, path);
        Directory.SetCurrentDirectory(path);

        return true;
    }

    private void MountDevice(PlaceInfo place, EntryContext context)
    {
        string? device = place.DevicePath ?? place.Path;
        StatusText = $"Mounting {place.Name}…";

        Task.Run(async () =>
        {
            string? mountPoint = await PlacesService.MountDeviceAsync(device);
            if (mountPoint == null)
            {
                StatusText = $"Could not mount {place.Name}";
                return;
            }

            NavigateTo(context, mountPoint);
            Reload();
        });
    }

    private void ConnectBookmark(PlaceInfo place, EntryContext context)
    {
        string uri = place.RemoteUri ?? place.Path;
        string? password = _networkBookmarkService.GetPassword(uri);
        if (string.IsNullOrEmpty(password) && !NetworkService.ParseUri(uri).Anonymous)
        {
            password = context.Input($"Password for {place.Name}: ", "", true);
            if (password == null)
            {
                return;
            }

            _networkBookmarkService.SetPassword(uri, password);
        }

        StatusText = $"Connecting to {place.Name}…";
        Task.Run(async () =>
        {
            (bool success, string mountPath, string error) = await _networkService.MountUriAsync(uri, password);
            if (!success || !Directory.Exists(mountPath))
            {
                StatusText = string.IsNullOrWhiteSpace(error) ? $"Could not connect to {place.Name}" : $"Connection failed: {error}";
                return;
            }

            NavigateTo(context, mountPath);
            Reload();
        });
    }

    private void RemoveSelected(EntryContext context)
    {
        PlaceInfo? place = Menu.SelectedItem?.Value?.Place;
        if (place == null || (!place.IsBookmark && !place.IsNetworkBookmark))
        {
            return;
        }

        string? answer = context.Input($"Remove “{place.Name}” from the sidebar? (y/N): ");
        if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (place.IsNetworkBookmark && place.RemoteUri != null)
        {
            _networkBookmarkService.RemoveBookmark(place.RemoteUri);
        }
        else
        {
            _bookmarkService.RemoveBookmark(place.Path);
        }

        Reload();
    }

    private void DismountSelected(EntryContext context)
    {
        PlaceInfo? place = Menu.SelectedItem?.Value?.Place;
        if (place == null || !place.IsMounted || place.Kind is PlaceKind.Places or PlaceKind.Bookmarks)
        {
            return;
        }

        StatusText = $"Unmounting {place.Name}…";
        Task.Run(async () =>
        {
            bool success;
            string error = "";
            if (place.Kind == PlaceKind.Devices && place.DevicePath != null && place.DevicePath.StartsWith("/dev/"))
            {
                success = await PlacesService.UnmountDeviceAsync(place.DevicePath);
            }
            else
            {
                (success, error) = await _networkService.UnmountAsync(place.Path);
            }

            if (!success)
            {
                StatusText = string.IsNullOrWhiteSpace(error) ? $"Could not unmount {place.Name}" : $"Unmount failed: {error}";
                return;
            }

            StatusText = $"Unmounted {place.Name}";
            if (context.Cwd.StartsWith(place.Path, StringComparison.Ordinal))
            {
                NavigateTo(context, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            }

            Reload();
        });
    }

    private IEnumerable<RenderPart> RenderEntry(SidebarEntry entry)
    {
        int contentWidth = Math.Max(Menu.MaxWidth - 1, 0);
        if (entry.IsSpacer)
        {
            yield return new RenderPart
            {
                Text = new string(' ', contentWidth),
            };
            yield break;
        }

        if (entry.IsHeader)
        {
            yield return new RenderPart
            {
                Text = Fit($" {entry.Header}", contentWidth),
                Foreground = HeaderColor,
            };
            yield break;
        }

        int index = Menu.IndexOf(entry);
        bool isCursor = IsFocused && index == Menu.SelectedIndex;
        PlaceInfo? place = entry.Place;
        bool isCurrent = place != null && place.IsMounted && CurrentPath != null && string.Equals(place.Path.TrimEnd('/'), CurrentPath.TrimEnd('/'), StringComparison.Ordinal);

        string icon = entry.IsConnectAction ? "+" : GetIcon(place!);
        string name = entry.IsConnectAction ? "Connect to server…" : place!.Name;
        string text = Fit($" {(isCursor ? "›" : " ")}{icon} {name}", contentWidth);

        Color foreground = isCursor ? Color.Black
            : entry.IsConnectAction || place is { IsMounted: false } ? DimColor
            : isCurrent ? CurrentColor
            : IsFocused ? Color.White : Color.LightGray;

        yield return new RenderPart
        {
            Text = text,
            Foreground = foreground,
            Background = isCursor ? Color.White : Color.Reset,
        };
    }

    private static string GetIcon(PlaceInfo place)
    {
        if (place.Kind == PlaceKind.Places)
        {
            return place.Path switch
            {
                "/" => "/",
                _ when place.Path.EndsWith("/Trash/files", StringComparison.Ordinal) => "×",
                _ when place.Path == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) => "⌂",
                _ => "▪",
            };
        }

        return place.Kind switch
        {
            PlaceKind.Bookmarks => "★",
            PlaceKind.Devices => place.IsMounted ? "▣" : "□",
            PlaceKind.Network => place.IsMounted ? "⇄" : "↔",
            _ => "▪",
        };
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        if (text.Length > width)
        {
            return text[..Math.Max(width - 1, 0)] + "…";
        }

        return text.PadRight(width);
    }

    public IEnumerable<RenderPart> Render(bool privileged)
    {
        foreach (RenderPart part in Menu.Render())
        {
            yield return part;
        }

        int contentWidth = Math.Max(Width - 1, 0);
        yield return new RenderPart
        {
            X = 0,
            Y = 0,
            Text = Fit(IsFocused && TargetLabel != null ? $" Sidebar → {TargetLabel}" : " Sidebar (F9)", contentWidth),
            Foreground = IsFocused ? Color.White : Color.Gray,
            Background = privileged ? Color.Red.Transform(-50, 40, 50) : Color.Reset,
            ZIndex = -2,
        };

        int itemCount = Menu.GetViewItems().Count;
        for (int row = itemCount; row < Menu.MaxHeight; row++)
        {
            yield return new RenderPart
            {
                X = 0,
                Y = Menu.Y + row,
                Text = new string(' ', contentWidth),
                ZIndex = -1,
            };
        }

        for (int row = 0; row < Menu.MaxHeight; row++)
        {
            yield return new RenderPart
            {
                X = Math.Max(Width - 2, 0),
                Y = Menu.Y + row,
                Text = " ",
                ZIndex = -1,
            };
        }

        for (int row = 0; row <= Menu.MaxHeight; row++)
        {
            yield return new RenderPart
            {
                X = Width - 1,
                Y = row,
                Text = "│",
                Foreground = IsFocused ? Color.Gray : new Color(70, 70, 70),
            };
        }
    }
}
