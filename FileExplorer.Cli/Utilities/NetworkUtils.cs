using FileExplorer.Cli.Context;
using FileExplorer.Cli.Models;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Services;

using TuiLib;

namespace FileExplorer.Cli.Utilities;

public static class NetworkUtils
{
    private static readonly NetworkBrowseService BrowseService = new();
    private static readonly NetworkService ConnectService = new();
    private static readonly Color ServerColor = new(120, 180, 255);
    private static readonly Color ShareColor = new(132, 180, 250);
    private const int MaxLoginAttempts = 3;

    public static string GetTitle(string path)
    {
        return NetworkBrowseService.IsNetworkRoot(path) ? "Network" : $"Network › {NetworkBrowseService.GetLocationName(path)}";
    }

    public static List<EntryListItem> BuildItems(EntryContext context, string path, CancellationToken token)
    {
        List<EntryListItem> items = new();

        string? parent = NetworkBrowseService.GetParent(path);
        if (parent != null)
        {
            EntryListItem parentItem = new()
            {
                Entry = new FolderEntry { FullPath = parent, FullName = ".." },
                DisplayName = new ColoredText("..", Color.Reset),
                Icon = new ColoredText("   ", Color.Reset),
            };
            parentItem.OnClick = () => Navigate(context, parent, () => context.Menu.SelectedIndex = 0);
            items.Add(parentItem);
        }

        NetworkBrowseResult result = BrowseService.BrowseAsync(path, token).GetAwaiter().GetResult();
        for (int attempt = 0; result.NeedsCredentials && attempt < MaxLoginAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            NetworkCredentials? credentials = AskServerLogin(context, path, result.CredentialsRejected);
            if (credentials == null)
            {
                break;
            }

            NetworkBrowseService.SetCredentials(path, credentials);
            result = BrowseService.BrowseAsync(path, token).GetAwaiter().GetResult();
        }

        token.ThrowIfCancellationRequested();
        if (result.NeedsCredentials)
        {
            Program.Sidebar.StatusText = $"{GetTitle(path)} requires a login";
        }
        else if (result.Error != null)
        {
            Program.Sidebar.StatusText = $"Could not browse {GetTitle(path)}: {result.Error}";
        }

        foreach (NetworkBrowseEntry entry in result.Entries)
        {
            bool isServer = entry.Kind == NetworkBrowseKind.Container;
            Color color = isServer ? ServerColor : ShareColor;
            EntryListItem item = new()
            {
                Entry = new FolderEntry { FullPath = entry.Uri, FullName = entry.Name },
                DisplayName = new ColoredText(entry.Name, color),
                Icon = new ColoredText(isServer ? "\x1b[1m⇄  \x1b[0m" : "\x1b[1m🗁  \x1b[0m", color),
            };
            item.Suffixes.Add(new ColoredText(isServer ? " (Server)" : " (Share)", Color.Gray));
            item.OnClick = isServer ? () => Navigate(context, entry.Uri) : () => OpenShare(context, entry);
            items.Add(item);
        }

        return items;
    }

    private static NetworkCredentials? AskServerLogin(EntryContext context, string path, bool rejected)
    {
        string server = NetworkBrowseService.GetLocationName(path);
        string? user = context.Input(rejected ? $"Login failed. Username for {server}:" : $"Username for {server}:", Environment.UserName);
        if (string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        string? password = context.Input("Password:", "", true);
        return password == null ? null : new NetworkCredentials(user.Trim(), null, password);
    }

    private static void Navigate(EntryContext context, string path, Action? onLoaded = null)
    {
        context.Navigation.NavigateTo(path);
        FolderUtils.LoadFolderContent(context, path, null, onLoaded);
        FolderUtils.SetWorkingDirectory(path);
    }

    private static void OpenShare(EntryContext context, NetworkBrowseEntry entry)
    {
        Program.Sidebar.StatusText = $"Connecting to {entry.Name}…";
        Task.Run(async () =>
        {
            NetworkMountResult? result = await ConnectService.ConnectBrowsedShareAsync(entry.Uri);
            if (result.NeedsCredentials)
            {
                result = await LoginAsync(context, entry.Name, result.Info);
            }

            if (result == null)
            {
                Program.Sidebar.StatusText = "";
                return;
            }

            if (!result.Success || !Directory.Exists(result.MountPath))
            {
                Program.Sidebar.StatusText = string.IsNullOrWhiteSpace(result.ErrorMessage) ? $"Could not connect to {entry.Name}" : $"Connection failed: {result.ErrorMessage}";
                return;
            }

            Program.Sidebar.StatusText = "";
            Navigate(context, result.MountPath);
            Program.Sidebar.Reload();
        });
    }

    private static async Task<NetworkMountResult?> LoginAsync(EntryContext context, string name, NetworkConnectionInfo info)
    {
        string? user = context.Input($"Username for {name}:", info.Username ?? Environment.UserName);
        if (string.IsNullOrWhiteSpace(user))
        {
            return null;
        }

        string? password = context.Input("Password:", "", true);
        if (password == null)
        {
            return null;
        }

        Program.Sidebar.StatusText = $"Connecting to {name}…";
        NetworkConnectionInfo login = info with { Username = user.Trim(), Password = password, Anonymous = false };
        NetworkMountResult result = await ConnectService.ConnectAsync(login);
        if (!result.Success)
        {
            return result;
        }

        new NetworkBookmarkService().AddBookmark(new()
        {
            Name = result.DisplayName,
            Uri = result.Info.BuildUri(),
            Password = result.Info.Password,
        });

        return result;
    }
}
