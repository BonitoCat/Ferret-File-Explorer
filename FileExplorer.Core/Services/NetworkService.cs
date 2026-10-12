using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        if (string.IsNullOrWhiteSpace(info.Host))
        {
            return (false, "", "Server hostname or IP address cannot be empty.");
        }

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

        Process process = new() { StartInfo = startInfo };
        StringBuilder outputBuilder = new();
        StringBuilder errorBuilder = new();

        try
        {
            process.Start();

            Task readOutTask = Task.Run(async () =>
            {
                char[] buffer = new char[512];
                int read;
                while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string chunk = new(buffer, 0, read);
                    outputBuilder.Append(chunk);
                    await HandleInteractivePromptAsync(process.StandardInput, chunk, info);
                }
            }, cancellationToken);

            Task readErrTask = Task.Run(async () =>
            {
                char[] buffer = new char[512];
                int read;
                while ((read = await process.StandardError.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string chunk = new(buffer, 0, read);
                    errorBuilder.Append(chunk);
                    await HandleInteractivePromptAsync(process.StandardInput, chunk, info);
                }
            }, cancellationToken);

            Task feedInputTask = Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(info.Password))
                    {
                        await Task.Delay(250, cancellationToken);
                        if (!process.HasExited)
                        {
                            await process.StandardInput.WriteLineAsync(info.Password);
                            await process.StandardInput.FlushAsync();
                        }
                    }
                }
                catch
                {
                }
            }, cancellationToken);

            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(35), cancellationToken);
            Task completedTask = await Task.WhenAny(Task.WhenAll(process.WaitForExitAsync(cancellationToken), readOutTask, readErrTask, feedInputTask), timeoutTask);

            if (completedTask == timeoutTask)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                return (false, "", "Connection timed out.");
            }
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }

        if (process.ExitCode != 0)
        {
            string err = errorBuilder.ToString().Trim();
            if (string.IsNullOrEmpty(err))
            {
                err = outputBuilder.ToString().Trim();
            }

            if (string.IsNullOrEmpty(err))
            {
                err = $"Mount process exited with code {process.ExitCode}";
            }

            return (false, "", err);
        }

        await Task.Delay(200, cancellationToken);
        HashSet<string> afterMounts = GetGvfsMounts();
        string? newMount = afterMounts.Except(beforeMounts).FirstOrDefault();

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

    private static async Task HandleInteractivePromptAsync(StreamWriter writer, string chunk, NetworkConnectionInfo info)
    {
        try
        {
            if (chunk.Contains("(yes/no", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("yes");
                await writer.FlushAsync();

                if (!string.IsNullOrEmpty(info.Password))
                {
                    await Task.Delay(200);
                    await writer.WriteLineAsync(info.Password);
                    await writer.FlushAsync();
                }

                return;
            }

            if (chunk.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                chunk.Contains("passphrase", StringComparison.OrdinalIgnoreCase) ||
                chunk.Contains("passwort", StringComparison.OrdinalIgnoreCase) ||
                chunk.Contains("kennwort", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(info.Password))
                {
                    await writer.WriteLineAsync(info.Password);
                    await writer.FlushAsync();
                }

                return;
            }

            if (chunk.Contains("user", StringComparison.OrdinalIgnoreCase) ||
                chunk.Contains("benutzer", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(info.Username))
                {
                    await writer.WriteLineAsync(info.Username);
                    await writer.FlushAsync();
                }
                return;
            }

            if (chunk.Contains("domain", StringComparison.OrdinalIgnoreCase) ||
                chunk.Contains("domäne", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(info.Domain))
                {
                    await writer.WriteLineAsync(info.Domain);
                    await writer.FlushAsync();
                }
                return;
            }
        }
        catch
        {
        }
    }
}
