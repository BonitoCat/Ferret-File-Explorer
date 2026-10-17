using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Models;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class ConflictDialog : Window
{
    public ConflictDialog()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Finish(ConflictAction.Cancel);
            e.Handled = true;
        }, Tunnel);
    }

    public ConflictDialog(TransferConflict conflict) : this()
    {
        string name = Path.GetFileName(conflict.DestinationPath);
        string destinationFolder = Path.GetFileName(Path.GetDirectoryName(conflict.DestinationPath) ?? "");
        if (string.IsNullOrEmpty(destinationFolder))
        {
            destinationFolder = Path.GetDirectoryName(conflict.DestinationPath) ?? "/";
        }

        if (conflict.CanMerge)
        {
            Title = LocalizationService.Get("ConflictFolderTitle");
            HeaderText.Text = LocalizationService.Format("ConflictMergeHeader", name);
            MessageText.Text = LocalizationService.Format("ConflictFolderMessage", destinationFolder);
        }
        else
        {
            Title = LocalizationService.Get(conflict.DestinationIsDirectory ? "ConflictFolderTitle" : "ConflictFileTitle");
            HeaderText.Text = LocalizationService.Format("ConflictReplaceHeader", name);
            MessageText.Text = LocalizationService.Format(conflict.DestinationIsDirectory ? "ConflictFolderMessage" : "ConflictFileMessage", destinationFolder);
        }

        OriginalInfoText.Text = Describe(conflict.DestinationPath, conflict.DestinationIsDirectory);
        IncomingInfoText.Text = Describe(conflict.SourcePath, conflict.SourceIsDirectory);
        MergeButton.IsVisible = conflict.CanMerge;
        ApplyToAllCheckBox.IsVisible = conflict.HasMultipleItems;

        Opened += (_, _) =>
        {
            if (conflict.CanMerge)
            {
                MergeButton.Focus();
                return;
            }

            ReplaceButton.Focus();
        };
    }

    private static string Describe(string path, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                DirectoryInfo directory = new(path);
                int count = directory.EnumerateFileSystemInfos().Count();
                return LocalizationService.Format("ConflictFolderInfo", LocalizationService.Plural("ConflictItems", count), directory.LastWriteTime.ToString("g"));
            }

            FileInfo file = new(path);
            return LocalizationService.Format("ConflictFileInfo", FileUtils.FormatSize(file.Length), file.LastWriteTime.ToString("g"));
        }
        catch
        {
            return LocalizationService.Get("Unknown");
        }
    }

    private void Finish(ConflictAction action)
    {
        Close(new ConflictResolution(action, ApplyToAllCheckBox.IsVisible && ApplyToAllCheckBox.IsChecked == true));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Finish(ConflictAction.Cancel);
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        Finish(ConflictAction.Skip);
    }

    private void OnDuplicateClick(object? sender, RoutedEventArgs e)
    {
        Finish(ConflictAction.Duplicate);
    }

    private void OnReplaceClick(object? sender, RoutedEventArgs e)
    {
        Finish(ConflictAction.Replace);
    }

    private void OnMergeClick(object? sender, RoutedEventArgs e)
    {
        Finish(ConflictAction.Merge);
    }
}
