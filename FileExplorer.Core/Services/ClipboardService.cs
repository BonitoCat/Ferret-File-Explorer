using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace FileExplorer.Core.Services;

public static class ClipboardService
{
    private const string Name = "fe.clipboard";
    private const int Size = 16 * 1024;

    public static void WritePaths(ClipboardMode mode, IEnumerable<string> paths)
    {
        try
        {
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "fe");
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            string filePath = Path.Combine(cacheDir, Name);
            using MemoryMappedFile memFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.OpenOrCreate, null, Size);
            using MemoryMappedViewStream stream = memFile.CreateViewStream();
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: false);

            string[] array = paths.ToArray();

            writer.Write((int) mode);
            writer.Write(array.Length);

            foreach (string path in array)
            {
                writer.Write(path);
            }
        }
        catch { }
    }

    public static void ReadPaths(out ClipboardMode mode, out string[] paths)
    {
        mode = ClipboardMode.Copy;
        paths = [];

        try
        {
            string filePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "fe"), Name);
            if (!File.Exists(filePath))
            {
                return;
            }

            using MemoryMappedFile memFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, Size);
            using MemoryMappedViewStream stream = memFile.CreateViewStream();
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);

            mode = (ClipboardMode) reader.ReadInt32();
            int count = reader.ReadInt32();

            paths = new string[count];
            for (int i = 0; i < count; i++)
            {
                paths[i] = reader.ReadString();
            }
        }
        catch
        {
            mode = ClipboardMode.Copy;
            paths = [];
        }
    }

    public static void ClearPaths()
    {
        try
        {
            string filePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "fe"), Name);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch { }
    }
    
    public static void Copy(string text)
    {
        if (IsWayland() && File.Exists("/usr/bin/wl-copy"))
        {
            RunWithInput("wl-copy", text);
            
            return;
        }

        if (File.Exists("/usr/bin/xclip"))
        {
            RunWithInput("xclip -selection clipboard", text);
            
            return;
        }

        throw new InvalidOperationException("No clipboard utility found (wl-copy or xclip)");
    }
    
    public static string Read()
    {
        if (IsWayland() && File.Exists("/usr/bin/wl-paste"))
        {
            return RunWithOutput("wl-paste");
        }

        if (File.Exists("/usr/bin/xclip"))
        {
            return RunWithOutput("xclip -selection clipboard -o");
        }

        throw new InvalidOperationException("No clipboard utility found (wl-paste or xclip)");
    }

    private static bool IsWayland()
    {
        string? wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        return !string.IsNullOrEmpty(wayland);
    }

    private static void RunWithInput(string command, string input)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "/bin/bash",
            Arguments = "-c \"" + command + "\"",
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        
        using Process process = new();
        process.StartInfo = psi;
        process.Start();

        if (!string.IsNullOrEmpty(input))
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        
        process.WaitForExit();
    }
    
    private static string RunWithOutput(string command)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "/bin/bash",
            Arguments = "-c \"" + command + "\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        
        using Process process = new();
        process.StartInfo = psi;
        process.Start();
        
        process.WaitForExit();
        return process.StandardOutput.ReadToEnd();
    }
}