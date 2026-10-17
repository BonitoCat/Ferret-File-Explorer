using System.Collections.Concurrent;
using System.Diagnostics;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public enum NetworkBrowseKind
{
    Container,
    Mountable,
}

public sealed record NetworkBrowseEntry(string Name, string Uri, NetworkBrowseKind Kind);

public sealed record NetworkBrowseResult(List<NetworkBrowseEntry> Entries, string? Error, bool NeedsCredentials, bool CredentialsRejected);

public sealed record NetworkCredentials(string Username, string? Domain, string Password);

public sealed class NetworkBrowseService
{
    public const string NetworkRoot = "network:///";

    private const string SmbPrefix = "smb://";
    private const int DiscoveryRetryDelayMs = 1500;
    private const int MountTimeoutMs = 15000;

    private static readonly string[] MountableSchemes = ["smb", "sftp", "ftp", "dav", "davs"];
    private static readonly ConcurrentDictionary<string, NetworkCredentials> SessionCredentials = new(StringComparer.OrdinalIgnoreCase);

    private readonly NetworkService _networkService = new();

    public static void SetCredentials(string uri, NetworkCredentials credentials)
    {
        string? host = GetHost(uri);
        if (host != null)
        {
            SessionCredentials[host] = credentials;
        }
    }

    public static NetworkCredentials? GetCredentials(string uri)
    {
        string? host = GetHost(uri);
        return host != null && SessionCredentials.TryGetValue(host, out NetworkCredentials? credentials) ? credentials : null;
    }

    private static void ForgetCredentials(string uri)
    {
        string? host = GetHost(uri);
        if (host != null)
        {
            SessionCredentials.TryRemove(host, out _);
        }
    }

    private static string? GetHost(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) && parsed.Host.Length > 0 ? parsed.Host.TrimEnd('.') : null;
    }

    public static bool IsBrowseLocation(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path.StartsWith("network:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!path.StartsWith(SmbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = path[SmbPrefix.Length..].Trim('/');
        return !rest.Contains('/');
    }

    public static bool IsNetworkRoot(string? path)
    {
        return path != null && path.StartsWith("network:", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetParent(string path)
    {
        return IsNetworkRoot(path) ? null : NetworkRoot;
    }

    public static string GetLocationName(string path)
    {
        if (!path.StartsWith(SmbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string host = path[SmbPrefix.Length..].Trim('/');
        int portSeparator = host.LastIndexOf(':');
        if (portSeparator > 0 && host.IndexOf(']') < portSeparator)
        {
            host = host[..portSeparator];
        }

        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            host = host[..^".local".Length];
        }

        return host.Length > 0 ? Uri.UnescapeDataString(host) : "SMB";
    }

    public async Task<(List<NetworkBrowseEntry> Entries, string? Error)> ListAsync(string uri, CancellationToken token)
    {
        NetworkBrowseResult result = await BrowseAsync(uri, token);
        return (result.Entries, result.Error);
    }

    public async Task<NetworkBrowseResult> BrowseAsync(string uri, CancellationToken token)
    {
        if (IsNetworkRoot(uri))
        {
            return ToResult(await ListEntriesAsync(uri, token), null);
        }

        if (!uri.StartsWith(SmbPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string? mountError = await EnsureMountedAsync(uri, token);
            return ToResult(await ListEntriesAsync(uri, token), mountError);
        }

        NetworkConnectionInfo info = NetworkService.ParseUri(uri);
        NetworkCredentials? credentials = GetCredentials(uri);
        if (credentials != null)
        {
            NetworkMountResult login = await _networkService.MountLocationAsync(info with
            {
                Username = credentials.Username,
                Domain = credentials.Domain,
                Password = credentials.Password,
                Anonymous = false,
            }, token);
            if (login.NeedsCredentials)
            {
                ForgetCredentials(uri);
                return new(new(), login.ErrorMessage, true, true);
            }

            return ToResult(await ListEntriesAsync(uri, token), login.Success ? null : login.ErrorMessage);
        }

        string? anonymousError = await EnsureMountedAsync(uri, token);
        (List<NetworkBrowseEntry> Entries, string? Error) listing = await ListEntriesAsync(uri, token);
        if (listing.Error == null || listing.Entries.Count > 0)
        {
            return ToResult(listing, anonymousError);
        }

        NetworkMountResult result = await _networkService.MountLocationAsync(info with { Anonymous = false }, token);
        if (result.NeedsCredentials)
        {
            return new(new(), listing.Error, true, false);
        }

        (List<NetworkBrowseEntry> Entries, string? Error) retry = await ListEntriesAsync(uri, token);
        if (retry.Error == null || retry.Entries.Count > 0)
        {
            return ToResult(retry, null);
        }

        return ToResult(listing, result.Success ? anonymousError : result.ErrorMessage);
    }

    private static NetworkBrowseResult ToResult((List<NetworkBrowseEntry> Entries, string? Error) listing, string? mountError)
    {
        if (!string.IsNullOrEmpty(listing.Error) && listing.Entries.Count == 0)
        {
            return new(listing.Entries, string.IsNullOrEmpty(mountError) ? listing.Error : mountError, false, false);
        }

        return new(listing.Entries, null, false, false);
    }

    private static async Task<(List<NetworkBrowseEntry> Entries, string? Error)> ListEntriesAsync(string uri, CancellationToken token)
    {
        List<GioFileEntry> raw = GioFileLister.List(uri, out string? error);
        if (raw.Count == 0 && error == null && IsNetworkRoot(uri))
        {
            await Task.Delay(DiscoveryRetryDelayMs, token);
            raw = GioFileLister.List(uri, out error);
        }

        List<NetworkBrowseEntry> entries = raw
            .Select(ToEntry)
            .OfType<NetworkBrowseEntry>()
            .DistinctBy(e => e.Uri.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return (entries, error);
    }

    private static NetworkBrowseEntry? ToEntry(GioFileEntry entry)
    {
        string target = entry.TargetUri ?? entry.Uri;
        string name = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Name : entry.DisplayName;
        if (name.EndsWith('$'))
        {
            return null;
        }

        if (IsBrowseLocation(target))
        {
            return new(name, target, NetworkBrowseKind.Container);
        }

        int schemeEnd = target.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            return null;
        }

        string scheme = target[..schemeEnd].ToLowerInvariant();
        return MountableSchemes.Contains(scheme) ? new(name, target, NetworkBrowseKind.Mountable) : null;
    }

    private static async Task<string?> EnsureMountedAsync(string uri, CancellationToken token)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "gio",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("mount");
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(uri);
            psi.Environment["LC_ALL"] = "C";

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            process.StandardInput.Close();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(token);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(MountTimeoutMs);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                return null;
            }

            string stderr = (await stderrTask).Trim();
            if (process.ExitCode == 0 || stderr.Contains("already mounted", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ex.Message;
        }
    }
}
