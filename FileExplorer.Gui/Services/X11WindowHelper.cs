using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FileExplorer.Gui.Services;

public static class X11WindowHelper
{
    private const string LibX11 = "libX11.so.6";
    private const int PropModeReplace = 0;
    private const int ClientMessage = 33;
    private const long PropertyChangeMask = 1L << 22;
    private const int XEventSize = 192;
    private const int ClientMessageDataSize = 20;

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint
    {
        public IntPtr res_name;
        public IntPtr res_class;
    }

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(string? display_name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XInternAtom(IntPtr display, string atom_name, bool only_if_exists);

    [DllImport(LibX11)]
    private static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        ref IntPtr data,
        int nelements);

    [DllImport(LibX11, EntryPoint = "XChangeProperty")]
    private static extern int XChangePropertyBytes(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        byte[] data,
        int nelements);

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr event_mask, IntPtr event_send);

    [DllImport(LibX11)]
    private static extern int XSetClassHint(IntPtr display, IntPtr window, ref XClassHint class_hints);

    public static void ConfigureWindow(IntPtr xid)
    {
        if (!OperatingSystem.IsLinux() || xid == IntPtr.Zero)
        {
            return;
        }

        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr pidAtom = XInternAtom(display, "_NET_WM_PID", false);
            IntPtr cardinalAtom = XInternAtom(display, "CARDINAL", false);
            IntPtr pid = new(Environment.ProcessId);

            if (pidAtom != IntPtr.Zero && cardinalAtom != IntPtr.Zero)
            {
                XChangeProperty(display, xid, pidAtom, cardinalAtom, 32, PropModeReplace, ref pid, 1);
            }

            IntPtr xdndAwareAtom = XInternAtom(display, "XdndAware", false);
            IntPtr atomType = XInternAtom(display, "ATOM", false);
            IntPtr version = new(5);

            if (xdndAwareAtom != IntPtr.Zero && atomType != IntPtr.Zero)
            {
                XChangeProperty(display, xid, xdndAwareAtom, atomType, 32, PropModeReplace, ref version, 1);
            }

            IntPtr userTimeAtom = XInternAtom(display, "_NET_WM_USER_TIME", false);
            IntPtr userTime = new(unchecked((uint) Environment.TickCount));
            if (userTimeAtom != IntPtr.Zero && cardinalAtom != IntPtr.Zero)
            {
                XChangeProperty(display, xid, userTimeAtom, cardinalAtom, 32, PropModeReplace, ref userTime, 1);
            }

            IntPtr resName = Marshal.StringToHGlobalAnsi("fe-file-explorer");
            IntPtr resClass = Marshal.StringToHGlobalAnsi("fe-file-explorer");
            try
            {
                XClassHint hint = new()
                {
                    res_name = resName,
                    res_class = resClass,
                };
                XSetClassHint(display, xid, ref hint);
            }
            finally
            {
                Marshal.FreeHGlobal(resName);
                Marshal.FreeHGlobal(resClass);
            }
        }
        catch
        {
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public static void CompleteStartupNotification(IntPtr xid, string? startupId = null)
    {
        startupId ??= Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID");
        Environment.SetEnvironmentVariable("DESKTOP_STARTUP_ID", null);
        if (!OperatingSystem.IsLinux() || xid == IntPtr.Zero || string.IsNullOrEmpty(startupId))
        {
            return;
        }

        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr startupIdAtom = XInternAtom(display, "_NET_STARTUP_ID", false);
            IntPtr utf8Atom = XInternAtom(display, "UTF8_STRING", false);
            byte[] idBytes = Encoding.UTF8.GetBytes(startupId);
            XChangePropertyBytes(display, xid, startupIdAtom, utf8Atom, 8, PropModeReplace, idBytes, idBytes.Length);

            string escapedId = startupId
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            SendStartupInfoMessage(display, xid, $"remove: ID=\"{escapedId}\"");
        }
        catch
        {
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static void SendStartupInfoMessage(IntPtr display, IntPtr xid, string message)
    {
        IntPtr root = XDefaultRootWindow(display);
        IntPtr beginAtom = XInternAtom(display, "_NET_STARTUP_INFO_BEGIN", false);
        IntPtr continueAtom = XInternAtom(display, "_NET_STARTUP_INFO", false);

        byte[] payload = Encoding.UTF8.GetBytes(message + "\0");
        IntPtr xEvent = Marshal.AllocHGlobal(XEventSize);
        try
        {
            for (int offset = 0; offset < payload.Length; offset += ClientMessageDataSize)
            {
                byte[] zeroed = new byte[XEventSize];
                Marshal.Copy(zeroed, 0, xEvent, XEventSize);

                Marshal.WriteInt32(xEvent, 0, ClientMessage);
                Marshal.WriteInt32(xEvent, 16, 1);
                Marshal.WriteIntPtr(xEvent, 24, display);
                Marshal.WriteIntPtr(xEvent, 32, xid);
                Marshal.WriteIntPtr(xEvent, 40, offset == 0 ? beginAtom : continueAtom);
                Marshal.WriteInt32(xEvent, 48, 8);

                int chunkLength = Math.Min(ClientMessageDataSize, payload.Length - offset);
                Marshal.Copy(payload, offset, xEvent + 56, chunkLength);

                XSendEvent(display, root, false, new IntPtr(PropertyChangeMask), xEvent);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(xEvent);
        }
    }

    public static void UpdateUserTime(IntPtr xid)
    {
        if (!OperatingSystem.IsLinux() || xid == IntPtr.Zero)
        {
            return;
        }

        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr userTimeAtom = XInternAtom(display, "_NET_WM_USER_TIME", false);
            IntPtr cardinalAtom = XInternAtom(display, "CARDINAL", false);
            IntPtr userTime = new(unchecked((uint) Environment.TickCount));

            if (userTimeAtom != IntPtr.Zero && cardinalAtom != IntPtr.Zero)
            {
                XChangeProperty(display, xid, userTimeAtom, cardinalAtom, 32, PropModeReplace, ref userTime, 1);
            }
        }
        catch
        {
        }
        finally
        {
            XCloseDisplay(display);
        }
    }
}
