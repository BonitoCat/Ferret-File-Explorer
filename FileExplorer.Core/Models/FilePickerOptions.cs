namespace FileExplorer.Core.Models;

public class FilePickerOptions
{
    public PickerMode Mode { get; set; } = PickerMode.File;

    public string? InitialPath { get; set; }

    public string? Title { get; set; }

    public bool AllowMultiple { get; set; }

    public string? FilterPattern { get; set; }

    public string? ActionButtonText { get; set; }
}
