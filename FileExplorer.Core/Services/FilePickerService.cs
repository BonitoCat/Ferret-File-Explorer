using System;
using System.Collections.Generic;
using System.IO;
using FileExplorer.Core.Models;

namespace FileExplorer.Core.Services;

public class FilePickerService
{
    public FilePickerOptions? ParseCommandLineArgs(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return null;
        }

        PickerMode mode = PickerMode.None;
        string? initialPath = null;
        string? title = null;
        bool allowMultiple = false;
        string? initialName = null;
        string? acceptLabel = null;
        List<PickerFilter> filters = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].Trim();

            if (arg.Equals("--file-picker", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--file-selection", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--file", StringComparison.OrdinalIgnoreCase))
            {
                mode = PickerMode.File;
                continue;
            }

            if (arg.Equals("--folder-picker", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--directory", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--folder", StringComparison.OrdinalIgnoreCase))
            {
                mode = PickerMode.Folder;
                continue;
            }

            if (arg.Equals("--save-picker", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--save", StringComparison.OrdinalIgnoreCase))
            {
                mode = PickerMode.Save;
                continue;
            }

            if (arg.Equals("--name", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                initialName = args[++i];
                continue;
            }

            if (arg.Equals("--accept-label", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                acceptLabel = args[++i];
                continue;
            }

            if ((arg.Equals("--filter", StringComparison.OrdinalIgnoreCase) || arg.Equals("--file-filter", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                PickerFilter? filter = PickerFilter.Parse(args[++i]);
                if (filter != null)
                {
                    filters.Add(filter);
                }

                continue;
            }

            if (arg.StartsWith("--file-filter=", StringComparison.OrdinalIgnoreCase))
            {
                PickerFilter? filter = PickerFilter.Parse(arg["--file-filter=".Length..]);
                if (filter != null)
                {
                    filters.Add(filter);
                }

                continue;
            }

            if (arg.Equals("--multiple", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--multi", StringComparison.OrdinalIgnoreCase))
            {
                allowMultiple = true;
                continue;
            }

            if ((arg.Equals("--title", StringComparison.OrdinalIgnoreCase) || arg.Equals("-t", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                title = args[++i];
                continue;
            }

            if ((arg.Equals("--path", StringComparison.OrdinalIgnoreCase) || arg.Equals("-p", StringComparison.OrdinalIgnoreCase) || arg.Equals("--filename", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                initialPath = ResolvePath(args[++i]);
                continue;
            }

            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                string? resolved = ResolvePath(arg);
                if (resolved != null)
                {
                    initialPath = resolved;
                }
            }
        }

        if (mode == PickerMode.None)
        {
            return null;
        }

        return new()
        {
            Mode = mode,
            InitialPath = initialPath,
            Title = title,
            AllowMultiple = allowMultiple,
            InitialName = initialName,
            ActionButtonText = acceptLabel,
            Filters = filters
        };
    }

    private static string? ResolvePath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            if (raw.StartsWith("~", StringComparison.Ordinal))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                raw = Path.Combine(home, raw.TrimStart('~').TrimStart('/', '\\'));
            }

            string full = Path.GetFullPath(raw);
            if (Directory.Exists(full))
            {
                return full;
            }

            if (File.Exists(full))
            {
                return full;
            }

            return full;
        }
        catch
        {
            return null;
        }
    }
}
