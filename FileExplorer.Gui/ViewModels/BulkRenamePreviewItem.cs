namespace FileExplorer.Gui.ViewModels;

public sealed class BulkRenamePreviewItem
{
    public required string Path { get; init; }
    public required string OriginalName { get; init; }
    public required string NewName { get; init; }
    public bool HasConflict { get; init; }

    public bool IsChanged => NewName != OriginalName;
}
