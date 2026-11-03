# Ferret File-Explorer

A termnial based file-explorer with the goal of replacing the use of 'cd' and 'ls'

Now also with a GUI to go with it!

Work in progress, report all issues in the issues tab

## Building from source

The required compatibility libraries are included in this repository. Build with
`dotnet build FileExplorer.sln -c Release -m:1`; see the
[library and verification guide](_docs/LIBRARIES.md) for Linux tests and credential storage.

## Fast Linux startup

The full Linux packages keep a prepared Home window in the background after
login. Toggle the panel icon with **View → Show tray icon**; Ferret stays ready
with the icon hidden too. Use **File → Quit Ferret** or `ferret --quit` to exit.
See the [startup guide](_docs/STARTUP.md) for building and verification.

## Addons

Add your own entries to the right-click and Addons menus, and your own commands to the path bar, that run scripts or programs, without rebuilding Ferret.

See the [Addon guide](_docs/ADDONS.md) for all options, and [examples/addons](examples/addons) for ready-to-use examples.

If you want to publish your addon on the official Addon-Store, then check out the [addons repo](https://github.com/BonitoCat/Ferret-Addons).
