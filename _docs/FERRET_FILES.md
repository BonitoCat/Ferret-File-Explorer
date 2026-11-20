# Folder rules (`.ferret` files)

A `.ferret` file in a folder tells Ferret what to leave out when you copy or move that folder, and what to copy along with it. Think of it like a `.gitignore` that Ferret applies whenever you transfer the folder.

Folder rules currently work in the GUI only.

### Quick start

Put a file named `.ferret` into a project folder:

```ini
# Build output and dependencies don't need to travel with the project
[exclude]
bin/
obj/
node_modules/
*.log

# ...but keep this one log
[include]
logs/important.log
```

Now copy or move the project folder (paste, drag & drop, **Copy to…**, **Move to…**). Ferret skips `bin`, `obj`, `node_modules` and every `.log` file, except `logs/important.log`.

### When rules apply

- Rules apply when you copy or move a **folder**. Files you select directly are always transferred.
- Only `.ferret` files **inside** the folder you transfer count, including the one at its top. A `.ferret` file above it is ignored.
- `.ferret` files themselves are always copied, so the rules travel with the folder.
- When you **move** a folder, excluded items stay behind in the original folder, and the original folder is not deleted.
- To ignore all rules once, hold **Ctrl+Shift** while dropping.

## File format

- Lines are grouped under section headers like `[exclude]`. Section names are case-insensitive.
- Each line in a section is one pattern.
- Empty lines and lines starting with `#` are ignored, and so is anything before the first section.

### Sections

| Section     | Meaning |
|-------------|---------|
| `[exclude]` | Leave out everything that matches. |
| `[include]` | Keep everything that matches, even if `[exclude]` or `[only]` would leave it out. |
| `[only]`    | Leave out everything that does **not** match. Folders that lead to a match are kept. |
| `[bring]`   | Copy these items from outside the folder along with it. See [Bringing items along](#bringing-items-along). |

Within one `.ferret` file, `[include]` wins over `[exclude]`, and `[exclude]` wins over `[only]`.

### Patterns

Patterns work like `.gitignore` patterns, and are relative to the folder that holds the `.ferret` file.

| Pattern            | Matches |
|--------------------|---------|
| `*.log`            | any `.log` file, in any subfolder |
| `node_modules`     | anything named `node_modules`, in any subfolder |
| `build/`           | folders named `build`, in any subfolder (the trailing `/` means folders only) |
| `/notes.txt`       | `notes.txt` at the top only (a leading `/` anchors the pattern) |
| `docs/drafts`      | `docs/drafts` at the top only (any `/` inside anchors the pattern) |
| `assets/*.psd`     | `.psd` files directly in `assets` |
| `assets/**/*.psd`  | `.psd` files anywhere below `assets` |
| `data?.csv`        | `data1.csv`, `dataA.csv`, … |

- `*` matches any characters except `/`, `?` matches one character, `**` matches any number of folders.
- A pattern that matches a folder also covers everything inside it.
- Matching is case-sensitive.
- There is no `!` negation. Use `[include]` instead.

### Nested `.ferret` files

Subfolders can have their own `.ferret` file. For each item, Ferret asks the nearest `.ferret` file first. If that file has no rule for the item, the next one up decides, and so on up to the folder you transfer. That way a subfolder can re-include something its parent excludes:

```
project/
├── .ferret          [exclude] *.bin
└── firmware/
    ├── .ferret      [include] release.bin
    ├── release.bin  ← copied
    └── debug.bin    ← left out
```

## Example: only take the sources

```ini
[only]
src/
README.md
LICENSE
```

Only `src` (with everything in it), `README.md` and `LICENSE` are copied. Everything else in the folder is left out.

## Bringing items along

`[bring]` lists items **next to** the folder that should be copied along with it. Paths are relative to the folder holding the `.ferret` file, so they start with `../`:

```
dev/
├── shared-config/
└── my-app/
    ├── .ferret      [bring] ../shared-config
    └── ...
```

```ini
[bring]
../shared-config
```

Copying `my-app` to `/backup` creates `/backup/my-app` and also copies `dev/shared-config` to `/backup/shared-config`. Each item keeps the same position relative to the folder.

- Ferret lists the extra items and asks before copying them.
- Items are always **copied**, even when you move the folder.
- Only the `.ferret` file at the top of the transferred folder is read for `[bring]`.
- Entries are skipped when they don't exist, when they are absolute paths, or when they point into the folder itself (or to a folder containing it).
- If the destination needs administrator rights, Ferret asks before copying as administrator.
