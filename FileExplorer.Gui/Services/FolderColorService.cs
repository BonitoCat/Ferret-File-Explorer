using System.Collections.Generic;
using FileExplorer.Core.Services;

namespace FileExplorer.Gui.Services;

public static class FolderColorService
{
    public const string DefaultFolderColor = "#84B4FA";

    public static readonly (string Name, string Hex)[] Presets =
    [
        ("Red", "#EF5350"),
        ("Orange", "#FF9F43"),
        ("Yellow", "#F6C945"),
        ("Green", "#66BB6A"),
        ("Teal", "#26A69A"),
        ("Aqua", "#4DD0E1"),
        ("Blue", "#4A7DF0"),
        ("Purple", "#AB7BF5"),
        ("Pink", "#F48FB1"),
        ("Brown", "#A1887F"),
        ("Grey", "#9E9E9E"),
        ("Dark", "#5C6370"),
    ];

    public static string? Get(string path)
    {
        return FolderColorStore.Get(path);
    }

    public static void Set(IEnumerable<string> paths, string? hex)
    {
        FolderColorStore.Set(paths, hex);
    }

    public static void Move(string oldPath, string newPath)
    {
        FolderColorStore.Move(oldPath, newPath);
    }

    public static void Remove(string path)
    {
        FolderColorStore.Remove(path);
    }
}
