using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileExplorer.Cli.Utilities;

public static class TerminalUtils
{
    public static void ExecuteInTerminal(string filePath)
    {
        ProcessStartInfo startInfo = GetTerminalStartInfo(filePath);
        Process proc = new() { StartInfo = startInfo };
        proc.Start();
        proc.WaitForExit();
    }

    private static ProcessStartInfo GetTerminalStartInfo(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k \"{filePath}\"",
                UseShellExecute = false,
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e 'tell application \"Terminal\" to do script \"{filePath}\"'",
                UseShellExecute = false,
            };
        }

        string[] terminals =
        [
            "gnome-terminal", "konsole", "xfce4-terminal",
            "xterm", "lxterminal", "mate-terminal",
        ];

        foreach (string terminal in terminals)
        {
            if (!IsCommandAvailable(terminal))
            {
                continue;
            }

            string args = terminal switch
            {
                "gnome-terminal" => $"-- bash -c '{filePath}'; exec bash",
                "konsole" => $"-e bash -c '{filePath}'; exec bash",
                _ => $"-e bash -c '{filePath}'; exec bash",
            };

            return new ProcessStartInfo
            {
                FileName = terminal,
                Arguments = args,
                UseShellExecute = false,
            };
        }

        return new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true,
        };
    }

    private static bool IsCommandAvailable(string command)
    {
        Process proc = new()
        {
            StartInfo = new()
            {
                FileName = "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        proc.Start();
        proc.WaitForExit();
        
        return proc.ExitCode == 0;
    }
}
