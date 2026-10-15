using FileExplorer.Core.Services;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.States;

using TuiLib.Controls;
using System.Collections.Generic;

using FileExplorer.Cli.Models;
using FileExplorer.Cli.Utilities;

using InputLib;

namespace FileExplorer.Cli.Context;

public class ClipboardContext
{
    public List<string> Items { get; } = new();
    public ClipboardMode Mode { get; set; } = ClipboardMode.Copy;
}

public class EntryContext
{
    public required TuiListBox<EntryListItem> Menu { get; init; }
    public required NavigationState Navigation { get; set; }
    public HashSet<EntryListItem> SelectedItems { get; } = new();
    public ClipboardContext Clipboard { get; } = new();
    public string? SearchPattern { get; set; }
    public bool ShowHiddenFiles { get; set; } = CliSettings.ShowHiddenFiles;
    public bool ShowFileSizes { get; set; } = false;
    public InputListener? Listener { get; set; }
    public bool IsReloading { get; set; } = false;
    public bool HasActiveImagePreview { get; set; } = false;
    public bool IsLoading { get; set; }
    public CancellationTokenSource? LoadCts { get; set; }

    public string Cwd => Navigation.CurrentPath;
}
