using System.Collections.Generic;
using FileExplorer.Core.Interfaces;

namespace FileExplorer.Core.Utilities;

public static class EntryComparers
{
    private static readonly NameComparer _nameComparer = new();

    public static IComparer<IFileSystemEntry> GetComparer(SortingMode mode)
    {
        return Comparer<IFileSystemEntry>.Create((a, b) =>
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;

            return _nameComparer.Compare(a.FullName, b.FullName);
        });
    }
}