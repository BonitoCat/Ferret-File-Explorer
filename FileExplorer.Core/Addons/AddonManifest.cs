namespace FileExplorer.Core.Addons;

public sealed class AddonManifest
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Website { get; set; }
    public object? Donate { get; set; }
    public string? Icon { get; set; }
    public string? Script { get; set; }
    public string? Interpreter { get; set; }
    public List<string>? Modes { get; set; }
    public Dictionary<string, string>? Hooks { get; set; }
    public string? Service { get; set; }
    public List<AddonSettingManifest>? Settings { get; set; }
    public List<AddonPlaceManifest>? Places { get; set; }
    public List<object>? Dependencies { get; set; }
    public List<AddonActionManifest> Actions { get; set; } = new();
}

public sealed class AddonSettingManifest
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? Default { get; set; }
    public List<string>? Choices { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
}

public sealed class AddonPlaceManifest
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Section { get; set; }
    public string? Icon { get; set; }
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
    public List<string>? Keywords { get; set; }
    public string? Icon { get; set; }
    public string? Confirm { get; set; }
    public string? Separator { get; set; }
    public List<string>? Modes { get; set; }
}
