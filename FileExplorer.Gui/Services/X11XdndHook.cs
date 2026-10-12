using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using FileExplorer.Gui.ViewModels;
using LoggerLib;
using EmitLabel = System.Reflection.Emit.Label;

namespace FileExplorer.Gui.Services;

public static class X11XdndHook
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";
    private const string LibXcursor = "libXcursor.so.1";

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr message_type;
        public int format;
        public IntPtr l0;
        public IntPtr l1;
        public IntPtr l2;
        public IntPtr l3;
        public IntPtr l4;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XSelectionEvent
    {
        public int type;
        public IntPtr serial;
        public int send_event;
        public IntPtr display;
        public IntPtr requestor;
        public IntPtr selection;
        public IntPtr target;
        public IntPtr property;
        public IntPtr time;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XWindowAttributes
    {
        public int x;
        public int y;
        public int width;
        public int height;
        public int border_width;
        public int depth;
        public IntPtr visual;
        public IntPtr root;
        public int c_class;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public IntPtr backing_planes;
        public IntPtr backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed;
        public int map_state;
        public IntPtr all_event_masks;
        public IntPtr your_event_mask;
        public IntPtr do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [DllImport(LibX11)]
    private static extern IntPtr XInternAtom(IntPtr display, string atom_name, int only_if_exists);

    [DllImport(LibX11)]
    private static extern int XSendEvent(
        IntPtr display,
        IntPtr window,
        int propagate,
        IntPtr event_mask,
        ref XClientMessageEvent event_send);

    [DllImport(LibX11, EntryPoint = "XSendEvent")]
    private static extern int XSendSelectionEvent(
        IntPtr display,
        IntPtr window,
        int propagate,
        IntPtr event_mask,
        ref XSelectionEvent event_send);

    [DllImport(LibX11)]
    private static extern int XConvertSelection(
        IntPtr display,
        IntPtr selection,
        IntPtr target,
        IntPtr property,
        IntPtr requestor,
        IntPtr time);

    [DllImport(LibX11)]
    private static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr long_offset,
        IntPtr long_length,
        int delete,
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
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XSetSelectionOwner(IntPtr display, IntPtr selection, IntPtr owner, IntPtr time);

    [DllImport(LibX11)]
    private static extern int XGrabPointer(
        IntPtr display,
        IntPtr grab_window,
        int owner_events,
        IntPtr event_mask,
        int pointer_mode,
        int keyboard_mode,
        IntPtr confine_to,
        IntPtr cursor,
        IntPtr time);

    [DllImport(LibX11)]
    private static extern int XUngrabPointer(IntPtr display, IntPtr time);

    [DllImport(LibX11)]
    private static extern int XGrabKeyboard(
        IntPtr display,
        IntPtr grab_window,
        int owner_events,
        int pointer_mode,
        int keyboard_mode,
        IntPtr time);

    [DllImport(LibX11)]
    private static extern int XUngrabKeyboard(IntPtr display, IntPtr time);

    [DllImport(LibX11)]
    private static extern int XChangeActivePointerGrab(
        IntPtr display,
        IntPtr event_mask,
        IntPtr cursor,
        IntPtr time);

    [DllImport(LibX11)]
    private static extern int XTranslateCoordinates(
        IntPtr display,
        IntPtr src_w,
        IntPtr dest_w,
        int src_x,
        int src_y,
        out int dest_x_return,
        out int dest_y_return,
        out IntPtr child_return);

    [DllImport(LibX11)]
    private static extern int XGetWindowAttributes(
        IntPtr display,
        IntPtr window,
        out XWindowAttributes window_attributes_return);

    [DllImport(LibX11)]
    private static extern int XQueryTree(
        IntPtr display,
        IntPtr window,
        out IntPtr root_return,
        out IntPtr parent_return,
        out IntPtr children_return,
        out uint nchildren_return);

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
    private static extern int XChangeProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        byte[] data,
        int nelements);

    [DllImport(LibX11, EntryPoint = "XChangeProperty")]
    private static extern int XChangePropertyAtoms(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr type,
        int format,
        int mode,
        IntPtr[] data,
        int nelements);

    [DllImport(LibX11)]
    private static extern int XMoveWindow(IntPtr display, IntPtr window, int x, int y);

    [DllImport(LibX11)]
    private static extern int XMoveResizeWindow(
        IntPtr display,
        IntPtr window,
        int x,
        int y,
        uint width,
        uint height);

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

    [DllImport(LibX11)]
    private static extern int XRaiseWindow(IntPtr display, IntPtr window);

    [DllImport(LibXcursor)]
    private static extern IntPtr XcursorLibraryLoadCursor(IntPtr display, string name);

    private static readonly Dictionary<IntPtr, Delegate> OriginalHandlers = new();
    private static readonly Dictionary<IntPtr, Action<List<string>, int, int, bool>> DropCallbacks = new();
    private static readonly Dictionary<IntPtr, Action<int, int>> DragOverCallbacks = new();
    private static readonly Dictionary<IntPtr, Action> DragLeaveCallbacks = new();
    private static readonly Dictionary<IntPtr, IntPtr> ActiveSources = new();
    private static readonly Dictionary<IntPtr, (int X, int Y)> LatestPositions = new();
    private static readonly Dictionary<IntPtr, IntPtr> SourceActions = new();

    private static IntPtr _display;
    private static IntPtr _rootWindow;
    private static IntPtr _xdndEnter;
    private static IntPtr _xdndPosition;
    private static IntPtr _xdndStatus;
    private static IntPtr _xdndLeave;
    private static IntPtr _xdndDrop;
    private static IntPtr _xdndFinished;
    private static IntPtr _xdndSelection;
    private static IntPtr _xdndTypeList;
    private static IntPtr _xdndAware;
    private static IntPtr _xdndProxy;
    private static IntPtr _xdndActionCopy;
    private static IntPtr _xdndActionMove;
    private static IntPtr _textUriList;
    private static IntPtr _atomTargets;
    private static IntPtr _atomAtom;
    private static IntPtr _utf8String;
    private static IntPtr _textPlain;
    private static IntPtr _netWmState;
    private static IntPtr _netWmStateAbove;
    private static bool _atomsInitialized;

    private static IntPtr _cursorCopy;
    private static IntPtr _cursorMove;
    private static IntPtr _cursorNone;
    private static bool _cursorsLoaded;

    private static bool _isDragSourceActive;
    private static IntPtr _activeDragSourceWindow;
    private static List<string>? _activeDragPaths;
    private static IntPtr _activeDragAction;
    private static IntPtr _dragTargetWindow;
    private static bool _dragTargetAccepted;
    private static int _lastRootX;
    private static int _lastRootY;

    private static object? _x11Platform;
    private static DragPreviewWindow? _previewWindow;
    private static IntPtr _previewXid;
    private static int _previewWidth = 60;
    private static int _previewHeight = 60;
    private static DispatcherTimer? _dragTimer;

    public static bool IsDragActive => _isDragSourceActive;

    public static IReadOnlyList<string>? ActiveDragPaths => _activeDragPaths;

    public static bool IsPathBeingDragged(string path)
    {
        if (string.IsNullOrEmpty(path) || !_isDragSourceActive || _activeDragPaths == null || _activeDragPaths.Count == 0)
        {
            return false;
        }

        string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

        foreach (string dragPath in _activeDragPaths)
        {
            if (string.IsNullOrEmpty(dragPath))
            {
                continue;
            }

            string dragNorm = Path.GetFullPath(dragPath).TrimEnd(Path.DirectorySeparatorChar);

            if (string.Equals(normalized, dragNorm, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(dragNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IWindowImpl? CreatePreviewPlatformImpl()
    {
        if (!OperatingSystem.IsLinux() || _x11Platform == null)
        {
            return null;
        }

        Type platformType = _x11Platform.GetType();
        Assembly x11Asm = platformType.Assembly;
        Type? x11WinType = x11Asm.GetType("Avalonia.X11.X11Window");
        if (x11WinType == null)
        {
            return null;
        }

        ConstructorInfo? ctor = x11WinType.GetConstructor([platformType, typeof(IWindowImpl), typeof(bool)]);
        if (ctor == null)
        {
            return null;
        }

        object? impl = ctor.Invoke([_x11Platform, null, true]);
        return impl as IWindowImpl;
    }

    public static void Hook(
        Window window,
        Action<List<string>, int, int, bool> onDropped,
        Action<int, int>? onDragOver = null,
        Action? onDragLeave = null)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        IntPtr xid = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (xid == IntPtr.Zero)
        {
            return;
        }

        PropertyInfo? pImplProp = typeof(TopLevel).GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        object? platformImpl = pImplProp?.GetValue(window);
        if (platformImpl == null)
        {
            return;
        }

        FieldInfo? platformField = platformImpl.GetType().GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic);
        object? x11Platform = platformField?.GetValue(platformImpl);
        if (x11Platform == null)
        {
            return;
        }

        _x11Platform = x11Platform;

        PropertyInfo? displayProp = x11Platform.GetType().GetProperty("Display", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _display = (IntPtr) (displayProp?.GetValue(x11Platform) ?? IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            return;
        }

        InitializeAtoms(_display);

        IntPtr[] xdndVersion = [(IntPtr) 5];
        XChangePropertyAtoms(_display, xid, _xdndAware, _atomAtom, 32, 0, xdndVersion, 1);

        PropertyInfo? windowsProp = x11Platform.GetType().GetProperty("Windows", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        IDictionary? windowsDict = windowsProp?.GetValue(x11Platform) as IDictionary;
        if (windowsDict == null || !windowsDict.Contains(xid))
        {
            return;
        }

        Delegate? origHandler = windowsDict[xid] as Delegate;
        if (origHandler == null)
        {
            return;
        }

        OriginalHandlers[xid] = origHandler;
        DropCallbacks[xid] = onDropped;
        if (onDragOver != null)
        {
            DragOverCallbacks[xid] = onDragOver;
        }

        if (onDragLeave != null)
        {
            DragLeaveCallbacks[xid] = onDragLeave;
        }

        Type delegateType = origHandler.GetType();
        MethodInfo? invokeMethod = delegateType.GetMethod("Invoke");
        if (invokeMethod == null)
        {
            return;
        }

        Type paramType = invokeMethod.GetParameters()[0].ParameterType;
        DynamicMethod dm = new($"XdndHook_{xid}", null, [paramType], typeof(X11XdndHook).Module);
        ILGenerator il = dm.GetILGenerator();
        EmitLabel callOrig = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Conv_U);
        il.Emit(OpCodes.Ldc_I8, (long) xid);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Call, typeof(X11XdndHook).GetMethod(nameof(ProcessXEvent), BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Brfalse_S, callOrig);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(callOrig);
        il.Emit(OpCodes.Ldc_I8, (long) xid);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Call, typeof(X11XdndHook).GetMethod(nameof(GetOriginalHandler), BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Castclass, delegateType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, invokeMethod);
        il.Emit(OpCodes.Ret);

        Delegate hookDelegate = dm.CreateDelegate(delegateType);
        windowsDict[xid] = hookDelegate;
    }

    public static void StartDrag(Window sourceWindow, List<string> filePaths, FileItemViewModel? firstItem)
    {
        if (!OperatingSystem.IsLinux() || filePaths.Count == 0)
        {
            return;
        }

        IntPtr xid = sourceWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (xid == IntPtr.Zero)
        {
            return;
        }

        if (_x11Platform == null)
        {
            PropertyInfo? pImplProp = typeof(TopLevel).GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object? platformImpl = pImplProp?.GetValue(sourceWindow);
            FieldInfo? platformField = platformImpl?.GetType().GetField("_platform", BindingFlags.Instance | BindingFlags.NonPublic);
            _x11Platform = platformField?.GetValue(platformImpl);
        }

        if (_display == IntPtr.Zero && _x11Platform != null)
        {
            PropertyInfo? displayProp = _x11Platform.GetType().GetProperty("Display", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _display = (IntPtr) (displayProp?.GetValue(_x11Platform) ?? IntPtr.Zero);
            if (_display == IntPtr.Zero)
            {
                return;
            }
        }

        InitializeAtoms(_display);
        EnsureCursors(_display);

        XQueryPointer(_display, _rootWindow, out _, out _, out int rootX, out int rootY, out _, out _, out uint mask);
        _lastRootX = rootX;
        _lastRootY = rootY;

        bool isControl = (mask & 4) != 0;
        _activeDragAction = isControl ? _xdndActionCopy : _xdndActionMove;

        _activeDragSourceWindow = xid;
        _activeDragPaths = filePaths;
        _isDragSourceActive = true;
        _dragTargetWindow = IntPtr.Zero;
        _dragTargetAccepted = false;

        _ = Logger.LogI($"Drag started for {filePaths.Count} items");

        XSetSelectionOwner(_display, _xdndSelection, xid, IntPtr.Zero);

        IntPtr[] typeList = [_textUriList, _utf8String, _textPlain];
        XChangePropertyAtoms(_display, xid, _xdndTypeList, _atomAtom, 32, 0, typeList, typeList.Length);

        ShowPreview(rootX, rootY, firstItem, filePaths.Count);

        IntPtr initialCursor = (_activeDragAction == _xdndActionCopy) ? _cursorCopy : _cursorMove;
        if (initialCursor == IntPtr.Zero)
        {
            initialCursor = _cursorMove != IntPtr.Zero ? _cursorMove : _cursorCopy;
        }

        IntPtr grabMask = (IntPtr) (64 | 8 | 256 | 8192);
        XGrabPointer(_display, xid, 0, grabMask, 1, 1, IntPtr.Zero, initialCursor, IntPtr.Zero);
        XGrabKeyboard(_display, xid, 0, 1, 1, IntPtr.Zero);

        _dragTimer?.Stop();
        _dragTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        _dragTimer.Tick += OnDragTimerTick;
        _dragTimer.Start();

        XFlush(_display);
    }

    private static void OnDragTimerTick(object? sender, EventArgs e)
    {
        if (!_isDragSourceActive || _display == IntPtr.Zero || _rootWindow == IntPtr.Zero)
        {
            _dragTimer?.Stop();
            return;
        }

        XQueryPointer(_display, _rootWindow, out _, out _, out int rootX, out int rootY, out _, out _, out uint mask);
        _lastRootX = rootX;
        _lastRootY = rootY;

        bool button1Down = (mask & 256) != 0;
        if (!button1Down)
        {
            _dragTimer?.Stop();
            HandleDragDrop(IntPtr.Zero);
            return;
        }

        bool isControl = (mask & 4) != 0;
        IntPtr newAction = isControl ? _xdndActionCopy : _xdndActionMove;
        bool actionChanged = newAction != _activeDragAction;
        _activeDragAction = newAction;
        if (actionChanged)
        {
            UpdateGrabCursor(_dragTargetAccepted);
        }

        int posX = rootX - (_previewWidth / 2);
        int posY = rootY - (_previewHeight / 2);

        if (_previewXid != IntPtr.Zero)
        {
            XMoveWindow(_display, _previewXid, posX, posY);
            XRaiseWindow(_display, _previewXid);
        }

        IntPtr target = FindXdndTarget(_display, _rootWindow, rootX, rootY, _previewXid);
        if (target != _dragTargetWindow)
        {
            if (_dragTargetWindow != IntPtr.Zero)
            {
                SendXdndLeave(_display, _dragTargetWindow, _activeDragSourceWindow);
            }

            _dragTargetWindow = target;
            _dragTargetAccepted = false;

            if (_dragTargetWindow != IntPtr.Zero)
            {
                SendXdndEnter(_display, _dragTargetWindow, _activeDragSourceWindow, _textUriList);
                SendXdndPosition(_display, _dragTargetWindow, _activeDragSourceWindow, rootX, rootY, IntPtr.Zero, _activeDragAction);
            }
            else
            {
                UpdateGrabCursor(false);
            }
        }
        else if (_dragTargetWindow != IntPtr.Zero)
        {
            SendXdndPosition(_display, _dragTargetWindow, _activeDragSourceWindow, rootX, rootY, IntPtr.Zero, _activeDragAction);
        }

        XFlush(_display);
    }

    private static void HandleDragDrop(IntPtr time)
    {
        _dragTimer?.Stop();

        XUngrabPointer(_display, IntPtr.Zero);
        XUngrabKeyboard(_display, IntPtr.Zero);

        if (_previewWindow != null)
        {
            _previewWindow.Hide();
            _previewWindow.Close();
            _previewWindow = null;
            _previewXid = IntPtr.Zero;
        }

        if (_dragTargetWindow == _activeDragSourceWindow)
        {
            if (_activeDragPaths != null && _activeDragPaths.Count > 0 && DropCallbacks.TryGetValue(_activeDragSourceWindow, out Action<List<string>, int, int, bool>? cb))
            {
                List<string> paths = new(_activeDragPaths);
                bool isCopy = (_activeDragAction == _xdndActionCopy);
                int dropX = _lastRootX;
                int dropY = _lastRootY;
                Dispatcher.UIThread.Post(() => cb(paths, dropX, dropY, isCopy));
            }
        }
        else if (_dragTargetAccepted && _dragTargetWindow != IntPtr.Zero)
        {
            SendXdndDrop(_display, _dragTargetWindow, _activeDragSourceWindow, time);
        }
        else if (_dragTargetWindow != IntPtr.Zero)
        {
            SendXdndLeave(_display, _dragTargetWindow, _activeDragSourceWindow);
        }

        _isDragSourceActive = false;
        _dragTargetWindow = IntPtr.Zero;
        _dragTargetAccepted = false;

        XFlush(_display);
    }

    public static void DropDrag()
    {
        if (!_isDragSourceActive)
        {
            return;
        }

        HandleDragDrop(IntPtr.Zero);
    }

    public static void CancelDrag()
    {
        if (!_isDragSourceActive)
        {
            return;
        }

        _ = Logger.LogI("Drag canceled");

        _dragTimer?.Stop();

        XUngrabPointer(_display, IntPtr.Zero);
        XUngrabKeyboard(_display, IntPtr.Zero);

        if (_previewWindow != null)
        {
            _previewWindow.Hide();
            _previewWindow.Close();
            _previewWindow = null;
            _previewXid = IntPtr.Zero;
        }

        if (_dragTargetWindow != IntPtr.Zero)
        {
            SendXdndLeave(_display, _dragTargetWindow, _activeDragSourceWindow);
        }

        _isDragSourceActive = false;
        _dragTargetWindow = IntPtr.Zero;
        _dragTargetAccepted = false;
        _activeDragPaths = null;

        XFlush(_display);
    }

    public static Delegate GetOriginalHandler(IntPtr xid)
    {
        return OriginalHandlers[xid];
    }

    public static bool ProcessXEvent(IntPtr evPtr, IntPtr xid)
    {
        int type = Marshal.ReadInt32(evPtr);

        if (type == 30)
        {
            IntPtr requestor = Marshal.ReadIntPtr(evPtr, 40);
            IntPtr selection = Marshal.ReadIntPtr(evPtr, 48);
            IntPtr target = Marshal.ReadIntPtr(evPtr, 56);
            IntPtr property = Marshal.ReadIntPtr(evPtr, 64);
            IntPtr time = Marshal.ReadIntPtr(evPtr, 72);

            if (selection == _xdndSelection)
            {
                HandleSelectionRequest(requestor, selection, target, property, time);
                return true;
            }
        }
        else if (type == 33)
        {
            IntPtr msgType = Marshal.ReadIntPtr(evPtr, 40);

            if (msgType == _xdndStatus)
            {
                if (_isDragSourceActive)
                {
                    IntPtr flags = Marshal.ReadIntPtr(evPtr, 64);
                    bool accepted = (flags.ToInt64() & 1) != 0;
                    _dragTargetAccepted = accepted;
                    UpdateGrabCursor(accepted);
                    return true;
                }
            }
            else if (msgType == _xdndFinished)
            {
                _activeDragPaths = null;
                return true;
            }
            else if (msgType == _xdndEnter)
            {
                IntPtr sourceWin = Marshal.ReadIntPtr(evPtr, 56);
                ActiveSources[xid] = sourceWin;
                return true;
            }
            else if (msgType == _xdndPosition)
            {
                IntPtr sourceWin = Marshal.ReadIntPtr(evPtr, 56);
                ActiveSources[xid] = sourceWin;

                IntPtr pos = Marshal.ReadIntPtr(evPtr, 72);
                long posVal = pos.ToInt64();
                int rootX = (int) ((posVal >> 16) & 0xFFFF);
                int rootY = (int) (posVal & 0xFFFF);
                LatestPositions[xid] = (rootX, rootY);

                IntPtr action = Marshal.ReadIntPtr(evPtr, 88);
                SourceActions[xid] = action;

                SendStatus(xid, sourceWin, action);

                if (DragOverCallbacks.TryGetValue(xid, out Action<int, int>? onOver))
                {
                    Dispatcher.UIThread.Post(() => onOver(rootX, rootY));
                }

                return true;
            }
            else if (msgType == _xdndLeave)
            {
                ActiveSources.Remove(xid);
                LatestPositions.Remove(xid);
                SourceActions.Remove(xid);

                if (DragLeaveCallbacks.TryGetValue(xid, out Action? onLeave))
                {
                    Dispatcher.UIThread.Post(() => onLeave());
                }

                return true;
            }
            else if (msgType == _xdndDrop)
            {
                IntPtr sourceWin = Marshal.ReadIntPtr(evPtr, 56);
                IntPtr time = Marshal.ReadIntPtr(evPtr, 72);
                ActiveSources[xid] = sourceWin;

                XConvertSelection(_display, _xdndSelection, _textUriList, _xdndSelection, xid, time);
                XFlush(_display);
                return true;
            }
        }
        else if (type == 31)
        {
            IntPtr selection = Marshal.ReadIntPtr(evPtr, 40);
            IntPtr prop = Marshal.ReadIntPtr(evPtr, 56);

            if (selection == _xdndSelection && prop != IntPtr.Zero)
            {
                HandleDropData(xid, prop);
                return true;
            }
        }

        return false;
    }

    private static void ShowPreview(int rootX, int rootY, FileItemViewModel? firstItem, int count)
    {
        if (_previewWindow == null)
        {
            IWindowImpl? impl = CreatePreviewPlatformImpl();
            _previewWindow = impl != null ? new(impl) : new();
        }

        (int w, int h) = _previewWindow.UpdateContent(firstItem, count);
        _previewWidth = w;
        _previewHeight = h;

        int posX = rootX - (_previewWidth / 2);
        int posY = rootY - (_previewHeight / 2);

        _previewWindow.Show();

        _previewXid = _previewWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_previewXid != IntPtr.Zero)
        {
            try
            {
                XShapeCombineRectangles(_display, _previewXid, 2, 0, 0, IntPtr.Zero, 0, 0, 0);
            }
            catch
            {
            }

            XMoveResizeWindow(_display, _previewXid, posX, posY, (uint) _previewWidth, (uint) _previewHeight);
            XRaiseWindow(_display, _previewXid);
            XFlush(_display);
        }
    }

    private static void UpdateGrabCursor(bool accepted)
    {
        if (_display == IntPtr.Zero)
        {
            return;
        }

        IntPtr cursor;
        if (_dragTargetWindow != IntPtr.Zero && !accepted)
        {
            cursor = _cursorNone != IntPtr.Zero ? _cursorNone : ((_activeDragAction == _xdndActionCopy) ? _cursorCopy : _cursorMove);
        }
        else
        {
            cursor = (_activeDragAction == _xdndActionCopy) ? _cursorCopy : _cursorMove;
        }

        if (cursor == IntPtr.Zero)
        {
            cursor = _cursorMove != IntPtr.Zero ? _cursorMove : _cursorCopy;
        }

        if (cursor != IntPtr.Zero)
        {
            IntPtr grabMask = (IntPtr) (64 | 8 | 256 | 8192);
            XChangeActivePointerGrab(_display, grabMask, cursor, IntPtr.Zero);
            XFlush(_display);
        }
    }

    private static void EnsureCursors(IntPtr display)
    {
        if (_cursorsLoaded)
        {
            return;
        }

        try
        {
            _cursorCopy = XcursorLibraryLoadCursor(display, "dnd-copy");
            if (_cursorCopy == IntPtr.Zero)
            {
                _cursorCopy = XcursorLibraryLoadCursor(display, "copy");
            }

            _cursorMove = XcursorLibraryLoadCursor(display, "dnd-move");
            if (_cursorMove == IntPtr.Zero)
            {
                _cursorMove = XcursorLibraryLoadCursor(display, "grabbing");
            }

            if (_cursorMove == IntPtr.Zero)
            {
                _cursorMove = XcursorLibraryLoadCursor(display, "closedhand");
            }

            _cursorNone = XcursorLibraryLoadCursor(display, "dnd-none");
            if (_cursorNone == IntPtr.Zero)
            {
                _cursorNone = XcursorLibraryLoadCursor(display, "dnd-no-drop");
            }
        }
        catch
        {
        }

        _cursorsLoaded = true;
    }

    private static void SendXdndEnter(IntPtr display, IntPtr target, IntPtr sourceWin, IntPtr type)
    {
        XClientMessageEvent enter = new()
        {
            type = 33,
            window = target,
            message_type = _xdndEnter,
            format = 32,
            l0 = sourceWin,
            l1 = (IntPtr) (5 << 24),
            l2 = type,
            l3 = IntPtr.Zero,
            l4 = IntPtr.Zero
        };

        XSendEvent(display, target, 0, IntPtr.Zero, ref enter);
    }

    private static void SendXdndPosition(IntPtr display, IntPtr target, IntPtr sourceWin, int rootX, int rootY, IntPtr time, IntPtr action)
    {
        IntPtr pos = (IntPtr) ((rootX << 16) | (rootY & 0xFFFF));

        XClientMessageEvent msg = new()
        {
            type = 33,
            window = target,
            message_type = _xdndPosition,
            format = 32,
            l0 = sourceWin,
            l1 = IntPtr.Zero,
            l2 = pos,
            l3 = time,
            l4 = action
        };

        XSendEvent(display, target, 0, IntPtr.Zero, ref msg);
    }

    private static void SendXdndLeave(IntPtr display, IntPtr target, IntPtr sourceWin)
    {
        XClientMessageEvent leave = new()
        {
            type = 33,
            window = target,
            message_type = _xdndLeave,
            format = 32,
            l0 = sourceWin,
            l1 = IntPtr.Zero,
            l2 = IntPtr.Zero,
            l3 = IntPtr.Zero,
            l4 = IntPtr.Zero
        };

        XSendEvent(display, target, 0, IntPtr.Zero, ref leave);
    }

    private static void SendXdndDrop(IntPtr display, IntPtr target, IntPtr sourceWin, IntPtr time)
    {
        IntPtr dropTime = time != IntPtr.Zero ? time : (IntPtr) unchecked((uint) Environment.TickCount);
        XClientMessageEvent drop = new()
        {
            type = 33,
            window = target,
            message_type = _xdndDrop,
            format = 32,
            l0 = sourceWin,
            l1 = IntPtr.Zero,
            l2 = dropTime,
            l3 = IntPtr.Zero,
            l4 = IntPtr.Zero
        };

        XSendEvent(display, target, 0, IntPtr.Zero, ref drop);
        X11WindowHelper.UpdateUserTime(sourceWin);
    }

    private static void HandleSelectionRequest(IntPtr requestor, IntPtr selection, IntPtr target, IntPtr property, IntPtr time)
    {
        if (property == IntPtr.Zero)
        {
            property = target;
        }

        bool success = false;

        if (target == _atomTargets)
        {
            IntPtr[] targets = [_atomTargets, _textUriList, _utf8String, _textPlain];
            XChangePropertyAtoms(_display, requestor, property, _atomAtom, 32, 0, targets, targets.Length);
            success = true;
        }
        else if (target == _textUriList)
        {
            if (_activeDragPaths != null && _activeDragPaths.Count > 0)
            {
                StringBuilder sb = new();
                foreach (string p in _activeDragPaths)
                {
                    sb.Append(new Uri(p, UriKind.Absolute).AbsoluteUri);
                    sb.Append("\r\n");
                }

                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                XChangeProperty(_display, requestor, property, _textUriList, 8, 0, bytes, bytes.Length);
                success = true;
            }
        }
        else if (target == _utf8String || target == _textPlain)
        {
            if (_activeDragPaths != null && _activeDragPaths.Count > 0)
            {
                string content = string.Join(Environment.NewLine, _activeDragPaths);
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                XChangeProperty(_display, requestor, property, target, 8, 0, bytes, bytes.Length);
                success = true;
            }
        }

        XSelectionEvent reply = new()
        {
            type = 31,
            display = _display,
            requestor = requestor,
            selection = selection,
            target = target,
            property = success ? property : IntPtr.Zero,
            time = time
        };

        XSendSelectionEvent(_display, requestor, 0, IntPtr.Zero, ref reply);
        XFlush(_display);
    }

    private static IntPtr FindXdndTarget(IntPtr display, IntPtr root, int rootX, int rootY, IntPtr ignoreWindow)
    {
        IntPtr target = FindXdndInTree(display, root, root, rootX, rootY, ignoreWindow);
        if (target != IntPtr.Zero)
        {
            IntPtr proxy = GetProxyWindow(display, target);
            if (proxy != IntPtr.Zero)
            {
                target = proxy;
            }
        }

        return target;
    }

    private static IntPtr FindXdndInTree(IntPtr display, IntPtr root, IntPtr window, int rootX, int rootY, IntPtr ignoreWindow)
    {
        if (window == ignoreWindow)
        {
            return IntPtr.Zero;
        }

        if (window != root && HasProperty(display, window, _xdndAware))
        {
            return window;
        }

        int res = XQueryTree(display, window, out _, out _, out IntPtr childrenPtr, out uint nchildren);
        if (res == 0 || childrenPtr == IntPtr.Zero || nchildren == 0)
        {
            return IntPtr.Zero;
        }

        try
        {
            int count = (int) nchildren;
            IntPtr[] children = new IntPtr[count];
            for (int i = 0; i < count; i++)
            {
                children[i] = Marshal.ReadIntPtr(childrenPtr, i * IntPtr.Size);
            }

            for (int i = count - 1; i >= 0; i--)
            {
                IntPtr child = children[i];
                if (child == ignoreWindow)
                {
                    continue;
                }

                if (XGetWindowAttributes(display, child, out XWindowAttributes attrs) == 0)
                {
                    continue;
                }

                if (attrs.map_state != 2)
                {
                    continue;
                }

                if (XTranslateCoordinates(display, root, child, rootX, rootY, out int destX, out int destY, out _) != 0)
                {
                    if (destX >= 0 && destX < attrs.width && destY >= 0 && destY < attrs.height)
                    {
                        IntPtr found = FindXdndInTree(display, root, child, rootX, rootY, ignoreWindow);
                        if (found != IntPtr.Zero)
                        {
                            return found;
                        }
                    }
                }
            }
        }
        finally
        {
            XFree(childrenPtr);
        }

        return IntPtr.Zero;
    }

    private static bool HasProperty(IntPtr display, IntPtr window, IntPtr property)
    {
        int result = XGetWindowProperty(
            display,
            window,
            property,
            IntPtr.Zero,
            new(1),
            0,
            IntPtr.Zero,
            out IntPtr actualType,
            out _,
            out _,
            out _,
            out IntPtr prop);

        if (result == 0 && actualType != IntPtr.Zero)
        {
            if (prop != IntPtr.Zero)
            {
                XFree(prop);
            }

            return true;
        }

        return false;
    }

    private static IntPtr GetProxyWindow(IntPtr display, IntPtr window)
    {
        int result = XGetWindowProperty(
            display,
            window,
            _xdndProxy,
            IntPtr.Zero,
            new(1),
            0,
            new(33),
            out IntPtr actualType,
            out _,
            out IntPtr nitems,
            out _,
            out IntPtr prop);

        if (result == 0 && actualType != IntPtr.Zero && nitems != IntPtr.Zero && prop != IntPtr.Zero)
        {
            try
            {
                return Marshal.ReadIntPtr(prop);
            }
            finally
            {
                XFree(prop);
            }
        }

        return IntPtr.Zero;
    }

    private static void SendStatus(IntPtr xid, IntPtr sourceWin, IntPtr requestedAction)
    {
        IntPtr acceptedAction = (requestedAction == _xdndActionMove) ? _xdndActionMove : _xdndActionCopy;

        XClientMessageEvent reply = new()
        {
            type = 33,
            window = sourceWin,
            message_type = _xdndStatus,
            format = 32,
            l0 = xid,
            l1 = new(1),
            l2 = IntPtr.Zero,
            l3 = IntPtr.Zero,
            l4 = acceptedAction,
        };

        XSendEvent(_display, sourceWin, 0, IntPtr.Zero, ref reply);
        XFlush(_display);
    }

    private static void SendFinished(IntPtr xid, IntPtr sourceWin, IntPtr action)
    {
        XClientMessageEvent finished = new()
        {
            type = 33,
            window = sourceWin,
            message_type = _xdndFinished,
            format = 32,
            l0 = xid,
            l1 = new(1),
            l2 = (action == _xdndActionMove) ? _xdndActionMove : _xdndActionCopy,
            l3 = IntPtr.Zero,
            l4 = IntPtr.Zero,
        };

        XSendEvent(_display, sourceWin, 0, IntPtr.Zero, ref finished);
        XFlush(_display);
    }

    private static void HandleDropData(IntPtr xid, IntPtr prop)
    {
        string? text = ReadStringProperty(xid, prop);

        IntPtr action = _xdndActionCopy;
        if (SourceActions.TryGetValue(xid, out IntPtr act))
        {
            action = act;
            SourceActions.Remove(xid);
        }

        if (ActiveSources.TryGetValue(xid, out IntPtr sourceWin))
        {
            SendFinished(xid, sourceWin, action);
            ActiveSources.Remove(xid);
        }

        LatestPositions.TryGetValue(xid, out (int X, int Y) rootPos);
        LatestPositions.Remove(xid);

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        List<string> paths = ParseUriList(text);
        bool isCopy = (action == _xdndActionCopy);
        if (paths.Count > 0 && DropCallbacks.TryGetValue(xid, out Action<List<string>, int, int, bool>? callback))
        {
            Dispatcher.UIThread.Post(() => callback(paths, rootPos.X, rootPos.Y, isCopy));
        }
    }

    private static string? ReadStringProperty(IntPtr xid, IntPtr prop)
    {
        int result = XGetWindowProperty(
            _display,
            xid,
            prop,
            IntPtr.Zero,
            new(65536),
            0,
            IntPtr.Zero,
            out _,
            out _,
            out IntPtr nitems,
            out _,
            out IntPtr dataPtr);

        if (result != 0 || nitems == IntPtr.Zero || dataPtr == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            int length = (int) nitems;
            byte[] bytes = new byte[length];
            Marshal.Copy(dataPtr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            XFree(dataPtr);
        }
    }

    private static List<string> ParseUriList(string text)
    {
        List<string> paths = new();
        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri))
                {
                    paths.Add(uri.LocalPath);
                }
            }
            else if (File.Exists(line) || Directory.Exists(line))
            {
                paths.Add(line);
            }
        }

        return paths;
    }

    private static void InitializeAtoms(IntPtr display)
    {
        if (_atomsInitialized)
        {
            return;
        }

        _xdndEnter = XInternAtom(display, "XdndEnter", 0);
        _xdndPosition = XInternAtom(display, "XdndPosition", 0);
        _xdndStatus = XInternAtom(display, "XdndStatus", 0);
        _xdndLeave = XInternAtom(display, "XdndLeave", 0);
        _xdndDrop = XInternAtom(display, "XdndDrop", 0);
        _xdndFinished = XInternAtom(display, "XdndFinished", 0);
        _xdndSelection = XInternAtom(display, "XdndSelection", 0);
        _xdndTypeList = XInternAtom(display, "XdndTypeList", 0);
        _xdndAware = XInternAtom(display, "XdndAware", 0);
        _xdndProxy = XInternAtom(display, "XdndProxy", 0);
        _xdndActionCopy = XInternAtom(display, "XdndActionCopy", 0);
        _xdndActionMove = XInternAtom(display, "XdndActionMove", 0);
        _textUriList = XInternAtom(display, "text/uri-list", 0);
        _atomTargets = XInternAtom(display, "TARGETS", 0);
        _atomAtom = XInternAtom(display, "ATOM", 0);
        _utf8String = XInternAtom(display, "UTF8_STRING", 0);
        _textPlain = XInternAtom(display, "text/plain", 0);
        _netWmState = XInternAtom(display, "_NET_WM_STATE", 0);
        _netWmStateAbove = XInternAtom(display, "_NET_WM_STATE_ABOVE", 0);
        _rootWindow = XDefaultRootWindow(display);
        _atomsInitialized = true;
    }
}
