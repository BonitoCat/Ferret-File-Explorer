using System.Diagnostics;
using System.Text;

namespace FileExplorer.Core.Services;

public class MimeService
{
    public static string GetMimeType(string filePath)
    {
        if (File.Exists(filePath))
        {
            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = "xdg-mime",
                    Arguments = $"query filetype \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process proc = Process.Start(psi)!;
                string mime = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();

                if (!string.IsNullOrEmpty(mime))
                {
                    return mime;
                }
            }
            catch
            {
            }
        }

        string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
        {
            return "application/octet-stream";
        }

        return EnsureCustomMimeType(ext);
    }

    public static string EnsureCustomMimeType(string extension)
    {
        string cleanExt = extension.TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(cleanExt))
        {
            return "application/octet-stream";
        }

        string tempFile = Path.Combine(Path.GetTempPath(), $"fe_probe_{Guid.NewGuid():N}.{cleanExt}");
        try
        {
            File.WriteAllText(tempFile, "");
            string probed = GetMimeType(tempFile);
            if (!string.IsNullOrEmpty(probed) && probed != "application/octet-stream")
            {
                return probed;
            }
        }
        catch
        {
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
            catch
            {
            }
        }

        string customMime = $"application/x-{cleanExt}";
        string packagesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "mime", "packages");
        Directory.CreateDirectory(packagesDir);

        string xmlPath = Path.Combine(packagesDir, $"fe_{cleanExt}.xml");
        if (!File.Exists(xmlPath))
        {
            string xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">\n  <mime-type type=\"{customMime}\">\n    <comment>{cleanExt} file</comment>\n    <glob pattern=\"*.{cleanExt}\"/>\n  </mime-type>\n</mime-info>\n";
            File.WriteAllText(xmlPath, xml);

            try
            {
                string mimeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "mime");
                ProcessStartInfo updatePsi = new()
                {
                    FileName = "update-mime-database",
                    Arguments = $"\"{mimeRoot}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process updateProc = Process.Start(updatePsi)!;
                updateProc.WaitForExit();
            }
            catch
            {
            }
        }

        return customMime;
    }

    public static string EnsureDesktopFile(string programPathOrCommand)
    {
        string raw = programPathOrCommand.Trim();
        if (raw.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(raw);
        }

        string appName = Path.GetFileNameWithoutExtension(raw);
        string[] appDirs =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications"),
            "/usr/share/applications",
            "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "flatpak", "exports", "share", "applications"),
            "/var/lib/snapd/desktop/applications"
        ];

        foreach (string dir in appDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            string directPath = Path.Combine(dir, $"{appName}.desktop");
            if (File.Exists(directPath))
            {
                return $"{appName}.desktop";
            }

            try
            {
                string[] files = Directory.GetFiles(dir, "*.desktop");
                foreach (string f in files)
                {
                    string fName = Path.GetFileName(f);
                    if (fName.StartsWith($"{appName}.", StringComparison.OrdinalIgnoreCase) ||
                        fName.Equals($"{appName}.desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        return fName;
                    }
                }
            }
            catch
            {
            }
        }

        string userAppsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
        Directory.CreateDirectory(userAppsDir);

        string safeName = $"fe-{appName.ToLowerInvariant()}.desktop";
        string targetDesktopPath = Path.Combine(userAppsDir, safeName);

        if (!File.Exists(targetDesktopPath))
        {
            StringBuilder sb = new();
            sb.AppendLine("[Desktop Entry]");
            sb.AppendLine("Type=Application");
            sb.AppendLine($"Name={appName}");
            sb.AppendLine($"Exec=\"{raw}\" %U");
            sb.AppendLine("NoDisplay=true");
            sb.AppendLine("Terminal=false");

            File.WriteAllText(targetDesktopPath, sb.ToString());
        }

        return safeName;
    }

    public static void SetDefaultApplication(string filePathOrExtension, string program)
    {
        string mimeType;
        if (filePathOrExtension.StartsWith('.'))
        {
            mimeType = EnsureCustomMimeType(filePathOrExtension.TrimStart('.'));
        }
        else if (File.Exists(filePathOrExtension))
        {
            mimeType = GetMimeType(filePathOrExtension);
        }
        else
        {
            string ext = Path.GetExtension(filePathOrExtension);
            mimeType = !string.IsNullOrEmpty(ext) ? EnsureCustomMimeType(ext) : GetMimeType(filePathOrExtension);
        }

        if (string.IsNullOrEmpty(mimeType) || mimeType == "application/octet-stream")
        {
            string ext = Path.GetExtension(filePathOrExtension);
            if (!string.IsNullOrEmpty(ext))
            {
                mimeType = EnsureCustomMimeType(ext);
            }
        }

        string desktopFile = EnsureDesktopFile(program);

        UpdateMimeAppsList(mimeType, desktopFile);

        try
        {
            ProcessStartInfo gioPsi = new()
            {
                FileName = "gio",
                Arguments = $"mime \"{mimeType}\" \"{desktopFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process proc = Process.Start(gioPsi)!;
            proc.WaitForExit();
        }
        catch
        {
        }

        try
        {
            ProcessStartInfo xdgPsi = new()
            {
                FileName = "xdg-mime",
                Arguments = $"default \"{desktopFile}\" \"{mimeType}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process proc2 = Process.Start(xdgPsi)!;
            proc2.WaitForExit();
        }
        catch
        {
        }
    }

    private static void UpdateMimeAppsList(string mimeType, string desktopFile)
    {
        string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        Directory.CreateDirectory(configDir);
        string listPath = Path.Combine(configDir, "mimeapps.list");

        List<string> lines = File.Exists(listPath) ? [..File.ReadAllLines(listPath)] : [];

        int defHeaderIdx = lines.FindIndex(l => l.Trim().Equals("[Default Applications]", StringComparison.OrdinalIgnoreCase));
        if (defHeaderIdx < 0)
        {
            lines.Add("[Default Applications]");
            defHeaderIdx = lines.Count - 1;
        }

        int nextHeaderIdx = lines.FindIndex(defHeaderIdx + 1, l => l.Trim().StartsWith('[') && l.Trim().EndsWith(']'));
        if (nextHeaderIdx < 0)
        {
            nextHeaderIdx = lines.Count;
        }

        int existingLineIdx = -1;
        for (int i = defHeaderIdx + 1; i < nextHeaderIdx; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith($"{mimeType}=", StringComparison.OrdinalIgnoreCase))
            {
                existingLineIdx = i;
                break;
            }
        }

        string newLine = $"{mimeType}={desktopFile}";
        if (existingLineIdx >= 0)
        {
            lines[existingLineIdx] = newLine;
        }
        else
        {
            lines.Insert(defHeaderIdx + 1, newLine);
        }

        int addedHeaderIdx = lines.FindIndex(l => l.Trim().Equals("[Added Associations]", StringComparison.OrdinalIgnoreCase));
        if (addedHeaderIdx < 0)
        {
            lines.Add("");
            lines.Add("[Added Associations]");
            addedHeaderIdx = lines.Count - 1;
        }

        int nextAddedHeaderIdx = lines.FindIndex(addedHeaderIdx + 1, l => l.Trim().StartsWith('[') && l.Trim().EndsWith(']'));
        if (nextAddedHeaderIdx < 0)
        {
            nextAddedHeaderIdx = lines.Count;
        }

        int existingAddedIdx = -1;
        for (int i = addedHeaderIdx + 1; i < nextAddedHeaderIdx; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith($"{mimeType}=", StringComparison.OrdinalIgnoreCase))
            {
                existingAddedIdx = i;
                break;
            }
        }

        string newAddedLine = $"{mimeType}={desktopFile};";
        if (existingAddedIdx >= 0)
        {
            lines[existingAddedIdx] = newAddedLine;
        }
        else
        {
            lines.Insert(addedHeaderIdx + 1, newAddedLine);
        }

        try
        {
            File.WriteAllLines(listPath, lines);
        }
        catch
        {
        }
    }
}
