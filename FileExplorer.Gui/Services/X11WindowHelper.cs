using System;
using System.Runtime.InteropServices;

namespace FileExplorer.Gui.Services;

public static class X11WindowHelper
{
    private const string LibX11 = "libX11.so.6";
    private const int PropModeReplace = 0;

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
