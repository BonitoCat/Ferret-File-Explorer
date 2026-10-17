namespace FileExplorer.Core.Addons;

public enum AddonLocation
{
    ContextMenu,
    AddonsMenu,
}

[Flags]
public enum AddonTarget
{
    None = 0,
    File = 1,
    Folder = 2,
    Background = 4,
}

public sealed class AddonContext
{
    public string CurrentDirectory { get; }
    public IReadOnlyList<string> SelectedPaths { get; }

    public AddonContext(string currentDirectory, IEnumerable<string> selectedPaths)
    {
        CurrentDirectory = currentDirectory;
        SelectedPaths = selectedPaths.ToList();
    }

    public AddonTarget Target
    {
        get
        {
            if (SelectedPaths.Count == 0)
            {
                return AddonTarget.Background;
            }

            AddonTarget target = AddonTarget.None;
            foreach (string path in SelectedPaths)
            {
                target |= Directory.Exists(path) ? AddonTarget.Folder : AddonTarget.File;
            }

            return target;
        }
    }

    public IReadOnlyList<string> TargetPaths => SelectedPaths.Count > 0 ? SelectedPaths : [CurrentDirectory];
}
