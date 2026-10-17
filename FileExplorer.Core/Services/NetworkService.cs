using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Core.Services;

public enum NetworkProtocol
{
    Sftp,
    Smb,
    Ftp,
    Dav,
    Davs
}

public record NetworkConnectionInfo
{
    public NetworkProtocol Protocol { get; init; } = NetworkProtocol.Sftp;
    public string Host { get; init; } = "";
    public int? Port { get; init; }
    public string? Path { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Domain { get; init; }
    public bool Anonymous { get; init; }

    public string BuildUri()
    {
        string scheme = Protocol switch
        {
            NetworkProtocol.Sftp => "sftp",
            NetworkProtocol.Smb => "smb",
            NetworkProtocol.Ftp => "ftp",
            NetworkProtocol.Dav => "dav",
            NetworkProtocol.Davs => "davs",
            _ => "sftp"
        };

        StringBuilder builder = new();
        builder.Append(scheme);
        builder.Append("://");

        if (Protocol == NetworkProtocol.Smb && !string.IsNullOrEmpty(Domain))
        {
            builder.Append(Uri.EscapeDataString(Domain));
            builder.Append(';');
        }

        if (!Anonymous && !string.IsNullOrEmpty(Username))
        {
            builder.Append(Uri.EscapeDataString(Username));
            builder.Append('@');
        }

        builder.Append(Host.Trim());

        if (Port.HasValue && Port.Value > 0 && !IsDefaultPort(Protocol, Port.Value))
        {
            builder.Append(':');
            builder.Append(Port.Value);
        }

        if (!string.IsNullOrEmpty(Path))
        {
            string trimmedPath = Path.Trim();
            if (!trimmedPath.StartsWith('/'))
            {
                builder.Append('/');
            }
            builder.Append(trimmedPath);
        }
        else if (Protocol == NetworkProtocol.Sftp || Protocol == NetworkProtocol.Ftp || Protocol == NetworkProtocol.Dav || Protocol == NetworkProtocol.Davs)
        {
            builder.Append('/');
        }

        return builder.ToString();
    }

    public string GetDisplayName()
    {
        string name = !Anonymous && !string.IsNullOrEmpty(Username)
            ? $"{Username}@{Host}"
            : $"{Protocol.ToString().ToUpperInvariant()}: {Host}";

        if (!string.IsNullOrEmpty(Path) && Path != "/")
        {
            name += Path.StartsWith('/') ? Path : "/" + Path;
        }

        return name;
    }

    private static bool IsDefaultPort(NetworkProtocol protocol, int port)
    {
        return protocol switch
        {
            NetworkProtocol.Sftp => port == 22,
            NetworkProtocol.Smb => port == 445,
            NetworkProtocol.Ftp => port == 21,
            NetworkProtocol.Dav => port == 80,
            NetworkProtocol.Davs => port == 443,
            _ => false
        };
    }
}

public sealed record NetworkMountResult(bool Success, string MountPath, string ErrorMessage, NetworkConnectionInfo Info, string DisplayName);

public class NetworkService
{
    public static string GetUserId()
    {
        try
        {
            Process proc = new()
            {
                StartInfo = new()
                {
                    FileName = "id",
                    Arguments = "-u",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            proc.Start();
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                return output;
            }
        }
        catch
        {
        }

        return "1000";
    }

    public static HashSet<string> GetGvfsMounts()
    {
        HashSet<string> mounts = new(StringComparer.OrdinalIgnoreCase);
        string gvfsDir = $"/run/user/{GetUserId()}/gvfs";

        if (!Directory.Exists(gvfsDir))
        {
            return mounts;
        }

        try
        {
            string[] directories = Directory.GetDirectories(gvfsDir);
            foreach (string dir in directories)
            {
                mounts.Add(dir);
            }
        }
        catch
        {
        }

        return mounts;
    }

    public async Task<(bool Success, string MountPath, string ErrorMessage)> MountAsync(NetworkConnectionInfo info, CancellationToken cancellationToken = default)
    {
        NetworkMountResult result = await ConnectAsync(info, cancellationToken);
        return (result.Success, result.MountPath, result.ErrorMessage);
    }

    public async Task<NetworkMountResult> ConnectAsync(NetworkConnectionInfo info, CancellationToken cancellationToken = default)
    {
        info = NormalizeServerInput(info);
        string displayName = info.GetDisplayName();
        if (string.IsNullOrWhiteSpace(info.Host))
        {
            return new(false, "", "Server hostname or IP address cannot be empty.", info, displayName);
        }

        bool isDav = info.Protocol is NetworkProtocol.Dav or NetworkProtocol.Davs;
        string? existingMount = isDav ? null : FindExistingMount(info);
        if (existingMount != null)
        {
            return new(true, existingMount, "", info, displayName);
        }

        string? unreachableError = await ProbeHostAsync(info, cancellationToken);
        if (unreachableError != null)
        {
            return new(false, "", unreachableError, info, displayName);
        }

        if (isDav)
        {
            (NetworkConnectionInfo resolved, string? davError) = await ResolveDavPathAsync(info, cancellationToken);
            if (davError != null)
            {
                return new(false, "", davError, info, displayName);
            }

            info = resolved;
            existingMount = FindExistingMount(info);
            if (existingMount != null)
            {
                return new(true, existingMount, "", info, displayName);
            }
        }

        (bool success, string mountPath, string error) = await RunGioMountAsync(info, cancellationToken);
        return new(success, mountPath, error, info, displayName);
    }

    public static NetworkConnectionInfo NormalizeServerInput(NetworkConnectionInfo info)
    {
        string input = info.Host.Trim();
        if (input.Length == 0)
        {
            return info with { Host = input };
        }

        NetworkProtocol protocol = info.Protocol;
        int? port = info.Port;
        int schemeEnd = input.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            NetworkProtocol? parsed = input[..schemeEnd].ToLowerInvariant() switch
            {
                "sftp" or "ssh" => NetworkProtocol.Sftp,
                "smb" or "cifs" => NetworkProtocol.Smb,
                "ftp" => NetworkProtocol.Ftp,
                "http" or "dav" or "webdav" => NetworkProtocol.Dav,
                "https" or "davs" or "webdavs" => NetworkProtocol.Davs,
                _ => null,
            };

            if (parsed.HasValue && parsed.Value != protocol)
            {
                protocol = parsed.Value;
                port = null;
            }

            input = input[(schemeEnd + 3)..];
        }

        string? path = null;
        int slash = input.IndexOf('/');
        if (slash >= 0)
        {
            path = input[slash..];
            input = input[..slash];
        }

        string? user = null;
        int at = input.LastIndexOf('@');
        if (at >= 0)
        {
            user = Uri.UnescapeDataString(input[..at]);
            input = input[(at + 1)..];
        }

        int colon = input.LastIndexOf(':');
        if (colon > 0 && !input.EndsWith(']') && int.TryParse(input[(colon + 1)..], out int explicitPort) && explicitPort > 0)
        {
            port = explicitPort;
            input = input[..colon];
        }

        string? domain = info.Domain;
        if (user != null && user.Contains(';'))
        {
            string[] userParts = user.Split(';', 2);
            domain ??= userParts[0];
            user = userParts[1];
        }

        return info with
        {
            Protocol = protocol,
            Host = input,
            Port = port,
            Path = string.IsNullOrWhiteSpace(info.Path) ? path?.TrimEnd() : info.Path.Trim(),
            Username = string.IsNullOrWhiteSpace(info.Username) ? user : info.Username.Trim(),
            Domain = domain,
        };
    }

    private static async Task<(NetworkConnectionInfo Info, string? Error)> ResolveDavPathAsync(NetworkConnectionInfo info, CancellationToken cancellationToken)
    {
        bool hasPath = !string.IsNullOrEmpty(info.Path) && info.Path != "/";
        List<string> candidates = new();
        if (hasPath)
        {
            candidates.Add(info.Path!.StartsWith('/') ? info.Path! : "/" + info.Path);
        }
        else
        {
            candidates.Add("/");
            if (!string.IsNullOrEmpty(info.Username))
            {
                candidates.Add($"/remote.php/dav/files/{Uri.EscapeDataString(info.Username)}/");
            }

            candidates.Add("/remote.php/webdav/");
        }

        string scheme = info.Protocol == NetworkProtocol.Davs ? "https" : "http";
        int port = info.Port ?? (info.Protocol == NetworkProtocol.Davs ? 443 : 80);
        bool sendCredentials = !info.Anonymous && !string.IsNullOrEmpty(info.Username);

        using HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        bool allDefinitive = true;
        foreach (string candidate in candidates)
        {
            UriBuilder builder = new(scheme, info.Host, port) { Path = candidate };
            using HttpRequestMessage request = new(new HttpMethod("PROPFIND"), builder.Uri);
            request.Headers.Add("Depth", "0");
            request.Content = new StringContent("<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>", Encoding.UTF8, "application/xml");
            if (sendCredentials)
            {
                string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{info.Username}:{info.Password}"));
                request.Headers.Authorization = new("Basic", token);
            }

            int status;
            try
            {
                using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
                status = (int) response.StatusCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                allDefinitive = false;
                continue;
            }

            if (status == 207)
            {
                return (info with { Path = candidate }, null);
            }

            if (status == 401)
            {
                if (sendCredentials && !string.IsNullOrEmpty(info.Password))
                {
                    return (info, "Authentication failed: wrong username or password.");
                }

                return (info with { Path = candidate }, null);
            }

            if (status is >= 500 or 408 or 429)
            {
                allDefinitive = false;
            }
        }

        if (hasPath || !allDefinitive)
        {
            return (info, null);
        }

        return (info, $"No WebDAV share found on {info.Host}. Enter the share path in the Folder field (e.g. /remote.php/dav/files/USERNAME/ for Nextcloud).");
    }

    private static async Task<(bool Success, string MountPath, string ErrorMessage)> RunGioMountAsync(NetworkConnectionInfo info, CancellationToken cancellationToken)
    {
        HashSet<string> beforeMounts = GetGvfsMounts();
        string uri = info.BuildUri();

        List<string> argsList = new();
        if (info.Anonymous)
        {
            argsList.Add("-a");
        }

        argsList.Add(uri);
        string arguments = string.Join(" ", argsList.Select(a => $"\"{a}\""));

        ProcessStartInfo startInfo = new()
        {
            FileName = "gio",
            Arguments = $"mount {arguments}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment["LC_ALL"] = "C";

        Process process = new() { StartInfo = startInfo };
        StringBuilder outputBuilder = new();
        StringBuilder errorBuilder = new();
        GioPromptResponder? responder = null;

        try
        {
            process.Start();
            responder = new(process, info);

            Task readOutTask = ReadPromptStreamAsync(process.StandardOutput, outputBuilder, responder);
            Task readErrTask = ReadPromptStreamAsync(process.StandardError, errorBuilder, responder);

            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(35), cancellationToken);
            Task completedTask = await Task.WhenAny(Task.WhenAll(process.WaitForExitAsync(cancellationToken), readOutTask, readErrTask), timeoutTask);

            if (completedTask == timeoutTask)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                return (false, "", responder.Question != null ? $"Connection needs confirmation: {responder.Question}" : "Connection timed out.");
            }
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }

        if (responder.PasswordMissing)
        {
            return (false, "", "This server requires a password.");
        }

        if (responder.AuthenticationFailed)
        {
            return (false, "", "Authentication failed: wrong username or password.");
        }

        if (responder.Question != null)
        {
            return (false, "", $"Connection was not confirmed: {responder.Question}");
        }

        if (process.ExitCode != 0)
        {
            string alreadyMounted = errorBuilder.ToString() + outputBuilder;
            string? mounted = FindExistingMount(info);
            if (mounted != null && alreadyMounted.Contains("already mounted", StringComparison.OrdinalIgnoreCase))
            {
                return (true, mounted, "");
            }

            string err = CleanGioError(errorBuilder.ToString());
            if (string.IsNullOrEmpty(err))
            {
                err = CleanGioError(outputBuilder.ToString());
            }

            if (string.IsNullOrEmpty(err))
            {
                err = $"Mount process exited with code {process.ExitCode}";
            }

            return (false, "", err);
        }

        HashSet<string> afterMounts = beforeMounts;
        string? newMount = null;
        for (int attempt = 0; attempt < 25 && newMount == null; attempt++)
        {
            await Task.Delay(200, cancellationToken);
            afterMounts = GetGvfsMounts();
            newMount = afterMounts.Except(beforeMounts).FirstOrDefault() ?? FindExistingMount(info);
        }

        if (string.IsNullOrEmpty(newMount))
        {
            newMount = afterMounts.FirstOrDefault(m => m.Contains(info.Host, StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrEmpty(newMount))
        {
            string scheme = info.Protocol switch
            {
                NetworkProtocol.Smb => "smb-share",
                NetworkProtocol.Ftp => "ftp",
                NetworkProtocol.Dav => "dav",
                NetworkProtocol.Davs => "dav",
                _ => "sftp"
            };

            newMount = afterMounts.FirstOrDefault(m => Path.GetFileName(m).StartsWith(scheme, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(newMount))
        {
            return (true, newMount, "");
        }

        string gvfsRoot = $"/run/user/{GetUserId()}/gvfs";
        return (true, gvfsRoot, "");
    }

    private static async Task ReadPromptStreamAsync(StreamReader reader, StringBuilder log, GioPromptResponder responder)
    {
        char[] buffer = new char[512];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            string chunk = new(buffer, 0, read);
            lock (log)
            {
                log.Append(chunk);
            }

            await responder.FeedAsync(chunk);
        }
    }

    private static string CleanGioError(string text)
    {
        List<string> lines = text
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Where(l => !l.StartsWith("Password:", StringComparison.Ordinal) && !l.StartsWith("User", StringComparison.Ordinal) && !l.StartsWith("Domain", StringComparison.Ordinal))
            .Where(l => !l.Equals("Authentication Required", StringComparison.Ordinal) && !l.StartsWith("Enter password for", StringComparison.Ordinal))
            .ToList();

        string message = lines.LastOrDefault() ?? "";
        const string prefix = "gio: ";
        if (message.StartsWith(prefix, StringComparison.Ordinal))
        {
            message = message[prefix.Length..];
            int colon = message.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && message[..colon].Contains("://", StringComparison.Ordinal))
            {
                message = message[(colon + 2)..];
            }
        }

        return message;
    }

    private static async Task<string?> ProbeHostAsync(NetworkConnectionInfo info, CancellationToken cancellationToken)
    {
        int port = info.Port ?? info.Protocol switch
        {
            NetworkProtocol.Smb => 445,
            NetworkProtocol.Ftp => 21,
            NetworkProtocol.Dav => 80,
            NetworkProtocol.Davs => 443,
            _ => 22
        };

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(info.Host, port, timeoutCts.Token);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"Server {info.Host}:{port} is not responding.";
        }
        catch (SocketException ex)
        {
            return $"Cannot reach {info.Host}:{port}: {ex.Message}";
        }
    }

    public static string? FindExistingMount(NetworkConnectionInfo info)
    {
        string prefix = info.Protocol switch
        {
            NetworkProtocol.Smb => "smb-share:",
            NetworkProtocol.Ftp => "ftp:",
            NetworkProtocol.Dav or NetworkProtocol.Davs => "dav",
            _ => "sftp:",
        };

        string host = info.Host.Trim();
        foreach (string mount in GetGvfsMounts())
        {
            string name = System.IO.Path.GetFileName(mount);
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Dictionary<string, string> keys = name[(name.IndexOf(':') + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.OrdinalIgnoreCase);

            string mountHost = keys.GetValueOrDefault("host") ?? keys.GetValueOrDefault("server") ?? "";
            if (!string.Equals(mountHost, host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (info.Port.HasValue && keys.TryGetValue("port", out string? port) && port != info.Port.Value.ToString())
            {
                continue;
            }

            if (!string.IsNullOrEmpty(info.Username) && keys.TryGetValue("user", out string? user) && !string.Equals(user, info.Username, StringComparison.Ordinal))
            {
                continue;
            }

            if (info.Protocol == NetworkProtocol.Smb && !string.IsNullOrEmpty(info.Path) && keys.TryGetValue("share", out string? share)
                && !string.Equals(share, info.Path.Trim('/').Split('/')[0], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (info.Protocol is NetworkProtocol.Dav or NetworkProtocol.Davs)
            {
                string expected = (info.Path ?? "").Trim('/');
                string actual = keys.GetValueOrDefault("prefix", "").Trim('/');
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    continue;
                }
            }

            return mount;
        }

        return null;
    }

    public async Task<(bool Success, string MountPath, string ErrorMessage)> MountUriAsync(string uri, string? password = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return (false, "", "URI cannot be empty.");
        }

        NetworkConnectionInfo info = ParseUri(uri);
        if (!string.IsNullOrEmpty(password))
        {
            info = info with { Password = password };
        }

        return await MountAsync(info, cancellationToken);
    }

    public async Task<(bool Success, string ErrorMessage)> UnmountAsync(string pathOrUri, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri))
        {
            return (false, "Path cannot be empty.");
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "gio",
            Arguments = $"mount -u \"{pathOrUri}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process = new() { StartInfo = startInfo };
        process.Start();

        string error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            return (true, "");
        }

        if (Directory.Exists(pathOrUri))
        {
            try
            {
                Process fallbackProc = new()
                {
                    StartInfo = new()
                    {
                        FileName = "udisksctl",
                        Arguments = $"unmount -p \"{pathOrUri}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                fallbackProc.Start();
                await fallbackProc.WaitForExitAsync(cancellationToken);

                if (fallbackProc.ExitCode == 0)
                {
                    return (true, "");
                }
            }
            catch
            {
            }
        }

        return (false, string.IsNullOrWhiteSpace(error) ? "Failed to dismount volume." : error.Trim());
    }

    public static NetworkConnectionInfo ParseUri(string uriString)
    {
        Uri uri = new(uriString);
        NetworkProtocol protocol = uri.Scheme.ToLowerInvariant() switch
        {
            "smb" => NetworkProtocol.Smb,
            "ftp" => NetworkProtocol.Ftp,
            "dav" => NetworkProtocol.Dav,
            "davs" => NetworkProtocol.Davs,
            _ => NetworkProtocol.Sftp
        };

        string host = uri.Host;
        int? port = uri.Port > 0 ? uri.Port : null;
        string? path = !string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/" ? uri.AbsolutePath : null;
        string? user = !string.IsNullOrEmpty(uri.UserInfo) ? uri.UserInfo : null;
        string? domain = null;

        if (user != null && user.Contains(';'))
        {
            string[] userParts = user.Split(';', 2);
            domain = userParts[0];
            user = userParts[1];
        }

        return new()
        {
            Protocol = protocol,
            Host = host,
            Port = port,
            Path = path,
            Username = user,
            Domain = domain
        };
    }

    private sealed class GioPromptResponder
    {
        private readonly Process _process;
        private readonly NetworkConnectionInfo _info;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly StringBuilder _pending = new();
        private int _passwordPrompts;

        public bool AuthenticationFailed { get; private set; }
        public bool PasswordMissing { get; private set; }
        public string? Question { get; private set; }

        public GioPromptResponder(Process process, NetworkConnectionInfo info)
        {
            _process = process;
            _info = info;
        }

        public async Task FeedAsync(string chunk)
        {
            await _lock.WaitAsync();
            try
            {
                _pending.Append(chunk);
                string text = _pending.ToString();
                string? answer = GetAnswer(text);
                if (answer == null)
                {
                    return;
                }

                _pending.Clear();
                await _process.StandardInput.WriteLineAsync(answer);
                await _process.StandardInput.FlushAsync();
            }
            catch
            {
            }
            finally
            {
                _lock.Release();
            }
        }

        private string? GetAnswer(string text)
        {
            string trimmed = text.TrimEnd();
            if (trimmed.Contains("(yes/no", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith('?'))
            {
                return "yes";
            }

            if (!trimmed.EndsWith(':'))
            {
                return null;
            }

            string lastLine = trimmed[(trimmed.LastIndexOf('\n') + 1)..].Trim();
            if (lastLine.StartsWith("Password", StringComparison.Ordinal))
            {
                _passwordPrompts++;
                if (string.IsNullOrEmpty(_info.Password))
                {
                    PasswordMissing = true;
                    Kill();
                    return null;
                }

                if (_passwordPrompts > 1)
                {
                    AuthenticationFailed = true;
                    Kill();
                    return null;
                }

                return _info.Password;
            }

            if (lastLine.StartsWith("User", StringComparison.Ordinal))
            {
                return _info.Username ?? "";
            }

            if (lastLine.StartsWith("Domain", StringComparison.Ordinal))
            {
                return _info.Domain ?? "";
            }

            if (lastLine.StartsWith("Choice", StringComparison.Ordinal))
            {
                string[] lines = trimmed.Split('\n');
                Question = string.Join(" ", lines
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && !l.StartsWith('[') && !l.StartsWith("Choice", StringComparison.Ordinal)));
                Kill();
                return null;
            }

            return null;
        }

        private void Kill()
        {
            try
            {
                _process.Kill(true);
            }
            catch
            {
            }
        }
    }
}
