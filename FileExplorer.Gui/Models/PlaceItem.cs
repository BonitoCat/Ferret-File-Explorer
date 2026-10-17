using Avalonia.Media;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui.Models;

public enum PlaceCategory
{
    Places,
    Bookmarks,
    Devices,
    Network,
}

public class PlaceItem : ViewModelBase
{
    private bool _showDropLineAbove;
    private bool _showDropLineBelow;
    private bool _isDropTarget;
    private bool _isVisible = true;
    private bool _isCollapsed;
    private string _path = "";
    private bool _isMounted = true;

    public required string Name { get; set; }

    public required string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
            {
                OnPropertyChanged(nameof(CanDismount));
            }
        }
    }

    public IImage? Icon { get; set; }
    public string IconSvg { get; set; } = "";
    public bool IsBookmark { get; init; }
    public bool IsHeader { get; init; }
    public bool IsNetworkBookmark { get; set; }

    public bool IsMounted
    {
        get => _isMounted;
        set
        {
            if (SetProperty(ref _isMounted, value))
            {
                OnPropertyChanged(nameof(CanDismount));
            }
        }
    }

    public string? RemoteUri { get; set; }
    public string? DevicePath { get; set; }
    public PlaceCategory Category { get; init; } = PlaceCategory.Places;
    public bool CanDismount => !IsHeader && IsMounted && (Category == PlaceCategory.Devices || Category == PlaceCategory.Network) && !string.IsNullOrEmpty(Path) && Path != "/" && !Path.StartsWith("sftp://") && !Path.StartsWith("smb://") && !Path.StartsWith("ftp://") && !Path.StartsWith("dav://") && !Path.StartsWith("davs://") && !Path.StartsWith("network:") && !Path.StartsWith("/dev/");
    public bool IsNetworkHeader => IsHeader && Category == PlaceCategory.Network;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetProperty(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(Chevron));
            }
        }
    }

    public string Chevron => IsCollapsed ? "▶" : "▼";

    public bool ShowDropLineAbove
    {
        get => _showDropLineAbove;
        set => SetProperty(ref _showDropLineAbove, value);
    }

    public bool ShowDropLineBelow
    {
        get => _showDropLineBelow;
        set => SetProperty(ref _showDropLineBelow, value);
    }

    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => SetProperty(ref _isDropTarget, value);
    }
}
