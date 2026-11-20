namespace FileExplorer.Core.Rules;

public sealed class FolderRuleSet
{
    private static readonly EnumerationOptions ScanOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly string _root;
    private readonly Dictionary<string, FerretFile?> _files = new(StringComparer.Ordinal);

    private FolderRuleSet(string root)
    {
        _root = root;
    }

    public static FolderRuleSet? Create(string root)
    {
        string normalized = Normalize(root);
        try
        {
            bool hasRules = File.Exists(Path.Combine(normalized, FerretFile.FileName))
                || Directory.EnumerateFiles(normalized, FerretFile.FileName, ScanOptions).Any();
            return hasRules ? new(normalized) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsExcluded(string path, bool isDirectory)
    {
        string full = Normalize(path);
        if (Path.GetFileName(full) == FerretFile.FileName || !IsInside(full, _root))
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(full);
        while (directory != null && (directory == _root || IsInside(directory, _root)))
        {
            FerretFile? file = GetFile(directory);
            bool? decision = file == null ? null : Decide(file, ToRelative(directory, full), isDirectory);
            if (decision != null)
            {
                return decision.Value;
            }

            if (directory == _root)
            {
                break;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return false;
    }

    private static bool? Decide(FerretFile file, string relativePath, bool isDirectory)
    {
        if (file.GetPatterns(FerretFile.IncludeSection).Any(pattern => pattern.MatchesSelfOrAncestor(relativePath, isDirectory)))
        {
            return false;
        }

        if (file.GetPatterns(FerretFile.ExcludeSection).Any(pattern => pattern.MatchesSelfOrAncestor(relativePath, isDirectory)))
        {
            return true;
        }

        IReadOnlyList<FerretPattern> only = file.GetPatterns(FerretFile.OnlySection);
        if (!file.HasSection(FerretFile.OnlySection))
        {
            return null;
        }

        if (only.Any(pattern => pattern.MatchesSelfOrAncestor(relativePath, isDirectory)))
        {
            return false;
        }

        return !(isDirectory && only.Any(pattern => pattern.CouldMatchInside(relativePath)));
    }

    private FerretFile? GetFile(string directory)
    {
        if (!_files.TryGetValue(directory, out FerretFile? file))
        {
            file = FerretFile.Load(directory);
            _files[directory] = file;
        }

        return file;
    }

    private static string ToRelative(string directory, string path)
    {
        return Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static bool IsInside(string path, string container)
    {
        return path.StartsWith(container.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        return full.Length > 1 ? full.TrimEnd(Path.DirectorySeparatorChar) : full;
    }
}
