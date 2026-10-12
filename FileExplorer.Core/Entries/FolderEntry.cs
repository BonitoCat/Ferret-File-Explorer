using FileExplorer.Core.Interfaces;

namespace FileExplorer.Core.Entries;

public class FolderEntry : IFileSystemEntry
{
    public required string FullName { get; set; }
    public required string FullPath { get; set; }

    public bool IsHidden { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsCompressed { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsSystem { get; set; }
    
    public DateTime CreationTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public UnixFileMode? UnixMode { get; set; }
}
