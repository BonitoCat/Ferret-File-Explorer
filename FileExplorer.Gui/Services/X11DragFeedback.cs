using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui.Services;

public static class X11DragFeedback
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
    private const string LibXcursor = "libXcursor.so.1";
    private const int ShapeInput = 2;
    private const uint ShiftMask = 1;
    private const uint ControlMask = 4;
    private const uint AltMask = 8;
    private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly string[] NoDropCursorNames = ["dnd-no-drop", "no-drop", "not-allowed", "forbidden", "dnd-none", "crossed_circle"];

    private static DragPreviewWindow? _previewWindow;
    private static DispatcherTimer? _timer;
    private static IntPtr _display;
    private static IntPtr _previewXid;
    private static int _previewWidth;
    private static int _previewHeight;

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XQueryPointer(
        IntPtr display,
        IntPtr w,
        out IntPtr root_return,
        out IntPtr child_return,
        out int root_x_return,
        out int root_y_return,
        out int win_x_return,
        out int win_y_return,
        out uint mask_return);

    [DllImport(LibX11)]
    private static extern int XMoveWindow(IntPtr display, IntPtr window, int x, int y);

    [DllImport(LibX11)]
    private static extern int XRaiseWindow(IntPtr display, IntPtr window);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    [DllImport(LibXext)]
    private static extern void XShapeCombineRectangles(
        IntPtr display,
        IntPtr window,
        int dest_kind,
        int x_off,
        int y_off,
        IntPtr rectangles,
        int n_rects,
        int op,
        int ordering);

    [DllImport(LibXcursor)]
    private static extern IntPtr XcursorLibraryLoadCursor(IntPtr display, string name);

    public static void FixNoDropCursor()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        object? locator = typeof(AvaloniaLocator).GetProperty("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
        object? cursorFactory = locator?.GetType().GetMethod("GetService", [typeof(Type)])?.Invoke(locator, [typeof(ICursorFactory)]);
        Type? factoryType = cursorFactory?.GetType();
        FieldInfo? displayField = factoryType?.GetField("_display", NonPublicInstance);
        FieldInfo? cursorField = factoryType?.GetField("<DragNoDropCursorHandle>k__BackingField", NonPublicInstance);
        if (cursorFactory == null || displayField?.GetValue(cursorFactory) is not IntPtr display || display == IntPtr.Zero || cursorField == null)
        {
            return;
        }

        try
        {
            foreach (string name in NoDropCursorNames)
            {
                IntPtr cursor = XcursorLibraryLoadCursor(display, name);
                if (cursor == IntPtr.Zero)
                {
                    continue;
                }

                cursorField.SetValue(cursorFactory, cursor);
                return;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    public static void ShowPreview(Window owner, FileItemViewModel? item, int count)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        object? platform = GetX11Platform(owner);
        if (platform?.GetType().GetProperty("Display", AnyInstance)?.GetValue(platform) is not IntPtr display || display == IntPtr.Zero)
        {
            return;
        }

        _display = display;
        _previewWindow ??= CreatePreviewWindow(platform);

        (int width, int height) = _previewWindow.UpdateContent(item, count);
        _previewWidth = (int) Math.Ceiling(width * owner.RenderScaling);
        _previewHeight = (int) Math.Ceiling(height * owner.RenderScaling);
        _previewWindow.SetLinkMode(false);
        _previewWindow.Show();

        _previewXid = _previewWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_previewXid != IntPtr.Zero)
        {
            XShapeCombineRectangles(_display, _previewXid, ShapeInput, 0, 0, IntPtr.Zero, 0, 0, 0);
        }

        UpdatePreviewPosition();

        _timer ??= CreateTimer();
        _timer.Start();
    }

    public static void HidePreview()
    {
        _timer?.Stop();
        if (_previewWindow == null)
        {
            return;
        }

        _previewWindow.Hide();
        _previewWindow.Reset();
    }

    private static object? GetX11Platform(Window owner)
    {
        object? platformImpl = typeof(TopLevel).GetProperty("PlatformImpl", AnyInstance)?.GetValue(owner);
        return platformImpl?.GetType().GetField("_platform", NonPublicInstance)?.GetValue(platformImpl);
    }

    private static DragPreviewWindow CreatePreviewWindow(object platform)
    {
        Type platformType = platform.GetType();
        Type? windowType = platformType.Assembly.GetType("Avalonia.X11.X11Window");
        ConstructorInfo? ctor = windowType?.GetConstructor([platformType, typeof(IWindowImpl), typeof(bool)]);
        IWindowImpl? impl = ctor?.Invoke([platform, null, true]) as IWindowImpl;
        return new(impl);
    }

    private static DispatcherTimer CreateTimer()
    {
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) => UpdatePreviewPosition();
        return timer;
    }

    private static void UpdatePreviewPosition()
    {
        if (_display == IntPtr.Zero || _previewXid == IntPtr.Zero)
        {
            return;
        }

        XQueryPointer(_display, XDefaultRootWindow(_display), out _, out _, out int rootX, out int rootY, out _, out _, out uint mask);
        bool isLink = (mask & AltMask) != 0 && (mask & (ControlMask | ShiftMask)) == 0;
        _previewWindow?.SetLinkMode(isLink);

        XMoveWindow(_display, _previewXid, rootX - _previewWidth / 2, rootY - _previewHeight / 2);
        XRaiseWindow(_display, _previewXid);
        XFlush(_display);
    }
}
