using Tmds.DBus.Protocol;

namespace FileExplorer.Portal;

public sealed class RequestHandle : IPathMethodHandler
{
    private const string InterfaceName = "org.freedesktop.impl.portal.Request";

    private readonly CancellationTokenSource _cts;

    public RequestHandle(string path, CancellationTokenSource cts)
    {
        Path = path;
        _cts = cts;
    }

    public string Path { get; }

    public bool HandlesChildPaths => false;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        Message request = context.Request;
        if (request.InterfaceAsString != InterfaceName || request.MemberAsString != "Close")
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        using MessageWriter writer = context.CreateReplyWriter("");
        context.Reply(writer.CreateMessage());
        return ValueTask.CompletedTask;
    }
}
