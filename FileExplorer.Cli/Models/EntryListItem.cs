using FileExplorer.Core.Interfaces;

namespace FileExplorer.Cli.Models;

public class EntryListItem
{
    public required IFileSystemEntry Entry { get; init; }
    public required ColoredText DisplayName { get; init; }
    public required ColoredText Icon { get; init; }
    public List<ColoredText> Prefixes { get; set; } = [];
    public List<ColoredText> Suffixes { get; set; } = [];
    public int Length => DisplayName.Length + Icon.Length + Prefixes.Sum(prefix => prefix.Length) + Suffixes.Sum(suffix => suffix.Length);
    public Action OnClick { get; set; } = () => { };
    
    public override string ToString()
    {
        string prefixString = "";
        if (Prefixes.Count > 0)
        {
            prefixString = string.Concat(Prefixes.Select(prefix => prefix.ToString()));
        }
        
        string suffixString = "";
        if (Suffixes.Count > 0)
        {
            suffixString = string.Concat(Suffixes.Select(suffix => suffix.ToString()));
        }
        
        return $"{Icon}{prefixString}{DisplayName}{suffixString}\x1b[0m";
    }
}