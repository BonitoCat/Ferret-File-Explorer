using System.Diagnostics;
using System.Text;

namespace FileExplorer.Core.Services;

public class MimeService
{
    private const string CustomMimePrefix = "application/x-fe-ext-";

    private static readonly HashSet<string> GenericMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/octet-stream",
        "application/x-zerosize",
        "text/plain",
    };

    private static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string UserDataDir
    {
        get
        {
            string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return string.IsNullOrEmpty(dataHome) ? Path.Combine(UserHome, ".local", "share") : dataHome;
        }
    }

    private static IEnumerable<string> DataDirs
    {
        get
        {
            yield return UserDataDir;

            string? dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            if (string.IsNullOrEmpty(dataDirs))
            {
                dataDirs = "/usr/local/share:/usr/share";
            }

            foreach (string dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                yield return dir;
            }
        }
    }

    public static string GetMimeType(string filePath)
    {
        string ext = GetExtension(filePath);
        if (!string.IsNullOrEmpty(ext))
        {
            string? globMime = LookupGlobMimeType(ext);
            if (globMime != null)
            {
                return globMime;
            }
        }

        return SniffMimeType(filePath) ?? "application/octet-stream";
    }

    public static string GetAssociationMimeType(string filePath)
    {
        string ext = GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return SniffMimeType(filePath) ?? "application/octet-stream";
        }

        return LookupGlobMimeType(ext) ?? EnsureCustomMimeType(ext, SniffMimeType(filePath));
    }

    public static string EnsureCustomMimeType(string extension)
    {
        string ext = extension.TrimStart('.').ToLowerInvariant();
        return LookupGlobMimeType(ext) ?? EnsureCustomMimeType(ext, null);
    }

    private static string EnsureCustomMimeType(string ext, string? sniffedParent)
    {
        if (string.IsNullOrEmpty(ext))
        {
            return "application/octet-stream";
        }

        string safeExt = new(ext.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '+').ToArray());
        string customMime = CustomMimePrefix + safeExt.ToLowerInvariant();
        string packagesDir = Path.Combine(UserDataDir, "mime", "packages");
        string xmlPath = Path.Combine(packagesDir, $"fe_{safeExt.ToLowerInvariant()}.xml");
        if (File.Exists(xmlPath))
        {
            return customMime;
        }

        string? parent = GetParentMimeType(sniffedParent);
        StringBuilder xml = new();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">");
        xml.AppendLine($"  <mime-type type=\"{customMime}\">");
        xml.AppendLine($"    <comment>{ext.ToUpperInvariant()} file</comment>");
        if (parent != null)
        {
            xml.AppendLine($"    <sub-class-of type=\"{parent}\"/>");
        }

        xml.AppendLine($"    <glob pattern=\"*.{ext}\" weight=\"60\"/>");
        xml.AppendLine("  </mime-type>");
        xml.AppendLine("</mime-info>");

        try
        {
            Directory.CreateDirectory(packagesDir);
            File.WriteAllText(xmlPath, xml.ToString());
            RunProcess("update-mime-database", $"\"{Path.Combine(UserDataDir, "mime")}\"");
        }
        catch
        {
        }

        return customMime;
    }

    private static string? GetParentMimeType(string? sniffed)
    {
        if (string.IsNullOrEmpty(sniffed) || sniffed is "application/octet-stream" or "application/x-zerosize")
        {
            return null;
        }

        if (sniffed.StartsWith(CustomMimePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return sniffed;
    }

    public static string? LookupGlobMimeType(string extension)
    {
        string pattern = "*." + extension.TrimStart('.');
        string? bestMime = null;
        int bestWeight = -1;

        foreach (string dataDir in DataDirs)
        {
            string globsPath = Path.Combine(dataDir, "mime", "globs2");
            if (!File.Exists(globsPath))
            {
                continue;
            }

            try
            {
                foreach (string line in File.ReadLines(globsPath))
                {
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    string[] parts = line.Split(':');
                    if (parts.Length < 3 || !string.Equals(parts[2], pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (int.TryParse(parts[0], out int weight) && weight > bestWeight)
                    {
                        bestWeight = weight;
                        bestMime = parts[1];
                    }
                }
            }
            catch
            {
            }

            if (bestMime != null)
            {
                return bestMime;
            }
        }

        return null;
    }

    public static string? SniffMimeType(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string? mime = RunProcess("xdg-mime", $"query filetype \"{filePath}\"");
        return string.IsNullOrWhiteSpace(mime) ? null : mime.Trim();
    }

    public static string? GetDefaultApplication(string mimeType)
    {
        string? desktop = RunProcess("xdg-mime", $"query default \"{mimeType}\"");
        return string.IsNullOrWhiteSpace(desktop) ? null : desktop.Trim();
    }

    public static List<string> GetApplicationsForMimeType(string mimeType)
    {
        string? output = RunProcess("gio", $"mime \"{mimeType}\"");
        List<string> apps = new();
        if (string.IsNullOrWhiteSpace(output))
        {
            return apps;
        }

        foreach (string line in output.Split('\n'))
        {
            if (!line.StartsWith('\t'))
            {
                continue;
            }

            string id = line.Trim();
            if (id.EndsWith(".desktop", StringComparison.Ordinal) && !apps.Contains(id))
            {
                apps.Add(id);
            }
        }

        return apps;
    }

    public static string GetDesktopAppName(string desktopId)
    {
        string? path = FindDesktopFilePath(desktopId);
        if (path == null)
        {
            return Path.GetFileNameWithoutExtension(desktopId);
        }

        try
        {
            bool inEntry = false;
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.StartsWith('['))
                {
                    if (inEntry)
                    {
                        break;
                    }

                    inEntry = line == "[Desktop Entry]";
                    continue;
                }

                if (inEntry && line.StartsWith("Name=", StringComparison.Ordinal))
                {
                    return line[5..].Trim();
                }
            }
        }
        catch
        {
        }

        return Path.GetFileNameWithoutExtension(desktopId);
    }

    private static string? FindDesktopFilePath(string desktopId)
    {
        IEnumerable<string> dirs = DataDirs
            .Select(d => Path.Combine(d, "applications"))
            .Append("/var/lib/flatpak/exports/share/applications")
            .Append(Path.Combine(UserHome, ".local", "share", "flatpak", "exports", "share", "applications"))
            .Append("/var/lib/snapd/desktop/applications");

        foreach (string dir in dirs)
        {
            string candidate = Path.Combine(dir, desktopId);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string EnsureDesktopFile(string programPathOrCommand)
    {
        string raw = programPathOrCommand.Trim();
        if (raw.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(raw);
        }

        string appName = Path.GetFileNameWithoutExtension(raw);
        List<string> appDirs = DataDirs
            .Select(d => Path.Combine(d, "applications"))
            .Concat(
            [
                "/var/lib/flatpak/exports/share/applications",
                Path.Combine(UserHome, ".local", "share", "flatpak", "exports", "share", "applications"),
                "/var/lib/snapd/desktop/applications",
            ])
            .Where(Directory.Exists)
            .Distinct()
            .ToList();

        foreach (string dir in appDirs)
        {
            if (File.Exists(Path.Combine(dir, $"{appName}.desktop")))
            {
                return $"{appName}.desktop";
            }
        }

        string? execMatch = FindDesktopFileByExec(appDirs, raw);
        if (execMatch != null)
        {
            return execMatch;
        }

        string userAppsDir = Path.Combine(UserDataDir, "applications");
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

    private static string? FindDesktopFileByExec(List<string> appDirs, string program)
    {
        string programName = Path.GetFileName(program);
        string? generatedMatch = null;

        foreach (string dir in appDirs)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.desktop");
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
            {
                string? execTarget = ReadExecTarget(file);
                if (execTarget == null)
                {
                    continue;
                }

                bool matches = string.Equals(execTarget, program, StringComparison.Ordinal)
                    || (!program.Contains('/') && string.Equals(Path.GetFileName(execTarget), programName, StringComparison.Ordinal));
                if (!matches)
                {
                    continue;
                }

                string id = Path.GetFileName(file);
                if (!id.StartsWith("fe-", StringComparison.Ordinal))
                {
                    return id;
                }

                generatedMatch ??= id;
            }
        }

        return generatedMatch;
    }

    private static string? ReadExecTarget(string desktopFile)
    {
        try
        {
            bool inEntry = false;
            foreach (string rawLine in File.ReadLines(desktopFile))
            {
                string line = rawLine.Trim();
                if (line.StartsWith('['))
                {
                    if (inEntry)
                    {
                        return null;
                    }

                    inEntry = line == "[Desktop Entry]";
                    continue;
                }

                if (!inEntry)
                {
                    continue;
                }

                if (line is "Hidden=true")
                {
                    return null;
                }

                if (!line.StartsWith("Exec=", StringComparison.Ordinal))
                {
                    continue;
                }

                string exec = line[5..].Trim();
                if (exec.StartsWith('"'))
                {
                    int end = exec.IndexOf('"', 1);
                    return end > 0 ? exec[1..end] : null;
                }

                int space = exec.IndexOf(' ');
                return space > 0 ? exec[..space] : exec;
            }
        }
        catch
        {
        }

        return null;
    }

    public static void SetDefaultApplication(string filePathOrExtension, string program)
    {
        string mimeType = filePathOrExtension.StartsWith('.') && !File.Exists(filePathOrExtension)
            ? EnsureCustomMimeType(filePathOrExtension)
            : GetAssociationMimeType(filePathOrExtension);

        if (string.IsNullOrEmpty(mimeType) || mimeType == "application/octet-stream")
        {
            return;
        }

        string desktopFile = EnsureDesktopFile(program);

        UpdateMimeAppsList(mimeType, desktopFile);
        RunProcess("gio", $"mime \"{mimeType}\" \"{desktopFile}\"");
        RunProcess("xdg-mime", $"default \"{desktopFile}\" \"{mimeType}\"");
    }

    public static bool IsGenericMimeType(string mimeType)
    {
        return GenericMimeTypes.Contains(mimeType);
    }

    private static string GetExtension(string filePath)
    {
        return Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
    }

    private static string? RunProcess(string fileName, string arguments)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process proc = Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output;
        }
        catch
        {
            return null;
        }
    }

    private static void UpdateMimeAppsList(string mimeType, string desktopFile)
    {
        string configDir = Path.Combine(UserHome, ".config");
        Directory.CreateDirectory(configDir);
        string listPath = Path.Combine(configDir, "mimeapps.list");

        List<string> lines = File.Exists(listPath) ? [..File.ReadAllLines(listPath)] : [];

        SetSectionEntry(lines, "[Default Applications]", mimeType, desktopFile);
        SetSectionEntry(lines, "[Added Associations]", mimeType, $"{desktopFile};");

        try
        {
            File.WriteAllLines(listPath, lines);
        }
        catch
        {
        }
    }

    private static void SetSectionEntry(List<string> lines, string header, string mimeType, string value)
    {
        int headerIdx = lines.FindIndex(l => l.Trim().Equals(header, StringComparison.OrdinalIgnoreCase));
        if (headerIdx < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add("");
            }

            lines.Add(header);
            headerIdx = lines.Count - 1;
        }

        int nextHeaderIdx = lines.FindIndex(headerIdx + 1, l => l.Trim().StartsWith('[') && l.Trim().EndsWith(']'));
        if (nextHeaderIdx < 0)
        {
            nextHeaderIdx = lines.Count;
        }

        string newLine = $"{mimeType}={value}";
        for (int i = headerIdx + 1; i < nextHeaderIdx; i++)
        {
            if (lines[i].Trim().StartsWith($"{mimeType}=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = newLine;
                return;
            }
        }

        lines.Insert(headerIdx + 1, newLine);
    }
}
