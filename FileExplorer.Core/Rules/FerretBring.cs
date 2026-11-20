using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Rules;

public sealed record BringItem(string Source, string Target, bool NeedsElevation);

public static class FerretBring
{
    private static readonly EnumerationOptions ReadCheckOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static List<BringItem> Plan(IEnumerable<string> sources, string destinationDirectory)
    {
        List<BringItem> items = new();
        foreach (string source in sources.Where(Directory.Exists))
        {
            string sourceFolder = Normalize(source);
            string targetFolder = Path.Combine(Normalize(destinationDirectory), Path.GetFileName(sourceFolder));
            items.AddRange(Plan(sourceFolder, targetFolder));
        }

        return items
            .GroupBy(item => item.Source, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }

    public static List<BringItem> Plan(string sourceFolder, string targetFolder)
    {
        FerretFile? file = FerretFile.Load(sourceFolder);
        if (file == null)
        {
            return new();
        }

        List<BringItem> items = new();
        foreach (string entry in file.GetSection(FerretFile.BringSection))
        {
            string relative = entry.Trim().TrimEnd('/', '\\');
            if (relative.Length == 0 || Path.IsPathRooted(relative))
            {
                continue;
            }

            string source = Normalize(Path.Combine(sourceFolder, relative));
            string target = Normalize(Path.Combine(targetFolder, relative));
            bool exists = File.Exists(source) || Directory.Exists(source);
            bool overlaps = source == sourceFolder || IsInside(source, sourceFolder) || IsInside(sourceFolder, source);
            bool invalidTarget = target == source || IsInside(target, source) || Path.GetFileName(source) != Path.GetFileName(target);
            if (!exists || overlaps || invalidTarget)
            {
                continue;
            }

            items.Add(new(source, target, NeedsElevation(source, target)));
        }

        return items;
    }

    private static bool NeedsElevation(string source, string target)
    {
        if (!ElevatedFileService.IsAvailable)
        {
            return false;
        }

        string? parent = Path.GetDirectoryName(target);
        while (parent != null && !Directory.Exists(parent))
        {
            parent = Path.GetDirectoryName(parent);
        }

        return ElevatedFileService.NeedsElevation(parent) || !CanReadAll(source);
    }

    private static bool CanReadAll(string path)
    {
        if (!Directory.Exists(path))
        {
            return FileUtils.CanRead(path, false);
        }

        try
        {
            return FileUtils.CanRead(path, true)
                && new DirectoryInfo(path).EnumerateFileSystemInfos("*", ReadCheckOptions).All(info => FileUtils.CanRead(info.FullName, info is DirectoryInfo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
