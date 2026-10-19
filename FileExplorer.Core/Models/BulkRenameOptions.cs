namespace FileExplorer.Core.Models;

public sealed class BulkRenameOptions
{
    public BulkRenameMode Mode { get; set; }
    public BulkRenameScope Scope { get; set; }
    public string Find { get; set; } = "";
    public string Replacement { get; set; } = "";
    public bool CaseSensitive { get; set; }
    public bool UseRegex { get; set; }
    public string Text { get; set; } = "";
    public int Position { get; set; }
    public bool FromEnd { get; set; }
    public bool Overwrite { get; set; }
    public int Count { get; set; } = 1;
    public string Pattern { get; set; } = "{name} {n}";
    public int StartNumber { get; set; } = 1;
    public int Digits { get; set; } = 1;
    public bool StripNumbers { get; set; }
    public BulkRenameCase Case { get; set; }
}
