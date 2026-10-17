namespace FileExplorer.Core.Addons;

public enum AddonCommandKind
{
    Navigate,
    Refresh,
    Status,
    Message,
    Open,
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
