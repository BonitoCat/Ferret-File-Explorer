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
    bool HasMultipleItems)
{
    public bool CanMerge => SourceIsDirectory && DestinationIsDirectory;
}

public record ConflictResolution(ConflictAction Action, bool ApplyToAll = false);
