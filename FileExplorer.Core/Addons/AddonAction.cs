namespace FileExplorer.Core.Addons;

public sealed class AddonAction
{
    public static readonly IReadOnlyList<string> DefaultArgs = ["{paths}"];

    public Addon Addon { get; }
    public string Id { get; }
    public string TitleKey { get; }
    public AddonLocation Location { get; }
    public int? Index { get; }
    public string? After { get; }
    public string? Before { get; }
    public AddonTarget Targets { get; }
    public IReadOnlyList<string> Extensions { get; }
    public bool Multiple { get; }
    public string? ScriptPath { get; }
    public string? Command { get; }
    public string? Interpreter { get; }
    public IReadOnlyList<string> Args { get; }
    public bool Terminal { get; }
    public string? Shortcut { get; }
    public IReadOnlyList<string> Keywords { get; }
    public string? IconPath { get; }
    public string? ConfirmKey { get; }
    public bool SeparatorBefore { get; }
    public bool SeparatorAfter { get; }

    public string Key => $"{Addon.Id}/{Id}";

    internal AddonAction(Addon addon, AddonActionManifest manifest, AddonLocation location, AddonTarget targets, bool separatorBefore, bool separatorAfter, int position)
    {
        Addon = addon;
        Id = string.IsNullOrWhiteSpace(manifest.Id) ? position.ToString() : manifest.Id.Trim();
        TitleKey = string.IsNullOrWhiteSpace(manifest.Title) ? addon.NameKey : manifest.Title.Trim();
        Location = location;
        Index = manifest.Index is >= 0 ? manifest.Index : null;
        After = NormalizeAnchor(manifest.After);
        Before = NormalizeAnchor(manifest.Before);
        Targets = targets;
        Extensions = (manifest.Extensions ?? new())
            .Select(NormalizeExtension)
            .Where(e => e.Length > 0)
            .ToList();
        Multiple = manifest.Multiple;
        Command = string.IsNullOrWhiteSpace(manifest.Command) ? null : manifest.Command.Trim();
        ScriptPath = Command == null ? addon.ResolvePath(manifest.Script ?? addon.Manifest.Script) : null;
        Interpreter = manifest.Interpreter ?? addon.Manifest.Interpreter;
        Args = manifest.Args ?? DefaultArgs;
        Terminal = manifest.Terminal;
        Shortcut = string.IsNullOrWhiteSpace(manifest.Shortcut) ? null : manifest.Shortcut.Trim();
        Keywords = (manifest.Keywords ?? (string.IsNullOrWhiteSpace(manifest.Id) ? new() : [manifest.Id]))
            .Select(k => k.Trim())
            .Where(k => k.Length > 0 && !k.Contains(' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        IconPath = addon.ResolvePath(manifest.Icon) ?? addon.IconPath;
        ConfirmKey = string.IsNullOrWhiteSpace(manifest.Confirm) ? null : manifest.Confirm.Trim();
        SeparatorBefore = separatorBefore;
        SeparatorAfter = separatorAfter;
    }

    public string GetTitle(string languageCode)
    {
        return Addon.Translate(TitleKey, languageCode);
    }

    public bool Matches(AddonContext context)
    {
        if (Location != AddonLocation.ContextMenu)
        {
            return true;
        }

        if (context.SelectedPaths.Count == 0)
        {
            return Targets.HasFlag(AddonTarget.Background);
        }

        if (!Multiple && context.SelectedPaths.Count > 1)
        {
            return false;
        }

        return context.SelectedPaths.All(MatchesPath);
    }

    private bool MatchesPath(string path)
    {
        if (Directory.Exists(path))
        {
            return Targets.HasFlag(AddonTarget.Folder);
        }

        if (!Targets.HasFlag(AddonTarget.File))
        {
            return false;
        }

        if (Extensions.Count == 0)
        {
            return true;
        }

        string name = Path.GetFileName(path);
        return Extensions.Any(e => e == "*" || name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeAnchor(string? anchor)
    {
        return string.IsNullOrWhiteSpace(anchor) ? null : anchor.Trim().ToLowerInvariant();
    }

    private static string NormalizeExtension(string extension)
    {
        string trimmed = extension.Trim().ToLowerInvariant();
        if (trimmed.Length == 0 || trimmed == "*" || trimmed.StartsWith('.'))
        {
            return trimmed;
        }

        return "." + trimmed;
    }
}
