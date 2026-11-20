using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Dialogs;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private const string TemplateItemClass = "template-item";
    private const int MaxTemplateDepth = 3;
    private const int MaxTemplateEntries = 200;

    private void InjectTemplateItems(ContextMenu menu)
    {
        MenuItem? newMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Tag is "new");
        if (newMenu == null)
        {
            return;
        }

        foreach (Control stale in newMenu.Items.OfType<Control>().Where(c => c.Classes.Contains(TemplateItemClass)).ToList())
        {
            newMenu.Items.Remove(stale);
        }

        string? folder = GetTemplatesFolder();
        if (folder == null)
        {
            return;
        }

        int budget = MaxTemplateEntries;
        List<Control> items = CreateTemplateItems(folder, 0, ref budget);
        if (items.Count == 0)
        {
            return;
        }

        MenuItem? newFile = newMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.Tag is "new-file");
        int insertAt = newFile == null ? newMenu.Items.Count : newMenu.Items.IndexOf(newFile) + 1;
        Separator separator = new();
        separator.Classes.Add(TemplateItemClass);
        newMenu.Items.Insert(insertAt, separator);
        foreach (Control item in items)
        {
            newMenu.Items.Insert(++insertAt, item);
        }
    }

    private static string? GetTemplatesFolder()
    {
        string folder = PathManager.TrimEnd(UserDirs.Templates);
        if (folder == PathManager.TrimEnd(UserDirs.Home) || !Directory.Exists(folder))
        {
            return null;
        }

        return folder;
    }

    private List<Control> CreateTemplateItems(string folder, int depth, ref int budget)
    {
        List<Control> items = new();
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(folder)
                .Where(path => !Path.GetFileName(path).StartsWith('.'))
                .OrderBy(path => !Directory.Exists(path))
                .ThenBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return items;
        }

        foreach (string path in entries)
        {
            if (budget-- <= 0)
            {
                break;
            }

            if (Directory.Exists(path))
            {
                if (depth + 1 >= MaxTemplateDepth)
                {
                    continue;
                }

                List<Control> children = CreateTemplateItems(path, depth + 1, ref budget);
                if (children.Count == 0)
                {
                    continue;
                }

                MenuItem submenu = new()
                {
                    Header = Path.GetFileName(path),
                    Icon = CreateMenuIcon(IconService.GetForEntry(true, default, path)),
                    ItemsSource = children,
                };
                submenu.Classes.Add(TemplateItemClass);
                items.Add(submenu);
                continue;
            }

            MenuItem item = new()
            {
                Header = Path.GetFileNameWithoutExtension(path),
                Icon = CreateMenuIcon(IconService.GetForEntry(false, default, path)),
            };
            item.Classes.Add(TemplateItemClass);
            string template = path;
            item.Click += (_, _) => CreateFromTemplate(template);
            items.Add(item);
        }

        return items;
    }

    private static Image? CreateMenuIcon(IImage? icon)
    {
        if (icon == null)
        {
            return null;
        }

        return new Image
        {
            Source = icon,
            Width = 16,
            Height = 16,
        };
    }

    private async void CreateFromTemplate(string template)
    {
        InputDialog dialog = new(LocalizationService.Format("NewFromTemplate", Path.GetFileNameWithoutExtension(template)), LocalizationService.Get("FileNamePrompt"), Path.GetFileName(template))
        {
            SelectNameOnly = true,
        };
        string? name = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        SelectWhenLoaded(await ViewModel.CreateFromTemplateAsync(template, name));
    }
}
