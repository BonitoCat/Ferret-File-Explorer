using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorer.Gui.Services;

public static class X11WindowHelper
{
    private const string LibX11 = "libX11.so.6";
    private const int PropModeReplace = 0;
    private const int ClientMessage = 33;
    private const long PropertyChangeMask = 1L << 22;
    private const int XEventSize = 192;
    private const int ClientMessageDataSize = 20;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const int PagerSourceIndication = 2;
    private const int NewWindowPollIntervalMs = 100;
    private const int NewWindowTimeoutMs = 5000;

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
    private static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr long_offset,
        IntPtr long_length,
        bool delete,
        IntPtr req_type,
        out IntPtr actual_type_return,
        out int actual_format_return,
        out IntPtr nitems_return,
        out IntPtr bytes_after_return,
        out IntPtr prop_return);

    [DllImport(LibX11)]
    private static extern int XFree(IntPtr data);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

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

    public static void CompleteStartupNotification(IntPtr xid)
    {
        string? startupId = Environment.GetEnvironmentVariable("DESKTOP_STARTUP_ID");
        Environment.SetEnvironmentVariable("DESKTOP_STARTUP_ID", null);
        Environment.SetEnvironmentVariable("XDG_ACTIVATION_TOKEN", null);
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

    public static HashSet<IntPtr> GetClientWindows()
    {
        HashSet<IntPtr> windows = new();
        if (!OperatingSystem.IsLinux())
        {
            return windows;
        }

        IntPtr display = XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            return windows;
        }

        try
        {
            ReadClientWindows(display, windows);
        }
        catch
        {
        }
        finally
        {
            XCloseDisplay(display);
        }

        return windows;
    }

    public static void ActivateNextNewWindow(HashSet<IntPtr> existingWindows)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Task.Run(() =>
        {
            IntPtr display = XOpenDisplay(null);
            if (display == IntPtr.Zero)
            {
                return;
            }

            try
            {
                for (int waited = 0; waited < NewWindowTimeoutMs; waited += NewWindowPollIntervalMs)
                {
                    Thread.Sleep(NewWindowPollIntervalMs);

                    HashSet<IntPtr> current = new();
                    ReadClientWindows(display, current);
                    current.ExceptWith(existingWindows);
                    foreach (IntPtr window in current)
                    {
                        SendActivateRequest(display, window);
                        return;
                    }
                }
            }
            catch
            {
            }
            finally
            {
                XCloseDisplay(display);
            }
        });
    }

    private static void ReadClientWindows(IntPtr display, HashSet<IntPtr> windows)
    {
        IntPtr clientListAtom = XInternAtom(display, "_NET_CLIENT_LIST", false);
        int status = XGetWindowProperty(display, XDefaultRootWindow(display), clientListAtom, IntPtr.Zero, new IntPtr(4096), false, IntPtr.Zero, out _, out int format, out IntPtr count, out _, out IntPtr data);
        if (status != 0 || data == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (format != 32)
            {
                return;
            }

            for (int i = 0; i < (int) count; i++)
            {
                windows.Add(Marshal.ReadIntPtr(data, i * IntPtr.Size));
            }
        }
        finally
        {
            XFree(data);
        }
    }

    private static void SendActivateRequest(IntPtr display, IntPtr window)
    {
        IntPtr activeWindowAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
        IntPtr xEvent = Marshal.AllocHGlobal(XEventSize);
        try
        {
            Marshal.Copy(new byte[XEventSize], 0, xEvent, XEventSize);
            Marshal.WriteInt32(xEvent, 0, ClientMessage);
            Marshal.WriteInt32(xEvent, 16, 1);
            Marshal.WriteIntPtr(xEvent, 24, display);
            Marshal.WriteIntPtr(xEvent, 32, window);
            Marshal.WriteIntPtr(xEvent, 40, activeWindowAtom);
            Marshal.WriteInt32(xEvent, 48, 32);
            Marshal.WriteInt64(xEvent, 56, PagerSourceIndication);

            XSendEvent(display, XDefaultRootWindow(display), false, new IntPtr(SubstructureRedirectMask | SubstructureNotifyMask), xEvent);
            XFlush(display);
        }
        finally
        {
            Marshal.FreeHGlobal(xEvent);
        }
    }
}
