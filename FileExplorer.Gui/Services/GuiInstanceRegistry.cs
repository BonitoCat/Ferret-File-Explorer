using System;
using System.IO;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Gui.Services;

public static class GuiInstanceRegistry
{
    private static readonly string CacheDir = Path.Combine(AppPaths.CacheDir, "instances");

    public static void Register(int pid, string currentPath)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            string filePath = Path.Combine(CacheDir, $"{pid}.path");
            File.WriteAllText(filePath, currentPath);
        }
        catch
        {
        }
    }

    public static void Update(int pid, string currentPath)
    {
        Register(pid, currentPath);
    }

    public static void Unregister(int pid)
    {
        try
        {
            string filePath = Path.Combine(CacheDir, $"{pid}.path");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
        }
    }

    public static string? GetPathForPid(int pid)
    {
        try
        {
            string filePath = Path.Combine(CacheDir, $"{pid}.path");
            if (File.Exists(filePath))
            {
                string text = File.ReadAllText(filePath).Trim();
                if (Directory.Exists(text))
                {
                    return text;
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
