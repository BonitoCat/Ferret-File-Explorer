namespace FileExplorer.Core.Addons;

public sealed class AddonManifest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Website { get; set; }
    public string? Icon { get; set; }
    public string? Script { get; set; }
    public string? Interpreter { get; set; }
    public List<AddonActionManifest> Actions { get; set; } = new();
}

public sealed class AddonActionManifest
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Location { get; set; }
    public int? Index { get; set; }
    public string? After { get; set; }
    public string? Before { get; set; }
    public List<string>? Targets { get; set; }
    public List<string>? Extensions { get; set; }
    public bool Multiple { get; set; } = true;
    public string? Script { get; set; }
    public string? Command { get; set; }
    public string? Interpreter { get; set; }
    public List<string>? Args { get; set; }
    public bool Terminal { get; set; }
    public string? Shortcut { get; set; }
    public string? Icon { get; set; }
    public string? Confirm { get; set; }
    public string? Separator { get; set; }
}
