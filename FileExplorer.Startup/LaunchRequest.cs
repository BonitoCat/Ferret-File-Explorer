using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Startup;

public sealed class LaunchRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string[] Args { get; set; } = [];
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public string? StartupId { get; set; } = Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID");
    public string? ActivationToken { get; set; } = Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN");

    public static bool IsPicker(string[] args) => args.Any(arg => arg.ToLowerInvariant() is
        "--file-picker" or "--file-selection" or "--file" or "--folder-picker" or
        "--directory" or "--folder" or "--save-picker" or "--save");

    public static string SessionDirectory
    {
        get
        {
            string session = string.Join("|", Environment.UserName,
                Environment.GetEnvironmentVariable("XDG_SESSION_ID"),
                Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
                Environment.GetEnvironmentVariable("DISPLAY"));
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session)))[..16];
            string root = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? Path.GetTempPath();
            return Path.Combine(root, "ferret-" + hash);
        }
    }

    public static string SocketPath => Path.Combine(SessionDirectory, "gui.sock");

    public async Task<bool> TrySendAsync(int timeoutMs = 250)
    {
        using CancellationTokenSource timeout = new(timeoutMs);
        try
        {
            using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), timeout.Token);
            using NetworkStream stream = new(socket);
            using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(this, LaunchJsonContext.Default.LaunchRequest).AsMemory(), timeout.Token);
            await writer.FlushAsync(timeout.Token);
            using StreamReader reader = new(stream);
            return await reader.ReadLineAsync(timeout.Token) == Id;
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            return false;
        }
    }
}

[JsonSerializable(typeof(LaunchRequest))]
internal partial class LaunchJsonContext : JsonSerializerContext;
