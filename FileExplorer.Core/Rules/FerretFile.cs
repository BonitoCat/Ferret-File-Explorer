namespace FileExplorer.Core.Rules;

public sealed class FerretFile
{
    public const string FileName = ".ferret";
    public const string ExcludeSection = "exclude";
    public const string IncludeSection = "include";
    public const string OnlySection = "only";
    public const string BringSection = "bring";

    private readonly Dictionary<string, List<string>> _sections;
    private readonly Dictionary<string, List<FerretPattern>> _patterns = new();

    public string Directory { get; }

    private FerretFile(string directory, Dictionary<string, List<string>> sections)
    {
        Directory = directory;
        _sections = sections;
    }

    public static FerretFile? Load(string directory)
    {
        string path = Path.Combine(directory, FileName);
        try
        {
            return File.Exists(path) ? Parse(directory, File.ReadLines(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static FerretFile Parse(string directory, IEnumerable<string> lines)
    {
        Dictionary<string, List<string>> sections = new(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                string name = line[1..^1].Trim();
                if (!sections.TryGetValue(name, out current))
                {
                    current = new();
                    sections[name] = current;
                }

                continue;
            }

            current?.Add(line);
        }

        return new(directory, sections);
    }

    public bool HasSection(string name)
    {
        return _sections.ContainsKey(name);
    }

    public IReadOnlyList<string> GetSection(string name)
    {
        return _sections.TryGetValue(name, out List<string>? lines) ? lines : [];
    }

    public IReadOnlyList<FerretPattern> GetPatterns(string name)
    {
        if (_patterns.TryGetValue(name, out List<FerretPattern>? cached))
        {
            return cached;
        }

        List<FerretPattern> patterns = GetSection(name)
            .Select(FerretPattern.Parse)
            .OfType<FerretPattern>()
            .ToList();
        _patterns[name] = patterns;
        return patterns;
    }
}
