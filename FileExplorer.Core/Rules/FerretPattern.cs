using System.Text;
using System.Text.RegularExpressions;

namespace FileExplorer.Core.Rules;

public sealed class FerretPattern
{
    private readonly string[] _segments;
    private readonly Regex[] _segmentRegexes;
    private readonly Regex _regex;

    public bool DirectoryOnly { get; }

    private FerretPattern(string[] segments, bool directoryOnly)
    {
        _segments = segments;
        DirectoryOnly = directoryOnly;
        _segmentRegexes = segments.Select(segment => new Regex("^" + SegmentToRegex(segment) + "$", RegexOptions.CultureInvariant)).ToArray();
        _regex = new(BuildRegex(segments), RegexOptions.CultureInvariant);
    }

    public static FerretPattern? Parse(string text)
    {
        string pattern = text.Trim().Replace('\\', '/');
        bool directoryOnly = pattern.EndsWith('/');
        pattern = pattern.Trim('/');
        if (pattern.Length == 0)
        {
            return null;
        }

        bool anchored = pattern.Contains('/') || text.TrimStart().StartsWith('/');
        if (!anchored)
        {
            pattern = "**/" + pattern;
        }

        string[] segments = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? null : new(segments, directoryOnly);
    }

    public bool Matches(string relativePath, bool isDirectory)
    {
        if (DirectoryOnly && !isDirectory)
        {
            return false;
        }

        return _regex.IsMatch(relativePath);
    }

    public bool MatchesSelfOrAncestor(string relativePath, bool isDirectory)
    {
        if (Matches(relativePath, isDirectory))
        {
            return true;
        }

        for (int i = relativePath.IndexOf('/'); i >= 0; i = relativePath.IndexOf('/', i + 1))
        {
            if (Matches(relativePath[..i], true))
            {
                return true;
            }
        }

        return false;
    }

    public bool CouldMatchInside(string relativeDirectory)
    {
        string[] parts = relativeDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i >= _segments.Length)
            {
                return false;
            }

            if (_segments[i] == "**")
            {
                return true;
            }

            if (!_segmentRegexes[i].IsMatch(parts[i]))
            {
                return false;
            }
        }

        return parts.Length < _segments.Length;
    }

    private static string BuildRegex(string[] segments)
    {
        StringBuilder builder = new("^");
        for (int i = 0; i < segments.Length; i++)
        {
            bool isLast = i == segments.Length - 1;
            if (segments[i] == "**")
            {
                builder.Append(isLast ? ".*" : "(?:[^/]+/)*");
                continue;
            }

            builder.Append(SegmentToRegex(segments[i]));
            if (!isLast)
            {
                builder.Append('/');
            }
        }

        return builder.Append('$').ToString();
    }

    private static string SegmentToRegex(string segment)
    {
        if (segment == "**")
        {
            return ".*";
        }

        StringBuilder builder = new();
        foreach (char c in segment)
        {
            builder.Append(c switch
            {
                '*' => "[^/]*",
                '?' => "[^/]",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return builder.ToString();
    }
}
