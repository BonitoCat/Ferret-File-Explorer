using Tmds.DBus.Protocol;

namespace FileExplorer.Portal;

public static class PortalFilters
{
    private const uint GlobFilterType = 0;
    private const uint MimeFilterType = 1;

    private static readonly Lazy<Dictionary<string, List<string>>> _mimeGlobs = new(LoadMimeGlobs);

    public static List<string> ToArguments(Dictionary<string, VariantValue> options)
    {
        List<(string Name, List<string> Patterns)> filters = new();
        if (options.TryGetValue("filters", out VariantValue filterList) && filterList.Type == VariantValueType.Array)
        {
            for (int i = 0; i < filterList.Count; i++)
            {
                (string Name, List<string> Patterns)? filter = ReadFilter(filterList.GetItem(i));
                if (filter != null)
                {
                    filters.Add(filter.Value);
                }
            }
        }

        if (options.TryGetValue("current_filter", out VariantValue current) && ReadFilter(current) is { } currentFilter)
        {
            filters.RemoveAll(f => f.Name == currentFilter.Name);
            filters.Insert(0, currentFilter);
        }

        List<string> arguments = new();
        foreach ((string name, List<string> patterns) in filters)
        {
            arguments.Add("--filter");
            arguments.Add($"{name.Replace("|", "/")}|{string.Join(" ", patterns)}");
        }

        return arguments;
    }

    private static (string Name, List<string> Patterns)? ReadFilter(VariantValue filter)
    {
        if (filter.Type != VariantValueType.Struct || filter.Count < 2)
        {
            return null;
        }

        string name = filter.GetItem(0).GetString();
        VariantValue rules = filter.GetItem(1);
        List<string> patterns = new();
        for (int i = 0; i < rules.Count; i++)
        {
            VariantValue rule = rules.GetItem(i);
            uint type = rule.GetItem(0).GetUInt32();
            string value = rule.GetItem(1).GetString();
            if (type == GlobFilterType)
            {
                patterns.Add(value);
            }
            else if (type == MimeFilterType)
            {
                patterns.AddRange(GetGlobsForMime(value));
            }
        }

        List<string> distinct = patterns.Where(p => !p.Contains(' ')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 0 ? null : (name, distinct);
    }

    private static IEnumerable<string> GetGlobsForMime(string mime)
    {
        if (mime == "*/*" || mime == "application/octet-stream")
        {
            return ["*"];
        }

        if (mime.EndsWith("/*", StringComparison.Ordinal))
        {
            string prefix = mime[..^1];
            return _mimeGlobs.Value
                .Where(entry => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .SelectMany(entry => entry.Value);
        }

        return _mimeGlobs.Value.TryGetValue(mime, out List<string>? globs) ? globs : [];
    }

    private static Dictionary<string, List<string>> LoadMimeGlobs()
    {
        Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string dataDir in GetDataDirs())
        {
            string globsPath = Path.Combine(dataDir, "mime", "globs2");
            if (!File.Exists(globsPath))
            {
                continue;
            }

            try
            {
                foreach (string line in File.ReadLines(globsPath))
                {
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    string[] parts = line.Split(':');
                    if (parts.Length < 3)
                    {
                        continue;
                    }

                    if (!result.TryGetValue(parts[1], out List<string>? globs))
                    {
                        globs = new();
                        result[parts[1]] = globs;
                    }

                    if (!globs.Contains(parts[2]))
                    {
                        globs.Add(parts[2]);
                    }
                }
            }
            catch (IOException)
            {
            }
        }

        return result;
    }

    private static IEnumerable<string> GetDataDirs()
    {
        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        yield return string.IsNullOrEmpty(dataHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : dataHome;

        string? dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        foreach (string dir in (string.IsNullOrEmpty(dataDirs) ? "/usr/local/share:/usr/share" : dataDirs).Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return dir;
        }
    }
}
