using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FileExplorer.Core.Models;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui.Dialogs;

public partial class BulkRenameDialog : Window
{
    private readonly IReadOnlyList<FileItemViewModel> _items;
    private readonly Dictionary<string, HashSet<string>> _existingNames = new(StringComparer.Ordinal);
    private List<BulkRenamePreviewItem> _preview = [];

    public BulkRenameDialog() : this([])
    {
    }

    public BulkRenameDialog(IReadOnlyList<FileItemViewModel> items)
    {
        InitializeComponent();

        _items = items;
        HeaderText.Text = LocalizationService.Format("BulkRenameHeader", items.Count);
        LoadExistingNames();

        ModeComboBox.SelectedIndex = 0;
        ScopeComboBox.SelectedIndex = 0;
        InsertFromComboBox.SelectedIndex = 0;
        RemoveFromComboBox.SelectedIndex = 0;
        CaseComboBox.SelectedIndex = 0;

        foreach (ComboBox comboBox in new[] { ModeComboBox, ScopeComboBox, InsertFromComboBox, RemoveFromComboBox, CaseComboBox })
        {
            comboBox.SelectionChanged += (_, _) => UpdatePreview();
        }

        foreach (TextBox textBox in new[] { FindBox, ReplaceBox, InsertTextBox, PatternBox })
        {
            textBox.TextChanged += (_, _) => UpdatePreview();
        }

        foreach (CheckBox checkBox in new[] { CaseSensitiveCheckBox, RegexCheckBox, OverwriteCheckBox, StripNumbersCheckBox })
        {
            checkBox.IsCheckedChanged += (_, _) => UpdatePreview();
        }

        foreach (NumericUpDown numericBox in new[] { InsertPositionBox, RemovePositionBox, RemoveCountBox, StartNumberBox, DigitsBox })
        {
            numericBox.ValueChanged += (_, _) => UpdatePreview();
        }

        Opened += (_, _) => FindBox.Focus();
        UpdatePreview();
    }

    private void LoadExistingNames()
    {
        foreach (string directory in _items.Select(item => Path.GetDirectoryName(item.FullPath) ?? "").Distinct())
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    names.Add(Path.GetFileName(entry));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            _existingNames[directory] = names;
        }
    }

    private BulkRenameOptions ReadOptions()
    {
        return new()
        {
            Mode = (BulkRenameMode) Math.Max(0, ModeComboBox.SelectedIndex),
            Scope = (BulkRenameScope) Math.Max(0, ScopeComboBox.SelectedIndex),
            Find = FindBox.Text ?? "",
            Replacement = ReplaceBox.Text ?? "",
            CaseSensitive = CaseSensitiveCheckBox.IsChecked == true,
            UseRegex = RegexCheckBox.IsChecked == true,
            Text = ModeIs(BulkRenameMode.Insert) ? InsertTextBox.Text ?? "" : "",
            Position = ModeIs(BulkRenameMode.Remove) ? ReadInt(RemovePositionBox) : ReadInt(InsertPositionBox),
            FromEnd = ModeIs(BulkRenameMode.Remove) ? RemoveFromComboBox.SelectedIndex == 1 : InsertFromComboBox.SelectedIndex == 1,
            Overwrite = OverwriteCheckBox.IsChecked == true,
            Count = ReadInt(RemoveCountBox),
            Pattern = PatternBox.Text ?? "",
            StartNumber = ReadInt(StartNumberBox),
            Digits = ReadInt(DigitsBox),
            StripNumbers = StripNumbersCheckBox.IsChecked == true,
            Case = (BulkRenameCase) Math.Max(0, CaseComboBox.SelectedIndex),
        };
    }

    private bool ModeIs(BulkRenameMode mode)
    {
        return ModeComboBox.SelectedIndex == (int) mode;
    }

    private static int ReadInt(NumericUpDown box)
    {
        return (int) (box.Value ?? 0);
    }

    private void UpdatePanels()
    {
        ReplacePanel.IsVisible = ModeIs(BulkRenameMode.Replace);
        InsertPanel.IsVisible = ModeIs(BulkRenameMode.Insert);
        RemovePanel.IsVisible = ModeIs(BulkRenameMode.Remove);
        NumberingPanel.IsVisible = ModeIs(BulkRenameMode.Numbering);
        CasePanel.IsVisible = ModeIs(BulkRenameMode.Case);
    }

    private void UpdatePreview()
    {
        UpdatePanels();

        BulkRenameOptions options = ReadOptions();
        List<string> newNames;
        try
        {
            newNames = _items.Select((item, index) => BulkRenamer.Apply(item.Name, item.IsDirectory, index, options)).ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            newNames = _items.Select(item => item.Name).ToList();
            ShowPreview(newNames, new bool[_items.Count]);
            ShowStatus(LocalizationService.Get("RenameInvalidRegex"));
            return;
        }

        bool[] conflicts = FindConflicts(newNames);
        ShowPreview(newNames, conflicts);

        int conflictCount = conflicts.Count(conflict => conflict);
        if (conflictCount > 0)
        {
            ShowStatus(LocalizationService.Format("RenameConflicts", conflictCount));
            return;
        }

        StatusLabel.IsVisible = false;
        RenameButton.IsEnabled = _preview.Any(item => item.IsChanged);
    }

    private bool[] FindConflicts(List<string> newNames)
    {
        bool[] conflicts = new bool[_items.Count];
        string[] directories = _items.Select(item => Path.GetDirectoryName(item.FullPath) ?? "").ToArray();

        HashSet<string> sourcePaths = new(_items.Select(item => item.FullPath), StringComparer.Ordinal);
        Dictionary<string, int> targetCounts = new(StringComparer.Ordinal);
        for (int i = 0; i < _items.Count; i++)
        {
            string target = Path.Combine(directories[i], newNames[i]);
            targetCounts[target] = targetCounts.GetValueOrDefault(target) + 1;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            string newName = newNames[i];
            if (newName == _items[i].Name)
            {
                continue;
            }

            string target = Path.Combine(directories[i], newName);
            bool isInvalid = string.IsNullOrWhiteSpace(newName) || newName is "." or ".." || newName.Contains('/') || newName.Contains('\0');
            bool isDuplicate = targetCounts[target] > 1;
            bool isTaken = _existingNames[directories[i]].Contains(newName) && !sourcePaths.Contains(target);
            conflicts[i] = isInvalid || isDuplicate || isTaken;
        }

        return conflicts;
    }

    private void ShowPreview(List<string> newNames, bool[] conflicts)
    {
        _preview = _items.Select((item, index) => new BulkRenamePreviewItem
        {
            Path = item.FullPath,
            OriginalName = item.Name,
            NewName = newNames[index],
            HasConflict = conflicts[index],
        }).ToList();
        PreviewList.ItemsSource = _preview;
    }

    private void ShowStatus(string message)
    {
        StatusLabel.Text = message;
        StatusLabel.IsVisible = true;
        RenameButton.IsEnabled = false;
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (!RenameButton.IsEnabled)
        {
            return;
        }

        List<BulkRenameEntry> renames = _preview
            .Where(item => item.IsChanged)
            .Select(item => new BulkRenameEntry(item.Path, item.NewName))
            .ToList();
        Close(renames);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
