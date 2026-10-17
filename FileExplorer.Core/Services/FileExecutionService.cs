using System.Diagnostics;
using System.Runtime.InteropServices;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Interfaces;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public class FileExecutionService
{
    public Action<string>? OnOpenTextFile { get; set; }
    public Action<string>? OnOpenTerminalForExecutable { get; set; }

    public void Execute(IFileSystemEntry entry)
    {
        if (entry is not FileEntry file)
        {
            return;
        }

        if (file.FileType == FileType.Text && OnOpenTextFile != null)
        {
            OnOpenTextFile(file.FullPath);
            return;
        }

        if (FileUtils.IsLaunchableProgram(file.FullPath))
        {
            if (OnOpenTerminalForExecutable != null && RequiresTerminal(file.FullPath))
            {
                OnOpenTerminalForExecutable(file.FullPath);
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = file.FullPath,
                    UseShellExecute = false,
                });
            }
            
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                ProcessStartInfo gioPsi = new()
                {
                    FileName = "gio",
                    Arguments = $"open \"{file.FullPath}\"",
                    UseShellExecute = false,
                };
                gioPsi.EnvironmentVariables["DESKTOP_STARTUP_ID"] = $"fe-file-explorer_{Environment.ProcessId}_TIME{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                Process.Start(gioPsi);
                return;
            }
            catch
            {
            }

            try
            {
                ProcessStartInfo xdgPsi = new()
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{file.FullPath}\"",
                    UseShellExecute = false,
                };
                xdgPsi.EnvironmentVariables["DESKTOP_STARTUP_ID"] = $"fe-file-explorer_{Environment.ProcessId}_TIME{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                Process.Start(xdgPsi);
                return;
            }
            catch
            {
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = file.FullPath,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    public static bool RequiresTerminal(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsSubsystem(path) == 3;
        }

        if (OperatingSystem.IsLinux())
        {
            return !IsLinuxGuiBinary(path);
        }

        if (OperatingSystem.IsMacOS())
        {
            string? dir = Path.GetDirectoryName(path);
            return dir == null || !dir.Contains(".app/");
        }

        return true;
    }

    private static int GetWindowsSubsystem(string path)
    {
        try
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(fs);

            if (reader.ReadUInt16() != 0x5A4D)
            {
                return -1;
            }

            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = reader.ReadInt32();

            fs.Seek(peOffset, SeekOrigin.Begin);

            if (reader.ReadUInt32() != 0x00004550)
            {
                return -1;
            }

            fs.Seek(peOffset + 4 + 20 + 68, SeekOrigin.Begin);
            return reader.ReadUInt16();
        }
        catch
        {
            return -1;
        }
    }

    private static bool IsLinuxGuiBinary(string path)
    {
        try
        {
            Process proc = new()
            {
                StartInfo = new()
                {
                    FileName = "ldd",
                    Arguments = path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output.Contains("libX11")
                   || output.Contains("libwayland")
                   || output.Contains("libQt")
                   || output.Contains("libgtk");
        }
        catch
        {
            return false;
        }
    }

    public void OpenTerminal(string workingDirectory)
    {
        if (OperatingSystem.IsLinux())
        {
            if (OnOpenTerminalForExecutable != null)
            {
                OnOpenTerminalForExecutable(workingDirectory);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "x-terminal-emulator",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
        }
        else if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
        }
    }

    public void OpenInDefaultFileBrowser(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
            });
        }
        else
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
    }

    public static void OpenAsRoot(string path)
    {
        string exe = Environment.ProcessPath ?? "";
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        string display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
        string xauth = Environment.GetEnvironmentVariable("XAUTHORITY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Xauthority");
        string wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "";
        string xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "";

        string envPrefix = $"DISPLAY={display} XAUTHORITY={xauth}";
        if (!string.IsNullOrEmpty(wayland))
        {
            envPrefix += $" WAYLAND_DISPLAY={wayland} XDG_RUNTIME_DIR={xdgRuntime}";
        }

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "pkexec",
                Arguments = $"env {envPrefix} \"{exe}\" \"{path}\"",
                UseShellExecute = false,
            };
            
            Process.Start(psi);
        }
        catch
        {
            try
            {
                string cmd = $"zenity --password --title=\"Authenticate\" | sudo -S env {envPrefix} \"{exe}\" \"{path}\"";
                ProcessStartInfo psi = new()
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{cmd.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                };
                
                Process.Start(psi);
            }
            catch { }
        }
    }
}
