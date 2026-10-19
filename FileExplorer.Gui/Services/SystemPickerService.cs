using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FileExplorer.Gui.Services;

public static class SystemPickerService
{
    private const string FileChooserKey = "org.freedesktop.impl.portal.FileChooser";
    private const string FileChooserValue = "ferret;gtk;";
    private const string PreferredSection = "[preferred]";
    private const string PortalDefinitionPath = "/usr/share/xdg-desktop-portal/portals/ferret.portal";

    public static bool IsInstalled => File.Exists(PortalDefinitionPath);

    public static bool IsEnabled
    {
        get
        {
            string path = UserConfigPath;
            return File.Exists(path) && File.ReadLines(path).Any(l => IsFileChooserLine(l) && l.Contains("ferret", StringComparison.Ordinal));
        }
    }

    private static string ConfigFileName
    {
        get
        {
            string desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':')[0].Trim().ToLowerInvariant();
            return desktop.Length == 0 ? "portals.conf" : $"{desktop}-portals.conf";
        }
    }

    private static string UserConfigPath
    {
        get
        {
            string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(configHome))
            {
                configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }

            return Path.Combine(configHome, "xdg-desktop-portal", ConfigFileName);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        string path = UserConfigPath;
        List<string> baseLines = ReadSystemConfig();
        List<string> lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new(baseLines);
        lines.RemoveAll(IsFileChooserLine);

        if (enabled)
        {
            int section = lines.FindIndex(l => l.Trim().Equals(PreferredSection, StringComparison.OrdinalIgnoreCase));
            if (section < 0)
            {
                lines.Insert(0, PreferredSection);
                section = 0;
            }

            lines.Insert(section + 1, $"{FileChooserKey}={FileChooserValue}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
        }
        else if (File.Exists(path))
        {
            if (Normalize(lines).SequenceEqual(Normalize(baseLines)))
            {
                File.Delete(path);
            }
            else
            {
                File.WriteAllLines(path, lines);
            }
        }

        RestartPortal();
    }

    private static List<string> ReadSystemConfig()
    {
        string? configDirs = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS");
        string? dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        IEnumerable<string> candidates = (string.IsNullOrEmpty(configDirs) ? "/etc/xdg" : configDirs)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Append("/etc")
            .Concat((string.IsNullOrEmpty(dataDirs) ? "/usr/local/share:/usr/share" : dataDirs).Split(':', StringSplitOptions.RemoveEmptyEntries))
            .Select(dir => Path.Combine(dir, "xdg-desktop-portal", ConfigFileName));

        string? systemConfig = candidates.FirstOrDefault(File.Exists);
        return systemConfig != null ? File.ReadAllLines(systemConfig).ToList() : [PreferredSection, "default=gtk;"];
    }

    private static bool IsFileChooserLine(string line)
    {
        return line.TrimStart().StartsWith(FileChooserKey + "=", StringComparison.Ordinal);
    }

    private static IEnumerable<string> Normalize(IEnumerable<string> lines)
    {
        return lines.Select(l => l.Trim()).Where(l => l.Length > 0);
    }

    private static void RestartPortal()
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "systemctl",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("--user");
            psi.ArgumentList.Add("try-restart");
            psi.ArgumentList.Add("xdg-desktop-portal.service");
            Process.Start(psi);
        }
        catch
        {
        }
    }
}
