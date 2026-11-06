using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Startup;

namespace FileExplorer.Gui.Services;

internal sealed class ResidentServer : IDisposable
{
    private readonly FileStream _sessionLock;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<Func<LaunchRequest, Task>> _handler = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> _handled = new();
    private readonly Queue<string> _recent = new();

    private ResidentServer(FileStream sessionLock, Socket listener)
    {
        _sessionLock = sessionLock;
        _listener = listener;
        _ = ListenAsync();
    }

    public static ResidentServer? TryCreate()
    {
        if (OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Resident startup requires Linux.");
        string directory = LaunchRequest.SessionDirectory;
        if (OperatingSystem.IsLinux())
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else
            Directory.CreateDirectory(directory);
        FileStream sessionLock = new(Path.Combine(directory, "gui.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            sessionLock.Lock(0, 1);
        }
        catch (IOException)
        {
            sessionLock.Dispose();
            return null;
        }

        Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            File.Delete(LaunchRequest.SocketPath);
            listener.Bind(new UnixDomainSocketEndPoint(LaunchRequest.SocketPath));
            listener.Listen(64);
            return new ResidentServer(sessionLock, listener);
        }
        catch
        {
            listener.Dispose();
            sessionLock.Dispose();
            throw;
        }
    }

    public void SetHandler(Func<LaunchRequest, Task> handler) => _handler.SetResult(handler);

    private async Task ListenAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using Socket client = await _listener.AcceptAsync(_stop.Token);
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    using NetworkStream stream = new(client);
                    using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
                    string? json = await reader.ReadLineAsync(timeout.Token);
                    if (json == null) continue;
                    LaunchRequest? request = JsonSerializer.Deserialize(json, LaunchJsonContext.Default.LaunchRequest);
                    if (request == null || string.IsNullOrEmpty(request.Id)) continue;
                    if (!_handled.Contains(request.Id))
                    {
                        Func<LaunchRequest, Task> handler = await _handler.Task.WaitAsync(timeout.Token);
                        await handler(request);
                        _handled.Add(request.Id);
                        _recent.Enqueue(request.Id);
                        if (_recent.Count > 4096) _handled.Remove(_recent.Dequeue());
                    }

                    using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true);
                    await writer.WriteLineAsync(request.Id.AsMemory(), timeout.Token);
                    await writer.FlushAsync(timeout.Token);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or JsonException)
                {
                    // A timed-out starter retries with the same request ID.
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            if (!_stop.IsCancellationRequested) Console.Error.WriteLine(ex);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        File.Delete(LaunchRequest.SocketPath);
        _sessionLock.Dispose();
        _stop.Dispose();
    }
}
