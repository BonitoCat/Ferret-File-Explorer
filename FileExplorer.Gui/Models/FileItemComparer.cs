using System;
using System.Collections.Generic;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui.Models;

public sealed class FileItemComparer : IComparer<FileItemViewModel>
{
    private static readonly NameComparer Names = new();

    public static readonly FileItemComparer Default = new(SortColumn.Name, false);

    public FileItemComparer(SortColumn column, bool descending)
    {
        Column = column;
        Descending = descending;
    }

    public SortColumn Column { get; }
    public bool Descending { get; }
    public bool IsDefault => Column == SortColumn.Name && !Descending;

    public int Compare(FileItemViewModel? x, FileItemViewModel? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        if (x.IsDirectory != y.IsDirectory) return x.IsDirectory ? -1 : 1;

        int result = CompareColumn(x, y);
        if (result != 0)
        {
            return Descending ? -result : result;
        }

        return Names.Compare(x.Name, y.Name);
    }

    private int CompareColumn(FileItemViewModel x, FileItemViewModel y)
    {
        return Column switch
        {
            SortColumn.Size => x.FileSize.CompareTo(y.FileSize),
            SortColumn.Type => string.Compare(x.TypeString, y.TypeString, StringComparison.CurrentCultureIgnoreCase),
            SortColumn.Modified => x.ModifiedTime.CompareTo(y.ModifiedTime),
            _ => Names.Compare(x.Name, y.Name),
        };
    }
}
