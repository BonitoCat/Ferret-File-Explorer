# Ferret File Explorer — Rust GUI port

A self-contained Rust implementation of Ferret's core file handling and desktop file explorer.

The Rust port must provide a **fully functional graphical desktop application written in Rust**. A terminal UI (TUI), command-line-only interface, or terminal-based file browser is not an acceptable replacement for the GUI.

The original C# Avalonia application, portal, addons and network stack remain untouched. This workspace is an independent Rust implementation that reuses Ferret's existing data layout and configuration formats.

## Mandatory GUI requirements

- Build the application as a desktop GUI using Rust.
- Use **egui/eframe** as the GUI framework.
- The GUI must be the primary and default application interface.
- Support both mouse interaction and keyboard shortcuts.
- Provide a modern, consistent desktop interface with proper layouts, icons, menus, dialogs and status information.
- Do not implement the application as a TUI using ratatui, crossterm or similar terminal UI libraries.
- Do not require users to interact with a terminal to perform normal file-management operations.
- Long-running filesystem operations must not freeze the GUI. Use background tasks or threads where necessary.
- Display operation progress, errors and completion feedback directly in the GUI.

## Project layout

```text
rust/
├── Cargo.toml
└── crates/
    ├── ferret-core/   # Filesystem, listing, sorting, settings, bookmarks, places and operations
    └── ferret-gui/    # Rust desktop GUI using egui/eframe (binary: ferret)
```

## Build and run

Requires Rust 1.83+ and Linux.

```sh
cd rust
cargo build --release
./target/release/ferret

# Start in a specific directory
./target/release/ferret -d ~/Downloads

# Display command-line options
./target/release/ferret --help
./target/release/ferret --version
```

The application must launch its graphical interface by default. Command-line arguments may configure startup behavior, but must not turn the application into a terminal-based explorer.

Install it on your `PATH`:

```sh
install -Dm755 target/release/ferret ~/.local/bin/ferret
```

## Tests

```sh
cd rust
cargo test
cargo fmt --all -- --check
```

`ferret-core` must include unit tests for:

- Directory listing and filtering
- Natural sorting
- Navigation history
- Settings persistence
- Symlink bookmarks
- Places and device discovery
- File-size and metadata formatting
- Copy, cut, paste, duplicate, rename, delete and trash operations

`ferret-gui` must include tests for relevant application logic, including:

- Navigation and pane selection
- Dialog state and user interactions
- Keyboard shortcuts
- Multi-selection and range selection
- File operation progress and error handling
- Open-with command parsing and quoting
- Path editing and multibyte text input

## Required functionality

### Main file explorer

- Two-pane graphical file explorer with clear active-pane highlighting.
- Resizable panes and adjustable sidebar width.
- Sidebar containing Places, Bookmarks, Devices and Network sections.
- File and directory icons based on file type.
- Natural, case-insensitive sorting.
- Directories displayed before files.
- Parent directory (`..`) pinned to the top where applicable.
- Hidden-file visibility toggle.
- File-size visibility toggle.
- Settings remembered between sessions.
- File metadata displayed in an appropriate details or properties view.
- Context menus accessible through right-click.
- Double-click to open files and directories.
- Keyboard navigation and shortcuts.

### Selection and navigation

- Single and multiple file selection.
- Range selection using Shift.
- Add or remove individual selections using Ctrl.
- Select all and deselect all.
- Back and forward navigation history.
- Up-directory navigation.
- Breadcrumb navigation.
- Editable address bar with direct path entry.
- Search with substring matching and `*` / `?` wildcards.
- Type-ahead file selection.
- Split panes and sidebar visibility.
- Refresh directory contents manually.

### File operations

- Copy, cut and paste.
- Duplicate files and directories.
- Rename files and directories.
- Create new files.
- Create new directories.
- Move files and directories.
- Move items to trash.
- Permanently delete items, with an appropriate confirmation dialog.
- Display progress for ongoing operations.
- Allow users to cancel operations where technically possible.
- Handle permission errors, missing files and conflicting filenames gracefully.
- Display clear success and error notifications.

Use `gio trash` for trash operations with a suitable freedesktop fallback.

### Dialogs and properties

Provide graphical dialogs for:

- File and directory properties.
- Rename operations.
- Creating files and directories.
- Delete confirmations.
- Search.
- Open-with application selection.
- Error and warning messages.

Properties must display relevant information such as:

- Name and full path.
- File type and MIME information.
- File size.
- Modification time.
- Permissions.
- Symlink target and status, where applicable.

Provide visual indicators for symlinks, broken links and inaccessible files.

### Opening files and applications

- Open files using `xdg-open`.
- Provide an Open With picker using installed `.desktop` entries.
- Support launching a shell from the application.
- Support opening files in an external editor such as `nano` or the configured default editor.
- External applications must be launched as subprocesses without requiring the user to leave the file explorer's graphical interface.
- Copy the current path to the system clipboard using available Linux clipboard integrations such as `wl-copy`, `xclip` or `xsel`.

### Devices and network locations

- Discover mounted devices.
- Discover available GVFS network shares.
- Display mounted devices and network locations in the sidebar.
- Allow browsing mounted locations.
- Allow unmounting supported devices and network shares through the GUI.
- Clearly indicate unavailable or disconnected locations.

## Data compatibility

The Rust port must deliberately reuse Ferret's existing data locations to maintain compatibility with the original application.

| Data | Location |
| --- | --- |
| Settings | `$XDG_DATA_HOME/fe/cli_settings` |
| Bookmarks | `$XDG_DATA_HOME/fe/bookmarks` (symlinks) |
| Legacy settings migration | `$XDG_CONFIG_HOME/fe` → `$XDG_DATA_HOME/fe` |

`cli_settings` must retain the same `key=value` format and support the existing `sidebar` and `hidden_files` keys.

The legacy `cli_sidebar` marker file must still be read for compatibility.

The Rust GUI must not introduce incompatible configuration formats or change existing data locations unnecessarily.

## Deliberate omissions

This implementation targets a standalone Rust desktop file explorer, not complete parity with every feature of the original Ferret application.

The following features are outside the initial scope:

- Original Avalonia GUI implementation.
- Portal integration.
- Startup service and system tray integration.
- Addon runtime and addon store.
- Addon forms, custom status sections and addon-specific progress components.
- Network server connection dialogs and on-demand mounting (`sftp://`, `smb://`, etc.). Already-mounted GVFS shares must remain discoverable and browsable.
- Image previews and thumbnail generation.
- Automatic filesystem watching and live refresh. Users can refresh manually using `F5` or `Ctrl+R`.
- Full content-based MIME detection. Extension-based classification is acceptable initially.
- Windows and macOS-specific integrations. The initial implementation is Linux-focused.

These omissions must not compromise the required desktop GUI or the core file-management functionality.

## Architecture notes

### `ferret-core`

`ferret-core` must remain independent of the GUI framework.

It owns:

- Filesystem access and path discovery.
- XDG directory handling.
- File and directory metadata.
- Natural sorting.
- Directory filtering and listing.
- Navigation history.
- Symlink bookmarks.
- Places and device discovery.
- Settings loading, saving and migration.
- File operations.
- Trash handling.
- Error reporting and operation results.

Core functionality must be reusable and testable without starting the graphical application.

### `ferret-gui`

`ferret-gui` owns all graphical application state and rendering using `egui`/`eframe`.

Suggested internal structure:

```text
ferret-gui/
└── src/
    ├── main.rs          # Application entry point and CLI parsing
    ├── app.rs           # Main application state and lifecycle
    ├── ui/
    │   ├── mod.rs
    │   ├── toolbar.rs   # Navigation and action toolbar
    │   ├── sidebar.rs   # Places, bookmarks, devices and network
    │   ├── explorer.rs  # File lists and pane rendering
    │   ├── dialogs.rs   # Properties, confirmation and other dialogs
    │   ├── menus.rs     # Context menus and application menus
    │   └── status.rs    # Status bar and operation feedback
    ├── operations.rs    # GUI integration for background operations
    └── shortcuts.rs     # Keyboard shortcut handling
```

The exact internal structure can be adjusted when implementation requires it, but the separation between core functionality and GUI presentation must remain clear.

### Responsiveness and background operations

Filesystem operations must not block the GUI event loop.

- Perform potentially slow operations in background workers.
- Send operation results and progress updates back to the GUI safely.
- Update the interface when directory contents change.
- Handle cancellation and errors without crashing the application.
- Keep UI state consistent when operations finish or fail.

### User experience

The application should feel like a real desktop file manager rather than a basic GUI wrapper around terminal commands.

Prioritize:

- Clear visual hierarchy.
- Consistent spacing and alignment.
- Intuitive navigation.
- Useful context menus.
- Responsive layouts.
- Accessible keyboard shortcuts.
- Clear operation feedback.
- Appropriate confirmation for destructive actions.

The two-pane layout, sidebar, file lists, dialogs and toolbar should work together as one coherent desktop application.

## License

MIT, matching the rest of Ferret.
