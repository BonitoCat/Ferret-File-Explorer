namespace FileExplorer.Core.Models;

public enum ConflictAction
{
    Cancel,
    Skip,
    Duplicate,
    Replace,
    Merge
}

public record TransferConflict(
    string SourcePath,
    string DestinationPath,
    bool SourceIsDirectory,
    bool DestinationIsDirectory,
    bool IsCopy,
    bool HasMultipleItems,
    bool AllowMerge = true)
{
    public bool CanMerge => AllowMerge && SourceIsDirectory && DestinationIsDirectory;
}

public record ConflictResolution(ConflictAction Action, bool ApplyToAll = false);
