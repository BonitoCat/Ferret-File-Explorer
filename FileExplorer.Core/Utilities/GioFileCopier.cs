using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public sealed class GioCopyException : IOException
{
    public GioCopyException(string message) : base(message)
    {
    }
}

public static class GioFileCopier
{
    private const string Library = "libgio-2.0.so.0";
    private const int CopyOverwrite = 1;
    private const int ErrorCodeOffset = 4;
    private const int ErrorMessageOffset = 8;
    private const int IoErrorCancelled = 19;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProgressCallback(long currentBytes, long totalBytes, IntPtr userData);

    [DllImport(Library)]
    private static extern IntPtr g_file_new_for_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(Library)]
    private static extern bool g_file_copy(IntPtr source, IntPtr destination, int flags, IntPtr cancellable, ProgressCallback? progress, IntPtr userData, out IntPtr error);

    [DllImport(Library)]
    private static extern bool g_file_delete(IntPtr file, IntPtr cancellable, out IntPtr error);

    [DllImport(Library)]
    private static extern IntPtr g_cancellable_new();

    [DllImport(Library)]
    private static extern void g_cancellable_cancel(IntPtr cancellable);

    [DllImport(Library)]
    private static extern void g_object_unref(IntPtr instance);

    [DllImport(Library)]
    private static extern void g_error_free(IntPtr error);

    public static bool IsGvfsPath(string path)
    {
        return path.StartsWith("/run/user/", StringComparison.Ordinal) && path.Contains("/gvfs/", StringComparison.Ordinal);
    }

    public static bool ShouldUse(string sourcePath, string destinationPath)
    {
        return OperatingSystem.IsLinux() && (IsGvfsPath(sourcePath) || IsGvfsPath(destinationPath)) && GioFileLister.IsAvailable;
    }

    public static void Copy(string sourcePath, string destinationPath, Action<long>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        IntPtr source = g_file_new_for_path(sourcePath);
        IntPtr destination = g_file_new_for_path(destinationPath);
        IntPtr cancellable = g_cancellable_new();
        ProgressCallback? callback = progress == null ? null : (current, _, _) => progress(current);

        try
        {
            using CancellationTokenRegistration registration = token.Register(() => g_cancellable_cancel(cancellable));
            if (g_file_copy(source, destination, CopyOverwrite, cancellable, callback, IntPtr.Zero, out IntPtr error))
            {
                return;
            }

            (int code, string message) = ReadError(error);
            if (code == IoErrorCancelled || token.IsCancellationRequested)
            {
                g_file_delete(destination, IntPtr.Zero, out IntPtr deleteError);
                ReadError(deleteError);
                throw new OperationCanceledException(token);
            }

            throw new GioCopyException(message);
        }
        finally
        {
            GC.KeepAlive(callback);
            g_object_unref(cancellable);
            g_object_unref(destination);
            g_object_unref(source);
        }
    }

    private static (int Code, string Message) ReadError(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return (0, "");
        }

        int code = Marshal.ReadInt32(error, ErrorCodeOffset);
        string message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, ErrorMessageOffset)) ?? "";
        g_error_free(error);
        return (code, message);
    }
}
