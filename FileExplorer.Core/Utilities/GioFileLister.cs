using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public sealed record GioFileEntry(string Name, string DisplayName, string Uri, string? TargetUri, bool IsDirectory);

public static class GioFileLister
{
    private const string Library = "libgio-2.0.so.0";
    private const string Attributes = "standard::name,standard::display-name,standard::target-uri,standard::type";
    private const int FileTypeDirectory = 2;
    private const int FileTypeMountable = 6;
    private const int FileTypeShortcut = 5;

    [DllImport(Library)]
    private static extern IntPtr g_file_new_for_uri([MarshalAs(UnmanagedType.LPUTF8Str)] string uri);

    [DllImport(Library)]
    private static extern IntPtr g_file_enumerate_children(IntPtr file, [MarshalAs(UnmanagedType.LPUTF8Str)] string attributes, int flags, IntPtr cancellable, out IntPtr error);

    [DllImport(Library)]
    private static extern IntPtr g_file_enumerator_next_file(IntPtr enumerator, IntPtr cancellable, out IntPtr error);

    [DllImport(Library)]
    private static extern IntPtr g_file_enumerator_get_child(IntPtr enumerator, IntPtr info);

    [DllImport(Library)]
    private static extern bool g_file_enumerator_close(IntPtr enumerator, IntPtr cancellable, out IntPtr error);

    [DllImport(Library)]
    private static extern IntPtr g_file_get_uri(IntPtr file);

    [DllImport(Library)]
    private static extern IntPtr g_file_info_get_name(IntPtr info);

    [DllImport(Library)]
    private static extern IntPtr g_file_info_get_display_name(IntPtr info);

    [DllImport(Library)]
    private static extern IntPtr g_file_info_get_attribute_string(IntPtr info, [MarshalAs(UnmanagedType.LPUTF8Str)] string attribute);

    [DllImport(Library)]
    private static extern int g_file_info_get_file_type(IntPtr info);

    [DllImport(Library)]
    private static extern void g_object_unref(IntPtr instance);

    [DllImport(Library)]
    private static extern void g_error_free(IntPtr error);

    [DllImport(Library)]
    private static extern void g_free(IntPtr memory);

    public static bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsLinux())
            {
                return false;
            }

            return NativeLibrary.TryLoad(Library, out IntPtr handle) && handle != IntPtr.Zero;
        }
    }

    public static List<GioFileEntry> List(string uri, out string? error)
    {
        error = null;
        List<GioFileEntry> entries = new();
        if (!IsAvailable)
        {
            error = "GIO is not available";
            return entries;
        }

        IntPtr file = g_file_new_for_uri(uri);
        IntPtr enumerator = g_file_enumerate_children(file, Attributes, 0, IntPtr.Zero, out IntPtr enumerateError);
        if (enumerator == IntPtr.Zero)
        {
            error = ReadError(enumerateError);
            g_object_unref(file);
            return entries;
        }

        while (true)
        {
            IntPtr info = g_file_enumerator_next_file(enumerator, IntPtr.Zero, out IntPtr nextError);
            if (info == IntPtr.Zero)
            {
                if (nextError != IntPtr.Zero)
                {
                    error = ReadError(nextError);
                }

                break;
            }

            entries.Add(ReadEntry(enumerator, info));
            g_object_unref(info);
        }

        g_file_enumerator_close(enumerator, IntPtr.Zero, out IntPtr closeError);
        if (closeError != IntPtr.Zero)
        {
            g_error_free(closeError);
        }

        g_object_unref(enumerator);
        g_object_unref(file);
        return entries;
    }

    private static GioFileEntry ReadEntry(IntPtr enumerator, IntPtr info)
    {
        string name = Marshal.PtrToStringUTF8(g_file_info_get_name(info)) ?? "";
        string displayName = Marshal.PtrToStringUTF8(g_file_info_get_display_name(info)) ?? name;
        string? targetUri = Marshal.PtrToStringUTF8(g_file_info_get_attribute_string(info, "standard::target-uri"));
        int fileType = g_file_info_get_file_type(info);

        IntPtr child = g_file_enumerator_get_child(enumerator, info);
        IntPtr childUri = g_file_get_uri(child);
        string uri = Marshal.PtrToStringUTF8(childUri) ?? "";
        g_free(childUri);
        g_object_unref(child);

        bool isDirectory = fileType is FileTypeDirectory or FileTypeMountable or FileTypeShortcut;
        return new(name, displayName, uri, string.IsNullOrEmpty(targetUri) ? null : targetUri, isDirectory);
    }

    private static string ReadError(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return "";
        }

        IntPtr messagePtr = Marshal.ReadIntPtr(error, 8);
        string message = Marshal.PtrToStringUTF8(messagePtr) ?? "";
        g_error_free(error);
        return message;
    }
}
