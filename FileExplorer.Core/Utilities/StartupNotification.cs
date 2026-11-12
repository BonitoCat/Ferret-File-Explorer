using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public static class StartupNotification
{
    private const string Library = "libX11.so.6";
    private const long PropertyChangeMask = 1L << 22;
    private const int PropertyNotify = 28;
    private const int PropModeReplace = 0;
    private const int XEventSize = 192;
    private const int TimeOffset = 48;
    private const int TypeOffset = 0;

    [DllImport(Library)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(Library)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(Library)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(Library)]
    private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, ulong border, ulong background);

    [DllImport(Library)]
    private static extern int XDestroyWindow(IntPtr display, IntPtr window);

    [DllImport(Library)]
    private static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr eventMask);

    [DllImport(Library)]
    private static extern IntPtr XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string atomName, bool onlyIfExists);

    [DllImport(Library)]
    private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr data, int elementCount);

    [DllImport(Library)]
    private static extern int XWindowEvent(IntPtr display, IntPtr window, IntPtr eventMask, IntPtr eventReturn);

    public static void Apply(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? id = CreateId();
        if (id == null)
        {
            psi.EnvironmentVariables.Remove("DESKTOP_STARTUP_ID");
            return;
        }

        psi.EnvironmentVariables["DESKTOP_STARTUP_ID"] = id;
    }

    public static string? CreateId()
    {
        uint? time = GetXServerTime();
        if (time == null)
        {
            return null;
        }

        return $"ferret_{Environment.ProcessId}_TIME{time.Value}";
    }

    private static uint? GetXServerTime()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return null;
        }

        IntPtr display = IntPtr.Zero;
        IntPtr window = IntPtr.Zero;
        IntPtr eventBuffer = IntPtr.Zero;

        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return null;
            }

            IntPtr root = XDefaultRootWindow(display);
            window = XCreateSimpleWindow(display, root, 0, 0, 1, 1, 0, 0, 0);
            if (window == IntPtr.Zero)
            {
                return null;
            }

            XSelectInput(display, window, (IntPtr) PropertyChangeMask);

            IntPtr atom = XInternAtom(display, "_FERRET_TIMESTAMP_PROP", false);
            XChangeProperty(display, window, atom, atom, 8, PropModeReplace, IntPtr.Zero, 0);

            eventBuffer = Marshal.AllocHGlobal(XEventSize);
            XWindowEvent(display, window, (IntPtr) PropertyChangeMask, eventBuffer);
            if (Marshal.ReadInt32(eventBuffer, TypeOffset) != PropertyNotify)
            {
                return null;
            }

            return (uint) Marshal.ReadInt64(eventBuffer, TimeOffset);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (eventBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(eventBuffer);
            }

            if (window != IntPtr.Zero)
            {
                XDestroyWindow(display, window);
            }

            if (display != IntPtr.Zero)
            {
                XCloseDisplay(display);
            }
        }
    }
}
