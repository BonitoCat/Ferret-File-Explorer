namespace FileExplorer.Core.Addons;

public enum AddonLocation
{
    ContextMenu,
    AddonsMenu,
    PathBar,
}

[Flags]
public enum AddonTarget
{
    None = 0,
    File = 1,
    Folder = 2,
    Background = 4,
}

[Flags]
public enum AddonMode
{
    None = 0,
    Explorer = 1,
    FilePicker = 2,
    FolderPicker = 4,
    SavePicker = 8,
    Picker = FilePicker | FolderPicker | SavePicker,
    All = Explorer | Picker,
}

public enum AddonHook
{
    PostInstall,
    PreUninstall,
    PostLoad,
    PostStart,
    PreClose,
}

public sealed class AddonContext
{
    public string CurrentDirectory { get; }
    public IReadOnlyList<string> SelectedPaths { get; }
    public string Input { get; }
    public AddonMode Mode { get; }

    public AddonContext(string currentDirectory, IEnumerable<string> selectedPaths, string input = "", AddonMode mode = AddonMode.Explorer)
    {
        CurrentDirectory = currentDirectory;
        SelectedPaths = selectedPaths.ToList();
        Input = input;
        Mode = mode;
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
