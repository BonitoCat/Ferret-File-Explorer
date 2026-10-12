using System;

namespace FileExplorer.Core.Models;

public class FileTransferProgress
{
    public int TransferredItems { get; set; }
    public int TotalItems { get; set; }
    public long TransferredBytes { get; set; }
    public long TotalBytes { get; set; }
    public double BytesPerSecond { get; set; }
    public TimeSpan? EstimatedRemaining { get; set; }
    public string CurrentItemName { get; set; } = string.Empty;
    public bool IsCopy { get; set; }
    public double Percent => TotalBytes > 0
        ? Math.Clamp((double) TransferredBytes / TotalBytes * 100.0, 0.0, 100.0)
        : (TotalItems > 0 ? Math.Clamp((double) TransferredItems / TotalItems * 100.0, 0.0, 100.0) : 0.0);
}
