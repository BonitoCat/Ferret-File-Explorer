using System.Text;
using Tmds.DBus.Protocol;

namespace FileExplorer.Portal;

public sealed class FileChooserPortal : IPathMethodHandler
{
    private const string InterfaceName = "org.freedesktop.impl.portal.FileChooser";
    private const uint ResponseSuccess = 0;
    private const uint ResponseCancelled = 1;
    private const uint ResponseOther = 2;

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes(
        """
        <interface name="org.freedesktop.impl.portal.FileChooser">
          <method name="OpenFile">
            <arg type="o" name="handle" direction="in"/>
            <arg type="s" name="app_id" direction="in"/>
            <arg type="s" name="parent_window" direction="in"/>
            <arg type="s" name="title" direction="in"/>
            <arg type="a{sv}" name="options" direction="in"/>
            <arg type="u" name="response" direction="out"/>
            <arg type="a{sv}" name="results" direction="out"/>
          </method>
          <method name="SaveFile">
            <arg type="o" name="handle" direction="in"/>
            <arg type="s" name="app_id" direction="in"/>
            <arg type="s" name="parent_window" direction="in"/>
            <arg type="s" name="title" direction="in"/>
            <arg type="a{sv}" name="options" direction="in"/>
            <arg type="u" name="response" direction="out"/>
            <arg type="a{sv}" name="results" direction="out"/>
          </method>
          <method name="SaveFiles">
            <arg type="o" name="handle" direction="in"/>
            <arg type="s" name="app_id" direction="in"/>
            <arg type="s" name="parent_window" direction="in"/>
            <arg type="s" name="title" direction="in"/>
            <arg type="a{sv}" name="options" direction="in"/>
            <arg type="u" name="response" direction="out"/>
            <arg type="a{sv}" name="results" direction="out"/>
          </method>
        </interface>
        """);

    private readonly DBusConnection _connection;
    private readonly PickerLauncher _launcher;

    public FileChooserPortal(DBusConnection connection, PickerLauncher launcher)
    {
        _connection = connection;
        _launcher = launcher;
    }

    public string Path => "/org/freedesktop/portal/desktop";

    public bool HandlesChildPaths => false;

    public async ValueTask HandleMethodAsync(MethodContext context)
    {
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml([IntrospectionXml], ReadOnlySpan<string>.Empty);
            return;
        }

        PortalRequest? request = ParseRequest(context.Request);
        if (request == null)
        {
            context.ReplyUnknownMethodError();
            return;
        }

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        _connection.AddMethodHandler(new RequestHandle(request.Handle, cts));

        List<string>? paths;
        try
        {
            paths = await _launcher.RunAsync(request.Arguments, cts.Token);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Reply(context, ResponseOther, null);
            return;
        }
        finally
        {
            _connection.RemoveMethodHandler(request.Handle);
        }

        if (paths == null)
        {
            Reply(context, ResponseCancelled, null);
            return;
        }

        if (request.SaveFileNames.Count > 0)
        {
            paths = request.SaveFileNames.Select(name => System.IO.Path.Combine(paths[0], name)).ToList();
        }

        Reply(context, ResponseSuccess, paths.Select(FileUri.FromPath).ToArray());
    }

    private static PortalRequest? ParseRequest(Message message)
    {
        if (message.InterfaceAsString != InterfaceName)
        {
            return null;
        }

        string? member = message.MemberAsString;
        if (member is not ("OpenFile" or "SaveFile" or "SaveFiles"))
        {
            return null;
        }

        Reader reader = message.GetBodyReader();
        string handle = reader.ReadObjectPathAsString();
        reader.ReadString();
        reader.ReadString();
        string title = reader.ReadString();
        Dictionary<string, VariantValue> options = reader.ReadDictionaryOfStringToVariantValue();

        List<string> arguments = new();
        List<string> saveFileNames = new();
        bool directory = GetBool(options, "directory");

        switch (member)
        {
            case "OpenFile":
                arguments.Add(directory ? "--folder-picker" : "--file-picker");
                if (GetBool(options, "multiple"))
                {
                    arguments.Add("--multiple");
                }

                AddPath(arguments, GetBytePath(options, "current_folder"));
                break;

            case "SaveFile":
                arguments.Add("--save-picker");
                string? currentFile = GetBytePath(options, "current_file");
                string? currentName = GetString(options, "current_name");
                AddPath(arguments, currentFile != null ? System.IO.Path.GetDirectoryName(currentFile) : GetBytePath(options, "current_folder"));
                currentName ??= currentFile != null ? System.IO.Path.GetFileName(currentFile) : null;
                if (!string.IsNullOrEmpty(currentName))
                {
                    arguments.Add("--name");
                    arguments.Add(currentName);
                }

                break;

            default:
                arguments.Add("--folder-picker");
                AddPath(arguments, GetBytePath(options, "current_folder"));
                saveFileNames.AddRange(GetBytePaths(options, "files").Select(System.IO.Path.GetFileName).OfType<string>());
                break;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            arguments.Add("--title");
            arguments.Add(title);
        }

        string? acceptLabel = GetString(options, "accept_label");
        if (!string.IsNullOrWhiteSpace(acceptLabel))
        {
            arguments.Add("--accept-label");
            arguments.Add(StripMnemonic(acceptLabel));
        }

        if (member != "SaveFiles" && !directory)
        {
            arguments.AddRange(PortalFilters.ToArguments(options));
        }

        return new(handle, arguments, saveFileNames);
    }

    private static void Reply(MethodContext context, uint response, string[]? uris)
    {
        using MessageWriter writer = context.CreateReplyWriter("ua{sv}");
        writer.WriteUInt32(response);
        ArrayStart results = writer.WriteDictionaryStart();
        if (uris != null)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("uris");
            writer.WriteVariant(VariantValue.Array(uris));
        }

        writer.WriteDictionaryEnd(results);
        context.Reply(writer.CreateMessage());
    }

    private static void AddPath(List<string> arguments, string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return;
        }

        arguments.Add("--path");
        arguments.Add(path);
    }

    private static bool GetBool(Dictionary<string, VariantValue> options, string key)
    {
        return options.TryGetValue(key, out VariantValue value) && value.Type == VariantValueType.Bool && value.GetBool();
    }

    private static string? GetString(Dictionary<string, VariantValue> options, string key)
    {
        return options.TryGetValue(key, out VariantValue value) && value.Type == VariantValueType.String ? value.GetString() : null;
    }

    private static string? GetBytePath(Dictionary<string, VariantValue> options, string key)
    {
        if (!options.TryGetValue(key, out VariantValue value) || value.Type != VariantValueType.Array)
        {
            return null;
        }

        string path = Encoding.UTF8.GetString(value.GetArray<byte>()).TrimEnd('\0');
        return path.Length > 0 ? path : null;
    }

    private static List<string> GetBytePaths(Dictionary<string, VariantValue> options, string key)
    {
        List<string> paths = new();
        if (!options.TryGetValue(key, out VariantValue value) || value.Type != VariantValueType.Array)
        {
            return paths;
        }

        for (int i = 0; i < value.Count; i++)
        {
            string path = Encoding.UTF8.GetString(value.GetItem(i).GetArray<byte>()).TrimEnd('\0');
            if (path.Length > 0)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static string StripMnemonic(string label)
    {
        return label.Replace("__", "\u0001").Replace("_", "").Replace("\u0001", "_");
    }

    private sealed record PortalRequest(string Handle, List<string> Arguments, List<string> SaveFileNames);
}
