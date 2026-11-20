using System.ComponentModel;
using System.Diagnostics;

namespace FileExplorer.Core.Utilities;

public static class ProcessUtils
{
    public static void TryKillTree(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException or NotSupportedException)
        {
        }
    }

    public static Process? StartDetached(ProcessStartInfo psi)
    {
        if (!OperatingSystem.IsLinux() || psi.UseShellExecute)
        {
            return Process.Start(psi);
        }

        string? setsid = FindExecutable("setsid");
        if (setsid == null)
        {
            return Process.Start(psi);
        }

        string executable = FindExecutable(psi.FileName) ?? throw new Win32Exception(2, $"{psi.FileName} not found");
        List<string> prefix = new();
        string? systemdRun = HasUserSystemd() ? FindExecutable("systemd-run") : null;
        if (systemdRun != null)
        {
            prefix.AddRange([systemdRun, "--user", "--scope", "--quiet", "--collect", "--"]);
        }

        prefix.AddRange([setsid, "-f", executable]);

        psi.FileName = prefix[0];
        if (psi.ArgumentList.Count == 0 && !string.IsNullOrEmpty(psi.Arguments))
        {
            psi.Arguments = string.Join(' ', prefix.Skip(1).Select(QuoteArgument)) + " " + psi.Arguments;
        }
        else
        {
            for (int i = 1; i < prefix.Count; i++)
            {
                psi.ArgumentList.Insert(i - 1, prefix[i]);
            }
        }

        return Process.Start(psi);
    }

    private static bool HasUserSystemd()
    {
        string? runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return Directory.Exists("/run/systemd/system") && !string.IsNullOrEmpty(runtimeDir) && File.Exists(Path.Combine(runtimeDir, "systemd", "private"));
    }

    private static string? FindExecutable(string name)
    {
        if (name.Contains('/'))
        {
            return File.Exists(name) ? Path.GetFullPath(name) : null;
        }

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    public static void TryLowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
}
