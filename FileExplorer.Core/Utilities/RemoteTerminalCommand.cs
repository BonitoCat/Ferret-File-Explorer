namespace FileExplorer.Core.Utilities;

public sealed class RemoteTerminalSession
{
    public required string[] Command { get; init; }
    public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);
}

public static class RemoteTerminalCommand
{
    private const string PasswordVariable = "FE_REMOTE_PASSWORD";
    private const string CadaverNetrcScript = "dir=$(mktemp -d) || exit 1; trap 'rm -rf \"$dir\"' EXIT; umask 077; printf 'machine %s\\nlogin %s\\npassword %s\\n' \"$2\" \"$3\" \"$FE_REMOTE_PASSWORD\" > \"$dir/.netrc\"; unset FE_REMOTE_PASSWORD; HOME=\"$dir\" cadaver \"$1\"";

    public static string[]? Build(string path, Func<string, bool> isProgramAvailable)
    {
        return Build(path, isProgramAvailable, out _);
    }

    public static string[]? Build(string path, Func<string, bool> isProgramAvailable, out string? missingProgram)
    {
        return BuildSession(path, null, isProgramAvailable, out missingProgram)?.Command;
    }

    public static RemoteTerminalSession? BuildSession(string path, string? password, Func<string, bool> isProgramAvailable, out string? missingProgram)
    {
        missingProgram = null;
        string? uriText = GvfsPath.ToUri(path);
        if (uriText == null || !Uri.TryCreate(uriText, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string remotePath = Uri.UnescapeDataString(uri.AbsolutePath);
        string user = Uri.UnescapeDataString(uri.UserInfo);
        string host = uri.IdnHost;
        int port = uri.Port;
        string? secret = string.IsNullOrEmpty(password) ? null : password;

        missingProgram = uri.Scheme switch
        {
            "sftp" => "ssh",
            "ftp" or "ftps" => "lftp",
            "dav" or "davs" => "cadaver",
            "smb" => "smbclient",
            _ => null,
        };

        RemoteTerminalSession? session = uri.Scheme switch
        {
            "sftp" => BuildSsh(host, user, port, remotePath, secret, isProgramAvailable),
            "ftp" or "ftps" => BuildLftp(uri.AbsoluteUri, user, secret, isProgramAvailable),
            "dav" or "davs" => BuildDav(uri, user, secret, isProgramAvailable),
            "smb" => BuildSmb(host, user, port, remotePath, secret, isProgramAvailable),
            _ => null,
        };

        if (session != null)
        {
            missingProgram = null;
        }

        return session;
    }

    private static RemoteTerminalSession? BuildSsh(string host, string user, int port, string remotePath, string? password, Func<string, bool> isProgramAvailable)
    {
        if (!isProgramAvailable("ssh"))
        {
            return null;
        }

        bool usePass = password != null && isProgramAvailable("sshpass");
        List<string> command = usePass ? ["sshpass", "-e", "ssh", "-t"] : ["ssh", "-t"];
        if (port > 0)
        {
            command.Add("-p");
            command.Add(port.ToString());
        }

        command.Add(user.Length > 0 ? $"{user}@{host}" : host);
        command.Add($"cd {QuoteForShell(remotePath)} 2>/dev/null; exec \"${{SHELL:-/bin/sh}}\" -l");

        RemoteTerminalSession session = new() { Command = command.ToArray() };
        if (usePass)
        {
            session.Environment["SSHPASS"] = password!;
        }

        return session;
    }

    private static RemoteTerminalSession? BuildLftp(string url, string user, string? password, Func<string, bool> isProgramAvailable)
    {
        if (!isProgramAvailable("lftp"))
        {
            return null;
        }

        if (password == null || user.Length == 0)
        {
            return new() { Command = ["lftp", url] };
        }

        RemoteTerminalSession session = new() { Command = ["lftp", "-u", user, "--env-password", url] };
        session.Environment["LFTP_PASSWORD"] = password;
        return session;
    }

    private static RemoteTerminalSession? BuildDav(Uri uri, string user, string? password, Func<string, bool> isProgramAvailable)
    {
        UriBuilder builder = new(uri)
        {
            Scheme = uri.Scheme == "davs" ? "https" : "http",
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            UserName = "",
            Password = "",
        };
        string url = builder.Uri.AbsoluteUri;
        bool hasCredentials = password != null && user.Length > 0;

        if (isProgramAvailable("cadaver"))
        {
            if (!hasCredentials)
            {
                return new() { Command = ["cadaver", url] };
            }

            RemoteTerminalSession session = new() { Command = ["bash", "-c", CadaverNetrcScript, "bash", url, uri.IdnHost, user] };
            session.Environment[PasswordVariable] = password!;
            return session;
        }

        return BuildLftp(url, user, password, isProgramAvailable);
    }

    private static RemoteTerminalSession? BuildSmb(string host, string user, int port, string remotePath, string? password, Func<string, bool> isProgramAvailable)
    {
        if (!isProgramAvailable("smbclient"))
        {
            return null;
        }

        string[] segments = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        List<string> command = ["smbclient", $"//{host}/{segments[0]}"];
        if (user.Length > 0)
        {
            command.Add("-U");
            command.Add(user.Replace(';', '\\'));
        }

        if (port > 0)
        {
            command.Add("-p");
            command.Add(port.ToString());
        }

        if (segments.Length > 1)
        {
            command.Add("-D");
            command.Add(string.Join('/', segments.Skip(1)));
        }

        RemoteTerminalSession session = new() { Command = command.ToArray() };
        if (password != null)
        {
            session.Environment["PASSWD"] = password;
        }

        return session;
    }

    private static string QuoteForShell(string value)
    {
        return "'" + value.Replace("'", "'\\''") + "'";
    }
}
