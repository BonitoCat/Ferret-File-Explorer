# Local compatibility libraries

Ferret includes the five libraries it needs in `Libraries/`. A fresh clone no
longer needs FileLib, LoggerLib, SecretsLib, TuiLib or InputLib checkouts next
to the repository. These are implementations of the APIs Ferret uses, rather
than complete recreations of the original libraries.

## Build and verification

Use the .NET 8 SDK or newer:

```sh
dotnet build FileExplorer.sln -c Release -m:1
bash _builds/verify-libraries.sh
```

The verification script targets Linux and needs Python 3 for the isolated
credential-provider and pseudo-terminal checks. It does not use your real
keyring or change your saved settings. Each C# check project can also be run
individually on Windows; Linux-only checks explicitly report a skip.

For isolated tests using a real keyring and GUI, install `dbus-x11`,
`gnome-keyring`, `libsecret-tools`, `xvfb`, `xauth` and `xdotool`, then run
`bash _builds/verify-libraries.sh --desktop`. These tests create their own
temporary D-Bus/keyring and X display, navigate to a temporary directory,
create a test file and quit Ferret.

Linux x64 and ARM64 are the supported targets. Windows compilation and
platform-independent checks are supported; full Windows/macOS runtime support
is not promised. NativeAOT launcher and DEB build requirements remain documented
in [STARTUP.md](STARTUP.md).

## Paths and logs

Absolute `XDG_DATA_HOME`, `XDG_CONFIG_HOME` and `XDG_CACHE_HOME` values override
the Linux defaults `~/.local/share`, `~/.config` and `~/.cache`. Relative values
are ignored. Existing `fe` subdirectories and config migration are retained.
Creating a file through FileLib does not truncate an existing file.

Logs are serialized to `fe/logs` under the data directory. Retention removes
only files matching the logger's own generated filename format. Logging failures
do not interrupt file operations.

## Credentials

Install `libsecret-tools` and use an unlocked Secret Service collection, such
as GNOME Keyring or a compatible KDE service, to remember passwords across
sessions. DEB packages recommend this dependency. Secret attributes are
`application=org.fe.fileexplorer` and `key=<credential key>`.

The provider runs without a shell, receives passwords over standard input,
and has a five-second timeout per operation. If it is missing, locked or
unresponsive, new passwords remain in memory for the current process only.
They are never written to the old fallback format.

Existing `fe/.secrets` entries remain readable. Each entry is removed from the
old file only after successful storage in the system keyring. Failed migration
leaves the old entry intact. Rewrites are atomic and user-private on Linux.
The old machine/user-derived encryption exists only to read legacy data; it
is not used for new storage. Original-library keyring attributes are unknown,
so some previously stored accounts may require signing in again.

## Terminal behavior

The terminal implementation preserves Ferret's existing controls and uses only
foreground console input, with no global hooks or device permissions. `--tty`
remains supported. A paused input reader hands ownership to text dialogs; exit
restores cursor, echo and the alternate screen.

Terminals cannot always distinguish Ctrl+Enter from Enter, Shift+Space from
Space, left/right modifiers, or physical key releases. Use Ferret's existing
alternative shortcuts where necessary. Ctrl+C is Copy while the explorer owns
input, and F10 exits. The renderer clips to terminal cells, handles common
wide/combining Unicode characters and filters cursor/OSC escapes from text.

Linux interactive checks cover a pseudo-terminal. Desktop GUI behavior, real
keyring integration and target-architecture package installation also require
checks on an actual desktop; successful compilation alone does not establish
those results.

## Verification performed

The replacement was verified with .NET SDK 10.0.401 on Windows and SDK 8.0.415
on Ubuntu 26.04 x64:

- Full Release solution builds and all executable C# checks passed on both
  hosts; platform-specific checks report skips on Windows.
- Linux pseudo-terminal checks passed for navigation, split panes, persisted
  sidebar visibility, repeated dialog handoff, Unicode paths/file creation,
  tiny-window resizing, F10 exit and terminal-mode restoration.
- The real Secret Service adapter passed store/lookup/delete against a separate
  temporary GNOME keyring, including a password ending in a newline.
- Both managed and published x64 GUI builds passed visible-window, directory
  navigation, file creation and resident-quit checks in isolated Xvfb.
- CLI, GUI and Portal were published for Linux x64 and ARM64. NativeAOT
  launchers were built for both architectures with the target toolchain.

ARM64 execution and installation into a real desktop are not verified by these
checks. Network-server authentication, desktop associations and desktop-specific
tray/focus behavior still need manual acceptance testing. Existing ImageSharp
3.1.12 vulnerability warnings and unrelated application compiler warnings remain
outside this replacement.
