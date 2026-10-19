using System;
using System.Collections.Generic;
using System.IO.Enumeration;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private bool TryRunBuiltInPathBarCommand(string keyword, string input)
    {
        switch (keyword.ToLowerInvariant())
        {
            case "select":
            case "sel":
                SelectByPattern(input);
                return true;
            case "new":
            case "mkdir":
                CreateFromPathBar(input, true);
                return true;
            case "touch":
            case "newfile":
                CreateFromPathBar(input, false);
                return true;
            case "hidden":
                ViewModel.ShowHiddenFiles = !ViewModel.ShowHiddenFiles;
                return true;
            case "split":
            case "dual":
                ViewModel.ToggleDualPane();
                return true;
            case "grid":
                ViewModel.SetGridView(true);
                return true;
            case "list":
                ViewModel.SetGridView(false);
                return true;
            case "find":
            case "search":
                SearchFromPathBar(input);
                return true;
            case "root":
                ViewModel.OpenAsRoot(ViewModel.CommandDirectory);
                return true;
            case "colors":
            case "theme":
                OnThemeColorsClick(null, new());
                return true;
            case "addons":
                OnMenuManageAddonsClick(null, new());
                return true;
            case "props":
            case "properties":
                OnMenuPropertiesClick(null, new());
                return true;
            default:
                return false;
        }
    }

    private void SelectByPattern(string input)
    {
        ListBox? list = GetActiveListBox();
        if (list == null)
        {
            return;
        }

        PaneViewModel pane = ViewModel.ActivePane;
        string[] patterns = input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.IndexOfAny(['*', '?']) < 0 ? $"*{p}*" : p)
            .ToArray();
        if (patterns.Length == 0)
        {
            patterns = ["*"];
        }

        List<int> matches = new();
        for (int i = 0; i < pane.Items.Count; i++)
        {
            string name = pane.Items[i].Name;
            if (patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, name)))
            {
                matches.Add(i);
            }
        }

        list.Selection.BeginBatchUpdate();
        list.Selection.Clear();
        foreach (int index in matches)
        {
            list.Selection.Select(index);
        }

        list.Selection.EndBatchUpdate();

        if (matches.Count > 0)
        {
            list.ScrollIntoView(matches[0]);
        }

        SyncPaneSelection(pane);
        ViewModel.StatusText = matches.Count > 0
            ? LocalizationService.Format("PathBarSelected", matches.Count)
            : LocalizationService.Format("PathBarNoMatch", input);
    }

    private void CreateFromPathBar(string name, bool folder)
    {
        if (name.Length == 0)
        {
            if (folder)
            {
                OnNewFolderClick(null, new());
            }
            else
            {
                OnNewFileClick(null, new());
            }

            return;
        }

        SelectWhenLoaded(folder ? ViewModel.CreateFolder(name) : ViewModel.CreateFile(name));
    }

    private void SearchFromPathBar(string text)
    {
        if (text.Length == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                SearchTextBox.Focus();
                SearchTextBox.SelectAll();
            });
            return;
        }

        ViewModel.SearchQuery = text;
    }
}
