# Addons

Addons add your own entries to Ferret's right-click menu and **Addons** menu, and your own commands to the path bar. Each entry runs a script or a program, so you can add features like "Open in VS Code" or "Serve this folder" without rebuilding Ferret.

Scripts can also talk back to Ferret: show progress bars, ask questions, select files or finish a file picker. With hooks, an addon can run scripts when it is installed, loaded or uninstalled, and when a window opens or closes.

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
| `modes`       | Where the addon runs: `explorer`, `picker`, … (see [modes](#where-addons-run))         | `[explorer]`       |
| `hooks`       | Scripts that run at certain moments (see [hooks](#lifecycle-hooks))                    |                    |
| `service`     | Script that keeps running in the background (see [services](#background-services))    |                    |
| `settings`    | Options the user can change (see [settings](#settings))                                |                    |
| `places`      | Sidebar entries (see [sidebar places](#sidebar-places))                                |                    |
| `actions`     | List of menu entries and path bar commands (see below)         | one Addons-menu entry running `script` |

**Action fields**

| Field         | Description                                                                 | Default         |
|---------------|-----------------------------------------------------------------------------|-----------------|
| `id`          | Unique id within the addon                                                  | position number |
| `title`       | Menu text                                                                   | addon name      |
| `location`    | `context-menu`, `addons-menu` or `path-bar`                                 | `context-menu`  |
| `keywords`    | Words that run it from the path bar (`path-bar` only)                       | action `id`     |
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
| `icon`        | Icon for this entry                                                         | none            |
| `after`       | Place it after this menu entry (see placement)                              |                 |
| `before`      | Place it before this menu entry                                             |                 |
| `index`       | Fixed position in the context menu (see placement)                          | end             |
| `separator`   | `before`, `after`, `both` or `none`                                         | `none`          |
| `modes`       | Where this action is offered (see [modes](#where-addons-run))               | addon `modes`   |

**Targets**

| Target       | Shown when you right-click…                  |
|--------------|----------------------------------------------|
| `file`       | files (all selected items must match)        |
| `folder`     | folders                                      |
| `background` | empty space in a folder                      |

### Addon store

Addons → Browse Addon Store lists every verified addon from [Ferret-Addons](https://github.com/BonitoCat/Ferret-Addons). To publish yours, open a pull request there that adds your addon folder as `addons/<id>/` (plain files, no zip). Store addons need an `id` and a `version`; raise `version` to ship an update. Downloads are checked against their SHA-256 hash before they are installed.

### Placement in the context menu

The easiest way is to place your entry next to a built-in one with `after:` or `before:`:

```yaml
after: open-terminal
```

Built-in entry names:

```
new  open  open-with  open-as-root  open-terminal  copy-path  cut  copy  paste
paste-link  duplicate  rename  compress  extract-here  trash  delete-permanent  bookmark
folder-color  share  properties
```

- **Other addons:** to anchor to another addon's entry, use `<addon-id>/<action-id>`.
- **Fallback:** if the anchor isn't in the current menu, `index` is used instead.
- **Without placement:** entries with neither `after`/`before` nor `index` go into their own group above **Properties**.
- **Counting with `index`:** `index` counts every visible entry, including separators, starting at 0.
- **Index conflicts:** when two addons want the same spot, the one higher up in **Manage Addons** wins and the other moves down.

### Where addons run

Ferret is also used as a file picker: when **View → Use as System File Picker** is on, it is the "Open…"/"Save as…" dialog of other apps, and Ferret itself opens one for things like **Install Addon from Folder…**. By default addons only show up in the normal explorer window. With `modes` you choose where they show up:

| Mode            | Window                                   |
|-----------------|------------------------------------------|
| `explorer`      | the normal Ferret window                 |
| `file-picker`   | a picker for opening files               |
| `folder-picker` | a picker for choosing a folder           |
| `save-picker`   | a "Save as" picker                       |
| `picker`        | all three pickers                        |
| `all`           | everywhere                               |

```yaml
modes: [picker]          # this addon only exists in pickers
actions:
  - id: newest
    title: Pick newest file
    targets: [background]
    script: scripts/newest.sh
  - id: copy-path
    title: Copy path
    modes: [all]         # an action can override the addon's modes
    command: wl-copy
    args: ["{path}"]
```

- **Where addons appear in a picker:** the right-click menu, keyboard shortcuts and path bar keywords (`location: path-bar`). Pickers have no menu bar, so `addons-menu` actions never show up there.
- **What the script knows:** `FE_MODE` is `file-picker`, `folder-picker` or `save-picker`.
- **Finishing the picker:** `fe:pick <path>` closes the picker and hands the path to the app, as if the user had picked it. Relative paths start from the current folder.

| Picker          | `fe:pick` accepts                                      |
|-----------------|--------------------------------------------------------|
| `file-picker`   | an existing file                                       |
| `folder-picker` | an existing folder                                     |
| `save-picker`   | a file name in an existing folder; if the file already exists, the user is asked whether to replace it |

Anything else is ignored and reported in **Addon Problems**. In the explorer window `fe:pick` does nothing.

```bash
#!/usr/bin/env bash
newest=$(ls -t | head -1)
[ -n "$newest" ] || exit 0
echo "fe:confirm Pick $newest?"
read -r answer
[ "$answer" = yes ] && echo "fe:pick $newest"
```

### Path bar commands

Click the path bar (or press `Ctrl+L`), type a command and press Enter. Ferret runs it in the folder you have open, then puts the path back.

Ferret checks what you typed in this order:

1. **A location:** a path, a subfolder of the current folder, or a special location like `trash:///`. Ferret goes there.
2. **An addon keyword:** runs that addon action. If two addons use the same keyword, the one higher up in **Manage Addons** wins.
3. **A built-in command** (see below).
4. **`terminal`, `cmd` or `term`:** opens a terminal.
5. **A program from your `PATH`:** starts it, e.g. `code .` or `gimp photo.png`.

Built-in commands:

| Command                        | Effect                                                          |
|--------------------------------|-----------------------------------------------------------------|
| `select <pattern>` / `sel`     | Select matching items, e.g. `select *.jpg *.png`. Text without `*` or `?` matches anywhere in the name. No pattern selects everything |
| `new <name>` / `mkdir <name>`  | Create a folder and select it (no name opens the dialog)        |
| `touch <name>` / `newfile <name>` | Create a file and select it (no name opens the dialog)       |
| `find <text>` / `search <text>` | Search the current folder (no text focuses the search box)     |
| `hidden`                       | Show or hide hidden files                                       |
| `split` / `dual`               | Toggle the second pane                                          |
| `grid` / `list`                | Switch the view                                                 |
| `root`                         | Open the current folder as root                                 |
| `props` / `properties`         | Properties of the selection, or of the current folder           |
| `colors` / `theme`             | Theme colors                                                    |
| `addons`                       | Manage Addons                                                   |

Built-in commands replace programs with the same name (`find`, `split`, `mkdir`, `touch`). To run the program instead, start with `>`, e.g. `>find . -name "*.log"`.

Start with `>` to run a command inside a terminal that stays open, e.g. `>htop` or `>git log`.

An addon adds a command with `location: path-bar` and `keywords`:

```yaml
actions:
  - id: code
    location: path-bar
    keywords: [code, vscode]
    command: code
    args: ["{dir}", "{args}"]
```

Typing `code README.md` runs `code <current folder> README.md`. Path bar actions don't use `targets`, `extensions`, `multiple` or the placement fields. `{paths}`, `{path}` and `FE_SELECTION` are the selected items, or the current folder if nothing is selected.

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
- **Errors:** if the script exits with a non-zero code, the last line it printed to stderr is shown in the status bar, and the full output goes to **Addon Problems** (see [finding errors](#finding-errors)).

**Placeholders in `args`**

| Placeholder | Replaced with                                                   |
|-------------|-----------------------------------------------------------------|
| `{paths}`   | Every selected path as separate arguments (or the current folder) |
| `{path}`    | The first selected path (or the current folder)                 |
| `{name}`    | File name of `{path}`                                           |
| `{dir}`     | The folder currently open                                       |
| `{addon}`   | The addon's own folder                                          |
| `{input}`   | Path bar only: the text typed after the keyword, as one argument |
| `{args}`    | Path bar only: the text typed after the keyword, split into words |

**Environment variables**

| Variable         | Value                                              |
|------------------|----------------------------------------------------|
| `FE_ADDON_ID`    | Addon id                                           |
| `FE_ADDON_DIR`   | Absolute path of the addon folder                  |
| `FE_ACTION_ID`   | Id of the action that was clicked                  |
| `FE_CURRENT_DIR` | The folder currently open                          |
| `FE_SELECTION`   | Selected paths, one per line (empty for background)|
| `FE_TARGET`      | `file`, `folder`, `background` or `mixed`          |
| `FE_INPUT`       | Path bar only: the text typed after the keyword    |
| `FE_LANGUAGE`    | Ferret's UI language, e.g. `en` or `de`            |
| `FE_PID`         | Process id of the Ferret window                    |
| `FE_MODE`        | `explorer`, `file-picker`, `folder-picker` or `save-picker` |
| `FE_HOOK`        | Name of the hook that is running (empty for actions) |
| `FE_HOME_DIR`    | The user's home folder, e.g. `/home/you`           |
| `FE_CACHE_DIR`   | The user's cache folder, e.g. `/home/you/.cache` (`$XDG_CACHE_HOME` if set) |
| `FE_DATA_DIR`    | Ferret's data folder, e.g. `/home/you/.local/share/fe` (`$XDG_DATA_HOME/fe` if set) |
| `FE_SERVICE`     | `1` for the [background service](#background-services), empty otherwise |
| `FE_SETTING_<ID>`| The value of each [setting](#settings), e.g. `FE_SETTING_API_KEY` |

### Talking back to Ferret

A script can control Ferret by printing these lines to stdout. Ferret ignores all other output.

| Line                 | Effect                                            |
|----------------------|---------------------------------------------------|
| `fe:navigate <path>` | Open a folder (relative paths start from the current folder) |
| `fe:refresh`         | Reload the current folder                         |
| `fe:status <text>`   | Show text in the status bar                       |
| `fe:message <text>`  | Show a message dialog (`\n` for line breaks)      |
| `fe:open <path>`     | Open a file with its default application          |
| `fe:prompt <text>`   | Ask the user for text (see below)                 |
| `fe:confirm <text>`  | Ask OK / Cancel (see below)                       |
| `fe:choose <text> \| <option> \| …` | Let the user pick one option (see [choosing](#choosing-one-option)) |
| `fe:form …`          | Ask for several values at once (see [forms](#forms)) |
| `fe:statusbar <id> <text>` | Show an item in the status bar (see [status bar](#status-bar-items)) |
| `fe:place <section> \| <name> \| <path>` | Add a sidebar entry (see [sidebar places](#sidebar-places)) |
| `fe:setting <id> <value>` | Save a setting (see [settings](#settings))  |
| `fe:progress …`      | Show a progress bar (see [progress](#progress-bars)) |
| `fe:loading on\|off` | Show the thin loading bar above the file list    |
| `fe:select <pattern>`| Select matching items in the current folder (see below) |
| `fe:pick <path>`     | Pickers only: finish the picker with this path    |

A line that starts with `fe:` but isn't one of these is reported in **Addon Problems**, so typos like `fe:stauts` don't go unnoticed.

- **One line per command:** each command is one line, and the text after the command is used as-is (no quotes needed, `\n` for line breaks in messages).
- **`fe:select`** works like the path bar `select` command: several patterns separated by spaces, `*` and `?` as wildcards, and text without wildcards matches anywhere in the name. `fe:select *.jpg *.png` selects all JPEG and PNG files; `fe:select` alone selects everything. The selection replaces the current one.
- **`fe:loading`** accepts `on`/`off` (also `true`/`false`, `1`/`0`). The bar stays visible as long as any script has it on.

### Asking for input

`fe:prompt <text>` opens a small dialog with your text, an input field, and OK and Cancel buttons. Ferret sends the answer back to your script's stdin as a single line. If the user clicks Cancel, Ferret closes stdin, so `read` fails. That way an empty answer and a cancel can be told apart.

```bash
echo "fe:prompt Name of the new branch:"
if ! IFS= read -r branch; then
    exit 0
fi

git switch -c "$branch"
```

Prompts only work for actions without `terminal: true`. In a terminal, use `read` directly.

### Asking for confirmation

`fe:confirm <text>` shows your text with OK and Cancel. Ferret answers on stdin with `yes` or `no`, so you can ask several times in one script:

```bash
echo "fe:confirm Delete $(wc -l < list.txt) files?"
read -r answer
[ "$answer" = yes ] || exit 0
```

`confirm:` in addon.yaml asks once before the script starts; `fe:confirm` lets you ask in the middle, once you know what you are about to do.

### Choosing one option

`fe:choose <text> | <option> | <option> …` shows your text and the options. Ferret answers on stdin with the text of the chosen option, exactly as you wrote it. Cancel closes stdin, like `fe:prompt`.

```bash
echo "fe:choose Convert to which format? | webp | png | jpg"
IFS= read -r format || exit 0
```

- **Look:** up to 6 options are shown as radio buttons, more as a drop-down list. The first option is selected at the start.
- **Rules:** options are separated by `|` and trimmed, so `|` can't appear inside an option. You need at least one option; `fe:choose` without options is reported in **Addon Problems** and answered like a cancel.

### Forms

A form asks for several values in one dialog. Describe it line by line, then show it:

```bash
echo "fe:form Export settings"
echo "fe:field name | text | File name | export"
echo "fe:field quality | number | Quality (1-100) | 80"
echo "fe:field strip | bool | Remove metadata | true"
echo "fe:field format | choice | Format | png,*webp,jpg"
echo "fe:field target | folder | Save in"
echo "fe:form-show"
IFS=$'\t' read -r name quality strip format target || exit 0
```

| Line | Effect |
|------|--------|
| `fe:form <text>` | Start a new form; `<text>` is shown above the fields (may be empty) |
| `fe:field <id> \| <type> \| <label> \| <default>` | Add a field. Only `id` and `type` are required; `label` defaults to the id |
| `fe:form-show` | Show the form and wait for the user |

**Field types**

| Type       | Input                  | Default / answer |
|------------|------------------------|------------------|
| `text`     | text box               | any text |
| `password` | text box with hidden characters | any text |
| `number`   | number box with arrows | a number with a dot as decimal point, e.g. `2.5` |
| `bool`     | check box              | `true` or `false` (`yes`/`no`, `on`/`off` and `1`/`0` are accepted as defaults) |
| `choice`   | radio buttons (up to 6) or a drop-down list | options separated by commas; mark the default with `*`, e.g. `small,*medium,large`. Without `*` the first option is the default |
| `file`     | text box and a **…** button that opens a Ferret file picker | a path (`~/` is expanded) |
| `folder`   | text box and a **…** button that opens a Ferret folder picker | a path (`~/` is expanded) |

- **Answer:** when the user clicks OK, Ferret sends **one line** with all values in field order, separated by tabs. Tabs and line breaks the user typed become spaces. Read it with `IFS=$'\t' read -r a b c`.
- **Cancel:** closes stdin, like `fe:prompt`.
- **Starting over:** `fe:form` always starts a new, empty form. `fe:field` lines without `fe:form` before them start a form without text.
- **Mistakes:** a field with an unknown type or a `choice` field without options is skipped and reported in **Addon Problems**. `fe:form-show` without fields is reported too and answered like a cancel.

### Progress bars

Your script can show a progress bar at the bottom of the window, like the one for copying files. When several bars are running at once (two addons, or an addon and a copy), they show up as tabs above the bar, and you can click them or use the arrows to switch between them.

```
┌───────────────────────────────────────────────────────────────┐
│ [icon] title                                          status  │
│        ███████████████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  [Cancel]│
│        text                                             item  │
└───────────────────────────────────────────────────────────────┘
```

| Line                          | Effect                                                         |
|-------------------------------|----------------------------------------------------------------|
| `fe:progress <n>`             | Set the progress to `n` (out of 100, or out of `progress-max`) |
| `fe:progress <n>/<max>`       | Set progress and maximum at once, e.g. `fe:progress 3/12`      |
| `fe:progress <n>/<max> <text>`| …and set the text below the bar, e.g. `fe:progress 3/12 3 of 12 files` |
| `fe:progress busy`            | Animated bar for when you don't know how long it takes (`fe:progress` alone does the same) |
| `fe:progress done`            | Remove the bar (the script keeps running)                      |
| `fe:progress-max <max>`       | Set the maximum                                                |
| `fe:progress-title <text>`    | Title (default: the action's title; empty text resets it)      |
| `fe:progress-text <text>`     | Text below the bar, left                                       |
| `fe:progress-status <text>`   | Text above the bar, right (e.g. speed or time left)            |
| `fe:progress-item <text>`     | Text below the bar, right (e.g. the current file)              |

- **When it shows:** the bar appears with the first of these lines (as a `busy` bar until you send a number) and disappears when your script exits or sends `fe:progress done`.
- **Numbers:** whole or decimal numbers with a dot (`2.5`), never negative. A trailing `%` is allowed (`fe:progress 40%`). The percentage in the tab is `n / max`.
- **One bar per run:** every time an action or hook runs, it gets its own bar. Running the same action twice gives two tabs.
- **Icon:** the addon's `icon`, or a puzzle piece if it has none.
- **Mistakes:** invalid values like `fe:progress abc` are ignored and reported in **Addon Problems**. **Cancel** sends `SIGTERM` to your script and everything it started, and kills them 3 seconds later if they are still running. Trap `TERM` to clean up:

```bash
files=("$@")
trap 'echo "fe:status Conversion cancelled"; exit 0' TERM
echo "fe:progress-title Converting ${#files[@]} images"
for i in "${!files[@]}"; do
    echo "fe:progress $i/${#files[@]}"
    echo "fe:progress-item $(basename "${files[$i]}")"
    convert "${files[$i]}" "${files[$i]%.*}.webp"
done
echo "fe:refresh"
```

`fe:loading on` shows the thin animated bar above the file list (the one Ferret shows while a folder loads) until `fe:loading off` or until your script exits.

### Lifecycle hooks

Hooks are scripts that run at certain moments instead of from a menu. Each hook is a path to a script inside the addon folder; it runs with the addon's `interpreter` (or its shebang / extension, like actions). Hooks can't use `command` or `args`.

```yaml
hooks:
  post_install: scripts/setup.sh
  pre_uninstall: scripts/cleanup.sh
  post_load: scripts/load.sh
  post_start: scripts/start.sh
  pre_close: scripts/close.sh
```

| Hook            | Runs                                                                 | Time limit |
|-----------------|----------------------------------------------------------------------|------------|
| `post_install`  | after the addon is installed or updated (folder, zip or addon store) | 5 minutes  |
| `pre_uninstall` | before the addon is uninstalled; it is removed even if the hook fails | 30 seconds |
| `post_load`     | when the addon becomes active in a window: when the window opens, after installing or updating it, and when it is enabled again | none |
| `post_start`    | when a Ferret window opens                                           | none       |
| `pre_close`     | when a Ferret window closes; the window waits for it                 | 3 seconds  |

- **Where hooks run:** hooks run inside the addon folder (not in the user's current folder). They get the same environment variables as actions: `FE_HOOK` is the hook's name (`post_install`, …), `FE_ACTION_ID` is empty, and `FE_CURRENT_DIR`/`FE_SELECTION` describe the window at that moment. They can use all `fe:` lines, e.g. `fe:message` or a progress bar in `post_install`.
- **Missing scripts:** if a hook's script doesn't exist, the addon gets an error (shown in **Manage Addons** and **Addon Problems**) and doesn't load.
- **Time limits:** a hook that is still running when its time is up is stopped like a cancelled action, and a warning goes to **Addon Problems**.
- **Every window:** each Ferret window is its own process, so `post_load`, `post_start` and `pre_close` run once per window. They only run in windows that match the addon's `modes`, so an addon with `modes: [picker]` gets `post_start` when a picker opens.
- **Updates:** updating an addon does not run `pre_uninstall` for the old version; it runs `post_install` (and then `post_load`) for the new one.
- **Disabled addons:** `post_load`, `post_start` and `pre_close` don't run for disabled addons or addons with errors. `pre_uninstall` always runs.

Hook names can also be written as `post-install` or `postInstall`.

```bash
#!/usr/bin/env bash
# scripts/setup.sh (post_install): check a dependency once
if ! command -v ffmpeg >/dev/null; then
    echo "fe:message This addon needs ffmpeg.\nInstall it with: sudo apt install ffmpeg"
fi
```

### Background services

A service is a script that starts with Ferret and keeps running. It gets told what the user does (open folders, select files, click its status bar item) and can react with the usual `fe:` lines. Use it for things that should always be up to date, such as a git branch in the status bar or a sync status.

```yaml
service: scripts/service.sh
```

Ferret writes **events** to the service's stdin, one per line:

| Event | When |
|-------|------|
| `event:navigate <path>` | at start, and whenever the open folder changes (also when switching between split panes) |
| `event:selection <paths>` | at start, and when the selection changes; paths are separated by tabs, empty when nothing is selected |
| `event:click <id>` | the user clicked the service's [status bar item](#status-bar-items) `<id>` |
| `event:reply <text>` | the answer to `fe:prompt`, `fe:confirm` (`yes`/`no`), `fe:choose` or a form |
| `event:cancel` | the user cancelled `fe:prompt`, `fe:choose` or a form |
| `event:setting <id> <value>` | a [setting](#settings) was changed (one line per changed setting) |
| `event:close` | Ferret is about to stop the service; exit now |

```bash
#!/usr/bin/env bash
# scripts/service.sh: show the git branch of the open folder
while IFS= read -r line; do
    case "$line" in
        "event:navigate "*)
            dir="${line#event:navigate }"
            branch=$(git -C "$dir" branch --show-current 2>/dev/null)
            echo "fe:statusbar branch $branch"
            ;;
        "event:click branch")
            echo "fe:message $(git -C "$dir" status --short | head -20)"
            ;;
        "event:close")
            exit 0
            ;;
    esac
done
```

- **When it runs:** the service starts when the window opens (and when the addon is installed, updated or enabled) and stops when the window closes or the addon is disabled or uninstalled. **Addons → Reload Addons** restarts it. Like hooks, it only runs in windows that match the addon's `modes`, and each Ferret window (process) runs its own copy.
- **Where it runs:** inside the addon folder, with `FE_SERVICE=1` and the same environment variables as hooks. `FE_CURRENT_DIR`/`FE_SELECTION` describe the window at start, so use the events to follow along.
- **Selection events** are sent 150 ms after the selection stops changing, and only when it really changed. Navigation events are only sent when the folder really changed.
- **Dialogs:** a service has to keep reading stdin for events, so answers to its `fe:prompt`, `fe:confirm`, `fe:choose` and forms arrive as `event:reply`/`event:cancel` instead of a plain line (and Cancel never closes stdin). The rest of the events keep coming while a dialog is open.
- **Stopping:** after `event:close`, Ferret closes stdin and waits up to 2 seconds for the service to exit, then sends `SIGTERM` to it and everything it started (`SIGKILL` one second later). Stopping a service this way is not an error.
- **Crashes:** if the service exits on its own, it is not restarted until the next reload, update, or until the addon is enabled again. A non-zero exit code is reported in **Addon Problems**. Its status bar items and the sidebar places it added disappear.
- **Slow services:** Ferret never waits for your service to read. If it falls more than 500 events behind, the oldest ones are dropped.
- **Missing script:** if `service` points to a file that doesn't exist, the addon gets an error and doesn't load.

### Status bar items

`fe:statusbar <id> <text>` shows a small item with your addon's icon and the text at the right side of the status bar. The first word is the id, the rest is the text. Use the same id again to change the text.

| Line | Effect |
|------|--------|
| `fe:statusbar <id> <text>` | Add the item or change its text |
| `fe:statusbar <id>` | Remove the item (empty text) |
| `fe:statusbar-tooltip <id> <text>` | Tooltip of the item (`\n` for line breaks, empty text removes it) |

- **Clicks:** if the addon has a running service, a click sends `event:click <id>` to it. Otherwise, a click runs the addon's action whose `id` is the same as the item's id (any `location`). If neither exists, the item can't be clicked.
- **Lifetime:** items from a service disappear when the service stops. Items from actions and hooks stay until you remove them, the addon is disabled or uninstalled, or the window closes. Items belong to the window that ran the script.
- **Look:** the icon is the addon's `icon` (none if it has none). Long texts are cut off with `…`, so keep them short. Items appear in the order they were added; the status bar is hidden in pickers.
- **Mistakes:** `fe:statusbar` without an id, and a tooltip for an item that doesn't exist, are reported in **Addon Problems**.

### Sidebar places

Addons can add their own sections to the sidebar, below **Places**, **Devices** and **Network**. Each section can be collapsed, and the sections move together when the user drags the sidebar sections around. Clicking an entry opens it, and right-clicking offers **Open**. Users can't reorder or remove addon entries.

```yaml
places:
  - name: Projects
    path: ~/dev
    icon: folder-documents
  - name: Server logs
    path: /var/log
    section: Server
```

| Field     | Description | Default |
|-----------|-------------|---------|
| `name`    | Text of the entry (required, `$Key` translates it) | |
| `path`    | Folder to open (required). `~/` is the home folder; `smb://…` and other URLs work too | |
| `section` | Section title, shown in capitals (`$Key` translates it) | addon name |
| `icon`    | An image inside the addon folder, or a built-in icon name such as `home`, `folder`, `folder-documents`, `folder-music`, `desktop`, `network`, `disk`, `terminal`, `bookmark`, `trash` | `folder` |

Scripts can add and remove entries while running:

| Line | Effect |
|------|--------|
| `fe:place <section> \| <name> \| <path> \| <icon>` | Add an entry, or change the entry with the same section and name. `icon` is optional; relative paths start from the current folder |
| `fe:place-remove <section> \| <name>` | Remove one entry |
| `fe:place-remove <section>` | Remove every entry the addon added to this section |

- **Lifetime:** entries from `fe:place` belong to the window that ran the script. Entries added by a service disappear when it stops; others stay until removed, the addon is disabled or uninstalled, or the window closes. `fe:place-remove` only removes entries added with `fe:place`, not the ones from `addon.yaml`.
- **Mistakes:** an entry in `addon.yaml` without `name` or `path` is an addon error. `fe:place` with fewer than three parts is reported in **Addon Problems**.

### Settings

Settings let users configure your addon without editing files. They appear in **Manage Addons → Settings…** (the button is disabled for addons without settings), with **Reset to defaults**, **Cancel** and **Save**.

```yaml
settings:
  - id: api_key
    title: $ApiKey
    description: Create one at example.com/keys
    type: password
  - id: quality
    title: Quality
    type: number
    default: 80
    min: 1
    max: 100
  - id: format
    title: Format
    type: choice
    choices: [webp, png, jpg]
    default: webp
```

| Field         | Description | Default |
|---------------|-------------|---------|
| `id`          | Unique id within the addon (required) | |
| `title`       | Label above the input (`$Key` translates it) | id |
| `description` | Small text below the input (`$Key` translates it) | |
| `type`        | `text`, `password`, `number`, `bool`, `choice`, `file` or `folder` (see [field types](#forms)) | `text` |
| `default`     | Value before the user changes it | empty, `false`, the minimum (or `0`), or the first choice |
| `choices`     | Options of a `choice` setting (required for `choice`) | |
| `min`, `max`  | Limits of a `number` setting | none |

- **Reading settings:** every action, hook and service gets each setting as an environment variable: `FE_SETTING_` followed by the id in capitals, with everything except letters and digits turned into `_`. `api_key` becomes `FE_SETTING_API_KEY`, `max-size` becomes `FE_SETTING_MAX_SIZE`. Settings the user never changed have their default value.
- **Values:** always text. Numbers use a dot as decimal point and are kept within `min`/`max`, booleans are `true` or `false`, choices are one of the options, and `~/` in `file`/`folder` values is expanded.
- **Changes:** a running service gets `event:setting <id> <value>` for each setting that really changed. Other scripts see the new value the next time they run.
- **Saving from a script:** `fe:setting <id> <value>` saves a value, e.g. a token after a login. The value is checked like above. An unknown id is reported in **Addon Problems**.
- **Storage:** settings are stored in Ferret's data folder (`addon_settings.json`), not in the addon folder, so they survive updates. Uninstalling the addon deletes them.
- **Mistakes:** a setting without `id`, with an unknown `type`, a `choice` setting without `choices`, or two settings with the same id give the addon an error.

**Tip:** don't write files into your addon folder while Ferret is running (logs, caches, …). Ferret watches that folder and reloads all addons when something in it changes. Use a folder named after your addon inside `$FE_CACHE_DIR` or `$FE_DATA_DIR` instead, e.g. `mkdir -p "$FE_CACHE_DIR/$FE_ADDON_ID"`.

### Finding errors

When an addon fails, a warning icon with the number of new problems appears in the status bar (in pickers, next to the file name box). Click it, or open **Addons → Addon Problems…**, to see:

- actions and hooks that exited with an error, with everything they printed to stderr
- scripts that could not be started (with the exact command Ferret tried to run)
- mistakes in `addon.yaml` (unknown values, missing scripts, …)
- unknown `fe:` lines and invalid values such as `fe:progress abc`
- hooks that ran out of time
- services that exited with an error
- invalid `fe:field`, `fe:place`, `fe:setting`, … lines

Opening the list marks everything as seen, so the icon disappears until the next problem. The list itself keeps the last 200 entries of the current window until you click **Clear**. **Copy All** copies everything as text, which helps when you report a bug in an addon.

Cancelling an action yourself, or a script exiting with code `0`, is not a problem and isn't listed.

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
