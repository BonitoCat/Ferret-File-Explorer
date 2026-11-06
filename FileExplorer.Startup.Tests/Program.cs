using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FileExplorer.Gui.Services;
using FileExplorer.Startup;

if (args.Contains("--lock-probe"))
{
    using ResidentServer? contender = ResidentServer.TryCreate();
    return contender == null ? 0 : 1;
}

string runtime = Path.Combine(Path.GetTempPath(), "ft-" + Guid.NewGuid().ToString("N")[..8]);
Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtime);
Directory.CreateDirectory(runtime);
try
{
    Check(LaunchRequest.IsPicker(["--file-picker"]), "picker bypass");
    Check(LaunchRequest.IsPicker(["--directory"]), "picker aliases");
    Check(LaunchRequest.IsPicker(["--SAVE"]), "picker flags are case insensitive");
    Check(!LaunchRequest.IsPicker(["--background"]), "background is not a picker");
    string firstSession = LaunchRequest.SocketPath;
    string? display = Environment.GetEnvironmentVariable("DISPLAY");
    Environment.SetEnvironmentVariable("DISPLAY", "ferret-test-another-session");
    Check(firstSession != LaunchRequest.SocketPath, "separate graphical sessions");
    Environment.SetEnvironmentVariable("DISPLAY", display);

    LaunchRequest absent = new();
    Check(!await absent.TrySendAsync(), "absent server returns promptly");
    ConcurrentQueue<LaunchRequest> received = new();
    using (ResidentServer server = ResidentServer.TryCreate() ?? throw new Exception("Cannot own session"))
    {
        LaunchRequest early = new() { Args = ["--background"] };
        Task<bool> timedOut = early.TrySendAsync(50);
        await Task.Delay(100);
        Check(!await timedOut, "starter can time out before UI readiness");
        Task<bool> pending = early.TrySendAsync(5000);
        await Task.Delay(100);
        Check(!pending.IsCompleted, "request waits for UI readiness");
        server.SetHandler(request =>
        {
            received.Enqueue(request);
            return Task.CompletedTask;
        });
        Check(await pending, "queued request acknowledged after readiness");
        Check(received.Count(r => r.Id == early.Id) == 1, "readiness retry delivered exactly once");

        LaunchRequest request = new()
        {
            Args = ["--path", "relative folder/with spaces"],
            WorkingDirectory = Path.GetTempPath(), StartupId = "test-startup", ActivationToken = "test-token",
        };
        Check(await request.TrySendAsync(5000), "request acknowledged");
        Check(await request.TrySendAsync(5000), "retry acknowledged");
        Check(received.Count(r => r.Id == request.Id) == 1, "retry does not duplicate a window");
        LaunchRequest delivered = received.Single(r => r.Id == request.Id);
        Check(delivered.Args.SequenceEqual(request.Args) && delivered.WorkingDirectory == request.WorkingDirectory &&
              delivered.StartupId == request.StartupId && delivered.ActivationToken == request.ActivationToken,
            "paths and activation metadata preserved");

        LaunchRequest disconnected = new();
        using (Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(LaunchRequest.SocketPath));
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(disconnected, LaunchJsonContext.Default.LaunchRequest) + "\n");
            await socket.SendAsync(bytes, SocketFlags.None);
        }
        Check(await disconnected.TrySendAsync(5000), "lost acknowledgement can be retried");
        Check(received.Count(r => r.Id == disconnected.Id) == 1, "lost acknowledgement does not duplicate request");

        using (Socket malformed = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            await malformed.ConnectAsync(new UnixDomainSocketEndPoint(LaunchRequest.SocketPath));
            await malformed.SendAsync(Encoding.UTF8.GetBytes("invalid JSON\n"), SocketFlags.None);
        }
        Check(await new LaunchRequest().TrySendAsync(5000), "malformed connection does not stop the server");

        Task<bool>[] burst = Enumerable.Range(0, 20)
            .Select(_ => new LaunchRequest().TrySendAsync(5000)).ToArray();
        Check((await Task.WhenAll(burst)).All(success => success), "concurrent opening requests acknowledged");

        ProcessStartInfo probe = new(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            probe.ArgumentList.Add(typeof(LaunchRequest).Assembly.Location);
        probe.ArgumentList.Add("--lock-probe");
        using Process child = Process.Start(probe)!;
        await child.WaitForExitAsync();
        Check(child.ExitCode == 0, "another process cannot take the session lock");
    }

    Check(!File.Exists(LaunchRequest.SocketPath), "shutdown removes socket");
    // A crashed process can leave a socket path behind; the next owner replaces it.
    await File.WriteAllTextAsync(LaunchRequest.SocketPath, "stale socket");
    using (ResidentServer restarted = ResidentServer.TryCreate() ?? throw new Exception("Cannot restart"))
    {
        restarted.SetHandler(_ => Task.CompletedTask);
        Check(await new LaunchRequest().TrySendAsync(5000), "restart replaces stale socket and releases lock");
    }
    Console.WriteLine("All startup integration checks passed.");
    return 0;
}
finally
{
    Directory.Delete(runtime, true);
}

static void Check(bool result, string description)
{
    if (!result) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}
