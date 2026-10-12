namespace FileExplorer.Gui.Models;

public class BreadcrumbItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsLast { get; init; }
}
