using System;
using System.IO;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Gui.Services;

/// <summary>
/// Keeps the per-user XDG autostart entry in sync with the background setting.
/// </summary>
public static class BackgroundStartupService
{
    private const string DesktopFileName = "ferret.desktop";

    public static string AutostartPath
    {
        get
        {
            string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
            {
                configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }

            return Path.Combine(configHome, "autostart", DesktopFileName);
        }
    }

    public static void Apply()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            Write(GuiSettings.RunInBackground);
        }
        catch
        {
            // A read-only or missing config directory must not prevent startup.
        }
    }

    private static void Write(bool enabled)
    {
        string path = AutostartPath;
        string directory = Path.GetDirectoryName(path)!;

        if (!enabled)
        {
            // Override a distro-wide autostart entry without deleting package files.
            AppPaths.EnsureDir(directory);
            File.WriteAllText(path, """
                [Desktop Entry]
                Type=Application
                Name=Ferret
                Comment=Keep a prepared file explorer window ready
                Hidden=true

                """);
            return;
        }

        LaunchCommand? command = ResolveCommand();
        if (command == null)
        {
            // No stable launcher is available (for example a development build).
            // Leave any existing package or user entry untouched.
            return;
        }

        AppPaths.EnsureDir(directory);
        File.WriteAllText(path, $"""
            [Desktop Entry]
            Type=Application
            Name=Ferret
            Comment=Keep a prepared file explorer window ready
            Exec={command.Exec}
            Icon=fe-file-explorer
            Terminal=false
            NoDisplay=true
            X-GNOME-Autostart-enabled=true

            """);
    }

    private static LaunchCommand? ResolveCommand()
    {
        string? appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
        {
            return new LaunchCommand($"{Quote(appImage)} --background");
        }

        string? launcher = FindOnPath("ferret");
        if (launcher != null)
        {
            return new LaunchCommand($"{Quote(launcher)} --background");
        }

        string? process = Environment.ProcessPath;
        if (string.IsNullOrEmpty(process) || !File.Exists(process))
        {
            return null;
        }

        string name = Path.GetFileNameWithoutExtension(process);
        if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("FileExplorer.", StringComparison.OrdinalIgnoreCase) ||
            process.Contains("/.mount_", StringComparison.Ordinal) ||
            process.Contains("/tmp/.mount", StringComparison.Ordinal))
        {
            return null;
        }

        return new LaunchCommand($"{Quote(process)} --background");
    }

    private static string? FindOnPath(string name)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static string Quote(string value)
    {
        // Desktop Entry Exec values use double quotes, with % as the field-code prefix.
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("%", "%%") + "\"";
    }

    private sealed record LaunchCommand(string Exec);
}
