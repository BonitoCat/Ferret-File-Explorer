# Original libraries as submodules

Ferret uses the original public libraries published by the upstream author:

| Library | Submodule |
| --- | --- |
| FileLib | `libs/FileLib` |
| LoggerLib | `libs/LoggerLib` |
| SecretsLib | `libs/SecretsLib` |
| TuiLib | `libs/TuiLib` |
| InputLib | `libs/InputLib` |

They are pinned to the same commits as upstream Ferret. A fresh clone must
initialize them before building:

```sh
git clone --recurse-submodules https://github.com/anelkoo/Ferret-File-Explorer.git
# or, in an existing clone:
git submodule update --init --recursive
```

The fork's own test projects live in `Tests/` instead of inside the libraries.

## Build and verification

Use the .NET 8 SDK or newer:

```sh
dotnet build FileExplorer.sln -c Release -m:1
bash _builds/verify-libraries.sh
```

The verification script targets Linux and needs Python 3 for the isolated
credential and pseudo-terminal checks. It does not use your real keyring or
change your saved settings. Each C# check project can also be run individually
on Windows; Linux-only checks explicitly report a skip.

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

`XDG_DATA_HOME`, `XDG_CONFIG_HOME` and `XDG_CACHE_HOME` are honoured verbatim
by FileLib, including relative values. The library does not create the returned
directories; Ferret creates them through `AppPaths` where needed. The default
Linux locations remain `~/.local/share`, `~/.config` and `~/.cache`, and the
existing `fe` data directory and config migration are retained.

`FileHelper.Create` truncates an existing file. Ferret only calls it after
checking that the target does not exist, so saved data is not affected.

LoggerLib writes to `fe/logs` under the data directory. Its retention limit
caps the number of files left in that directory after a new log is created;
unrelated files in the log directory can therefore be removed when retention
is enabled. Logging failures do not interrupt file operations.

## Credentials

SecretsLib talks to the Secret Service directly through `libsecret`. Install
`libsecret-tools` and use an unlocked collection, such as GNOME Keyring or a
compatible KDE service, to remember passwords across sessions. DEB packages
recommend this dependency. Entries use the schema name `org.fe.fileexplorer`
and the attribute `key=<credential key>`.

When no usable provider is available, new passwords remain in memory for the
current process only and are never written to the old fallback format.

Existing `fe/.secrets` entries remain readable. Each entry is removed from the
old file only after successful storage in the system keyring. Failed migration
leaves the old entry intact. Rewrites are atomic and user-private on Linux.
The old machine/user-derived encryption exists only to read legacy data; it is
not used for new storage.

## Terminal behavior

InputLib and TuiLib provide the terminal input and rendering primitives.
Ferret keeps its existing controls and uses foreground console input, with no
global hooks or device permissions. `--tty` remains supported. A paused input
reader hands ownership to text dialogs; exit restores cursor, echo and the
alternate screen.

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

The current tree was checked with the .NET 8 SDK on Linux:

- The full Release solution builds without errors.
- The library, credentials and startup check projects pass.
- The terminal smoke test passes navigation, split panes, sidebar visibility,
  repeated dialog handoff, Unicode paths/file creation, tiny-window resizing,
  F10 exit and terminal-mode restoration.

Desktop GUI and tray checks additionally require a graphical session; run
`bash _builds/verify-libraries.sh --desktop` on the target desktop. ARM64
execution and installation into a real desktop are not covered by these checks.
