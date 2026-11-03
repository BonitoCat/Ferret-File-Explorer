using Avalonia.Layout;
using Avalonia.Media;

namespace FileExplorer.Gui.ViewModels;

public enum DecorationCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public sealed record FileItemDecoration
{
    public IBrush? Foreground { get; init; }
    public IBrush? Background { get; init; }
    public IBrush? IconBackground { get; init; }
    public IBrush? NameBackground { get; init; }
    public IImage? Icon { get; init; }
    public IImage? BadgeIcon { get; init; }
    public string? BadgeText { get; init; }
    public IBrush? BadgeBackground { get; init; }
    public DecorationCorner BadgeCorner { get; init; } = DecorationCorner.TopRight;
    public string? Tooltip { get; init; }

    public bool HasBadge => BadgeIcon != null || BadgeText != null || BadgeBackground != null;

    public HorizontalAlignment BadgeHorizontalAlignment => BadgeCorner is DecorationCorner.TopLeft or DecorationCorner.BottomLeft
        ? HorizontalAlignment.Left
        : HorizontalAlignment.Right;

    public VerticalAlignment BadgeVerticalAlignment => BadgeCorner is DecorationCorner.TopLeft or DecorationCorner.TopRight
        ? VerticalAlignment.Top
        : VerticalAlignment.Bottom;
}
