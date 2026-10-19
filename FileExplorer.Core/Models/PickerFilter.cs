namespace FileExplorer.Core.Models;

public sealed record PickerFilter(string Name, IReadOnlyList<string> Patterns)
{
    public static PickerFilter? Parse(string value)
    {
        int separator = value.IndexOf('|');
        string name = separator >= 0 ? value[..separator].Trim() : "";
        string patternText = separator >= 0 ? value[(separator + 1)..] : value;
        List<string> patterns = patternText
            .Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (patterns.Count == 0)
        {
            return null;
        }

        return new(name.Length > 0 ? name : string.Join(" ", patterns), patterns);
    }
}
