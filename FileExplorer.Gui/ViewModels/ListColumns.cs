using System;
using System.Collections.Generic;
using System.Linq;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.ViewModels;

public sealed class ListColumn : ViewModelBase
{
    private bool _isVisible;
    private double _width;

    public string Id { get; }
    public string TitleKey { get; }
    public double MinWidth { get; }
    public bool NeedsMetadata { get; }

    public ListColumn(string id, string titleKey, double width, double minWidth, bool isVisible, bool needsMetadata = false)
    {
        Id = id;
        TitleKey = titleKey;
        MinWidth = minWidth;
        NeedsMetadata = needsMetadata;
        _width = width;
        _isVisible = isVisible;
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, value);
    }
}

public sealed class ListColumns
{
    public ListColumn Size { get; } = new("size", "Size", 110, 50, true);
    public ListColumn Type { get; } = new("type", "Type", 110, 50, true);
    public ListColumn Modified { get; } = new("modified", "Modified", 140, 80, true);
    public ListColumn Created { get; } = new("created", "ColumnCreated", 140, 80, false, true);
    public ListColumn Accessed { get; } = new("accessed", "ColumnAccessed", 140, 80, false, true);
    public ListColumn Permissions { get; } = new("permissions", "ColumnPermissions", 100, 70, false, true);
    public ListColumn Owner { get; } = new("owner", "ColumnOwner", 90, 50, false, true);
    public ListColumn Group { get; } = new("group", "ColumnGroup", 90, 50, false, true);
    public ListColumn MimeType { get; } = new("mimetype", "ColumnMimeType", 170, 70, false);
    public ListColumn Location { get; } = new("location", "ColumnLocation", 220, 80, false);

    public event Action? VisibilityChanged;

    public ListColumns()
    {
        All = [Size, Type, Modified, Created, Accessed, Permissions, Owner, Group, MimeType, Location];
        string? saved = GuiSettings.ListColumns;
        if (saved != null)
        {
            HashSet<string> visible = saved.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
            foreach (ListColumn column in All)
            {
                column.IsVisible = visible.Contains(column.Id);
            }
        }

        foreach (ListColumn column in All)
        {
            column.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ListColumn.IsVisible))
                {
                    return;
                }

                GuiSettings.ListColumns = string.Join(',', All.Where(c => c.IsVisible).Select(c => c.Id));
                VisibilityChanged?.Invoke();
            };
        }
    }

    public IReadOnlyList<ListColumn> All { get; }

    public bool NeedsMetadata => All.Any(c => c.IsVisible && c.NeedsMetadata);

    public ListColumn? Find(string id)
    {
        return All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
