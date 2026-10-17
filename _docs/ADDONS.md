# Addons

Addons add your own entries to Ferret's right-click menu and **Addons** menu. Each entry runs a script or a program, so you can add features like "Open in VS Code" or "Serve this folder" without rebuilding Ferret.

Addons currently work in the GUI only.

### Quick start

1. Open **Addons → Open Addons Folder** (`~/.local/share/fe/addons`).
2. Create a folder, e.g. `hello-world/`, containing an `addon.yaml`:

   ```yaml
   name: Hello World
   actions:
     - title: Say hello
       targets: [file, folder, background]
       command: notify-send
       args: ["Hello", "{name}"]
   ```

3. Right-click anything in Ferret: **Say hello** is there. Ferret picks up changes automatically, and **Addons → Reload Addons** forces a reload.

You can also install an addon via **Addons → Install Addon from Folder…** or **Install Addon from Zip…**. In **Addons → Manage Addons** you can enable, disable, reorder and uninstall addons.

### Folder layout

```
my-addon/
├── addon.yaml          required
├── icon.svg            optional (svg, png or jpg)
├── translations/       optional
│   ├── en.json
│   └── de.json
└── scripts/run.sh      any path inside the addon folder
```

### addon.yaml

**Addon fields**

| Field         | Description                                                    | Default            |
|---------------|----------------------------------------------------------------|--------------------|
| `id`          | Unique id                                                      | folder name        |
| `name`        | Display name                                                   | id                 |
| `description` | Shown in Manage Addons                                         |                    |
| `version`     | Any version string                                             |                    |
| `author`      | Your name                                                      |                    |
| `website`     | Link to the project                                            |                    |
| `icon`        | Path to an icon inside the addon folder                        |                    |
| `script`      | Default script for all actions (path inside the addon folder)  |                    |
| `interpreter` | Default interpreter for scripts (e.g. `python3`)               | detected           |
| `actions`     | List of menu entries (see below)                               | one Addons-menu entry running `script` |

**Action fields**

| Field         | Description                                                                 | Default         |
|---------------|-----------------------------------------------------------------------------|-----------------|
| `id`          | Unique id within the addon                                                  | position number |
| `title`       | Menu text                                                                   | addon name      |
| `location`    | `context-menu` or `addons-menu`                                             | `context-menu`  |
| `targets`     | When to show it: `file`, `folder`, `background`                             | `[file, folder]`|
| `extensions`  | Only show it for these file types, e.g. `[.png, .jpg]`                      | all files       |
| `multiple`    | Allow it when several items are selected                                    | `true`          |
| `command`     | Program from your `PATH` to run (instead of a script)                       |                 |
| `script`      | Script to run (path inside the addon folder, runs in the user's current folder) | addon `script`  |
| `interpreter` | Interpreter for this action's script                                        | addon `interpreter` |
| `args`        | Arguments passed to the command or script (see placeholders)                | `["{paths}"]`   |
| `terminal`    | Run inside a terminal window                                                | `false`         |
| `confirm`     | Ask this question before running                                            |                 |
| `shortcut`    | Keyboard shortcut, e.g. `Ctrl+Alt+C`                                        |                 |
| `icon`        | Icon for this entry                                                         | addon `icon`    |
| `after`       | Place it after this menu entry (see placement)                              |                 |
| `before`      | Place it before this menu entry                                             |                 |
| `index`       | Fixed position in the context menu (see placement)                          | end             |
| `separator`   | `before`, `after`, `both` or `none`                                         | `none`          |

**Targets**

| Target       | Shown when you right-click…                  |
|--------------|----------------------------------------------|
| `file`       | files (all selected items must match)        |
| `folder`     | folders                                      |
| `background` | empty space in a folder                      |

### Placement in the context menu

The easiest way is to place your entry next to a built-in one with `after:` or `before:`:

```yaml
after: open-terminal
```

Built-in entry names:

```
new  open  open-with  open-as-root  open-terminal  copy-path  cut  copy  paste
duplicate  rename  compress  extract-here  trash  delete-permanent  bookmark
folder-color  share  properties
```

- **Other addons:** to anchor to another addon's entry, use `<addon-id>/<action-id>`.
- **Fallback:** if the anchor isn't in the current menu, `index` is used instead.
- **Without placement:** entries with neither `after`/`before` nor `index` go into their own group above **Properties**.
- **Counting with `index`:** `index` counts every visible entry, including separators, starting at 0.
- **Index conflicts:** when two addons want the same spot, the one higher up in **Manage Addons** wins and the other moves down.

### Translations

Text that starts with `$` is looked up in `translations/<language>.json`. Any other text is shown as written.

```yaml
name: $AddonName
actions:
  - title: $OpenInVsCode
```

```json
{
  "AddonName": "Open in VS Code",
  "OpenInVsCode": "Open in VS Code"
}
```

Ferret looks up the current language first (e.g. `de-AT`, then `de`), then English (`en`). If none of them has the key, it shows the text itself.

### Running scripts

**What a script gets**
- **Working directory:** the folder the user currently has open in Ferret. Relative paths and commands in your script (`ls`, `./build.sh`, `git status`) act on that folder, not on the addon folder. To reach files that ship with your addon, use `$FE_ADDON_DIR` or `{addon}`.
- **Interpreter:** if `interpreter` isn't set, executable scripts run directly using their shebang. Otherwise Ferret picks the interpreter from the extension:
  - `.sh` → bash
  - `.py` → python3
  - `.js` → node
  - `.rb` → ruby
  - `.pl` → perl
  - `.lua` → lua
  - `.php` → php
  - `.ps1` → pwsh
- **Errors:** if the script exits with a non-zero code, the last line it printed to stderr is shown in the status bar.

**Placeholders in `args`**

| Placeholder | Replaced with                                                   |
|-------------|-----------------------------------------------------------------|
| `{paths}`   | Every selected path as separate arguments (or the current folder) |
| `{path}`    | The first selected path (or the current folder)                 |
| `{name}`    | File name of `{path}`                                           |
| `{dir}`     | The folder currently open                                       |
| `{addon}`   | The addon's own folder                                          |

**Environment variables**

| Variable         | Value                                              |
|------------------|----------------------------------------------------|
| `FE_ADDON_ID`    | Addon id                                           |
| `FE_ADDON_DIR`   | Absolute path of the addon folder                  |
| `FE_ACTION_ID`   | Id of the action that was clicked                  |
| `FE_CURRENT_DIR` | The folder currently open                          |
| `FE_SELECTION`   | Selected paths, one per line (empty for background)|
| `FE_TARGET`      | `file`, `folder`, `background` or `mixed`          |
| `FE_LANGUAGE`    | Ferret's UI language, e.g. `en` or `de`            |
| `FE_PID`         | Process id of the Ferret window                    |

### Talking back to Ferret

A script can control Ferret by printing these lines to stdout. Ferret ignores all other output.

| Line                 | Effect                                            |
|----------------------|---------------------------------------------------|
| `fe:navigate <path>` | Open a folder (relative paths start from the current folder) |
| `fe:refresh`         | Reload the current folder                         |
| `fe:status <text>`   | Show text in the status bar                       |
| `fe:message <text>`  | Show a message dialog (`\n` for line breaks)      |
| `fe:open <path>`     | Open a file with its default application          |

### Full example

```yaml
id: dev-tools
name: $Name
description: $Description
version: 1.0.0
author: You
icon: icon.svg
script: scripts/tools.sh

actions:
  - id: vscode
    title: $OpenInVsCode
    targets: [folder, background]
    command: code
    args: ["{path}"]
    after: open-terminal

  - id: serve
    title: $ServeFolder
    targets: [folder, background]
    terminal: true
    separator: before

  - id: checksum
    title: $Checksum
    targets: [file]
    extensions: [.iso, .img, .zip]
    multiple: false
    shortcut: Ctrl+Alt+H

  - id: clean
    title: $CleanBuild
    targets: [background]
    confirm: $CleanBuildConfirm
    location: addons-menu
```

```bash
#!/usr/bin/env bash
case "$FE_ACTION_ID" in
  serve)
    cd "$1" && python3 -m http.server 8000
    ;;
  checksum)
    echo "fe:message SHA256 of $(basename "$1"):\n$(sha256sum "$1" | cut -d' ' -f1)"
    ;;
  clean)
    rm -rf "$FE_CURRENT_DIR/bin" "$FE_CURRENT_DIR/obj"
    echo "fe:refresh"
    echo "fe:status Build output removed"
    ;;
esac
```

More examples are in [`examples/addons`](../examples/addons).
