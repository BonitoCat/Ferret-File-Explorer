# Linux background startup

The full DEB packages start `ferret --background` at graphical login using
`/etc/xdg/autostart/ferret.desktop`. This keeps one hidden Home window ready.
Normal `ferret` and `fe-gui` calls open a new window through the resident process.
The OS continues to manage Super+E and the default file-manager association.

**View → Show tray icon** changes panel visibility immediately and saves it in
`gui_settings` as `show_tray_icon`. It does not stop the background process or
disable preloading. The default is enabled. GNOME installations without a tray
extension can still use the background process and the normal launch command.
**File → Quit Ferret**, the tray's Quit action, and `ferret --quit` exit Ferret.
Closing a window only closes that window. A later normal launch starts the
resident process again if necessary.

Explicit paths, relative paths (against the caller's directory), and network
URLs are forwarded. Picker modes still run in separate processes and preserve
stdout and exit status. A session lock prevents competing resident processes;
acknowledged request IDs prevent duplicate windows during connection retries.

## Building

`_builds/build.sh` publishes `FileExplorer.Launcher` with NativeAOT. Linux builds
need the .NET 8+ SDK, clang, LLVM tools, and zlib development headers in addition to the
existing GUI dependencies. Building ARM64 from x64 also needs an ARM64
compiler/sysroot (for example `gcc-aarch64-linux-gnu` and `zlib1g-dev:arm64` on
a configured Debian multiarch host, plus the ARM64 libc development files).
The symbol tools must support the target architecture (`llvm-objcopy`, or
`-p:ObjCopyName=aarch64-linux-gnu-objcopy` for an ARM64-only publish).
NativeAOT publishing must run on Linux.

The DEBs install the small launcher as `/usr/bin/ferret` and `/usr/bin/fe-gui`,
and the GUI as `/usr/lib/ferret/ferret-gui`. AppImage and direct GUI starts use
the same resident logic but do not gain the DEB's native launcher or autostart
entry. Headless CLI packages do not install background startup.

## Verification

Run the socket/process integration checks without the GUI's external libraries:

```sh
dotnet run --project FileExplorer.Startup.Tests
```

These cover session separation, UI-readiness queuing, retries after a lost
acknowledgement, argument/activation metadata, concurrent requests, process
locking, socket cleanup, and stale-socket recovery.

On Cinnamon first, then KDE and GNOME:

1. Log in with the DEB installed: no window, taskbar entry, or focus change should
   occur. Confirm the optional tray icon and a single resident GUI process.
2. Open Ferret repeatedly through the desktop and `ferret`. Each request should
   create exactly one visible window. Test quick bursts before a replacement
   window has finished preparing.
3. Change Home contents while all windows are closed. The next Home window
   should reflect the changes without clearing and reloading its initial list.
4. Toggle the tray in one window: other open windows must show the same setting;
   hiding it must preserve startup speed. Restart and confirm persistence.
5. Test explicit/relative paths with spaces, URLs, picker selection and cancel,
   and separate graphical sessions. Check focus and startup indicators under
   X11 and Wayland sessions using the existing Avalonia X11/XWayland backend.
6. Close windows repeatedly; memory, filesystem watches and `udisksctl monitor`
   processes must settle rather than grow indefinitely. Test Quit and recovery
   after terminating the resident process.

For performance, compare Release DEBs on the same Cinnamon machine before and
after this change. Collect 100 prepared openings and report median and p95.
Measure from the OS launch action until Home is visibly rendered and responds
to input; a socket acknowledgement, mapped window, or `Show()` completion alone
does not establish this. Record display refresh rate, compositor/session, CPU,
Home directory size, and idle RSS. Target p95 is below 100 ms; this is an
acceptance target, not a measured result. Test first opening after login and
cold startup separately. Hidden layout preparation does not eliminate every
first-paint cost, and the compositor determines when a frame becomes visible.
