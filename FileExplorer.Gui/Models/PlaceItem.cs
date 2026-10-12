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

    public required string Name { get; set; }
    public required string Path { get; set; }
    public IImage? Icon { get; set; }
    public string IconSvg { get; set; } = "";
    public bool IsBookmark { get; init; }
    public bool IsHeader { get; init; }
    public bool IsNetworkBookmark { get; set; }
    public bool IsMounted { get; set; } = true;
    public string? RemoteUri { get; set; }
    public string? DevicePath { get; set; }
    public PlaceCategory Category { get; init; } = PlaceCategory.Places;
    public bool CanDismount => !IsHeader && IsMounted && (Category == PlaceCategory.Devices || Category == PlaceCategory.Network) && !string.IsNullOrEmpty(Path) && Path != "/" && !Path.StartsWith("sftp://") && !Path.StartsWith("smb://") && !Path.StartsWith("ftp://") && !Path.StartsWith("dav://") && !Path.StartsWith("davs://") && !Path.StartsWith("/dev/");
    public bool IsNetworkHeader => IsHeader && Category == PlaceCategory.Network;

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
