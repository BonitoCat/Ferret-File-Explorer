using Avalonia.Media;

namespace FileExplorer.Gui.Models;

public sealed record AddonPlaceEntry(string SectionKey, string Section, string Name, string Path, IImage? Icon);
