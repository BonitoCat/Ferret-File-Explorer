namespace FileExplorer.Core.Addons;

public enum AddonCommandKind
{
    Navigate,
    Refresh,
    Status,
    Message,
    Open,
    Prompt,
    Confirm,
    Progress,
    ProgressMax,
    ProgressTitle,
    ProgressText,
    ProgressStatus,
    ProgressItem,
    Loading,
    Select,
    Pick,
    Choose,
    Form,
    Field,
    FormShow,
    StatusBar,
    StatusBarTooltip,
    Place,
    PlaceRemove,
    Setting,
    Unknown,
}

public sealed record AddonCommand(AddonCommandKind Kind, string Argument)
{
    public const string Prefix = "fe:";

    public static bool TryParse(string? line, out AddonCommand? command)
    {
        command = null;
        if (line == null || !line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string body = line[Prefix.Length..];
        int space = body.IndexOf(' ');
        string name = space < 0 ? body : body[..space];
        string argument = space < 0 ? "" : body[(space + 1)..];

        AddonCommandKind? kind = name.Trim().ToLowerInvariant() switch
        {
            "navigate" or "cd" => AddonCommandKind.Navigate,
            "refresh" or "reload" => AddonCommandKind.Refresh,
            "status" => AddonCommandKind.Status,
            "message" or "alert" => AddonCommandKind.Message,
            "open" => AddonCommandKind.Open,
            "prompt" or "input" => AddonCommandKind.Prompt,
            "confirm" => AddonCommandKind.Confirm,
            "progress" => AddonCommandKind.Progress,
            "progress-max" => AddonCommandKind.ProgressMax,
            "progress-title" => AddonCommandKind.ProgressTitle,
            "progress-text" => AddonCommandKind.ProgressText,
            "progress-status" => AddonCommandKind.ProgressStatus,
            "progress-item" => AddonCommandKind.ProgressItem,
            "loading" => AddonCommandKind.Loading,
            "select" => AddonCommandKind.Select,
            "pick" => AddonCommandKind.Pick,
            "choose" => AddonCommandKind.Choose,
            "form" => AddonCommandKind.Form,
            "field" => AddonCommandKind.Field,
            "form-show" => AddonCommandKind.FormShow,
            "statusbar" => AddonCommandKind.StatusBar,
            "statusbar-tooltip" => AddonCommandKind.StatusBarTooltip,
            "place" => AddonCommandKind.Place,
            "place-remove" => AddonCommandKind.PlaceRemove,
            "setting" => AddonCommandKind.Setting,
            _ => null,
        };
        if (kind == null)
        {
            return false;
        }

        command = new(kind.Value, argument.Replace("\\n", "\n"));
        return true;
    }
}
