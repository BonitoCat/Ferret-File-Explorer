using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class CompressDialog : Window
{
    private readonly string _currentDirectory;

    public string ArchivePath { get; private set; } = string.Empty;
    public string ArchiveFormat { get; private set; } = string.Empty;
    public string? Password { get; private set; }

    public CompressDialog() : this([], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public CompressDialog(IReadOnlyList<string> sourcePaths, string currentDirectory)
    {
        InitializeComponent();
        UpdatePasswordIcon();

        _currentDirectory = currentDirectory;

        FormatComboBox.SelectedIndex = 0;
        FormatComboBox.SelectionChanged += OnFormatSelectionChanged;

        string defaultBaseName = LocalizationService.Get("ArchiveDefaultName");
        if (sourcePaths.Count == 1)
        {
            string first = sourcePaths[0];
            defaultBaseName = Directory.Exists(first) ? PathManager.GetName(first) : Path.GetFileNameWithoutExtension(first);
            if (string.IsNullOrEmpty(defaultBaseName))
            {
                defaultBaseName = LocalizationService.Get("ArchiveDefaultName");
            }
        }
        else if (!string.IsNullOrEmpty(currentDirectory))
        {
            string dirName = PathManager.GetName(currentDirectory);
            if (!string.IsNullOrEmpty(dirName))
            {
                defaultBaseName = dirName;
            }
        }

        ArchiveNameBox.Text = defaultBaseName;

        Opened += (_, _) =>
        {
            ArchiveNameBox.Focus();
            ArchiveNameBox.SelectAll();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
                e.Handled = true;
            }
        }, Tunnel);
    }

    private void OnFormatSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = FormatComboBox.SelectedIndex;
        bool supportsPassword = index == 0 || index == 1;

        PasswordBox.IsEnabled = supportsPassword;
        if (!supportsPassword)
        {
            PasswordBox.Text = string.Empty;
        }

        PasswordHintText.Text = supportsPassword
            ? LocalizationService.Get("EncryptionSupported")
            : LocalizationService.Get("EncryptionUnsupported");
    }

    private void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        string rawName = ArchiveNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(rawName))
        {
            StatusLabel.Text = LocalizationService.Get("EnterArchiveName");
            StatusLabel.IsVisible = true;
            ArchiveNameBox.Focus();
            return;
        }

        int formatIndex = FormatComboBox.SelectedIndex;
        string extension = formatIndex switch
        {
            1 => ".7z",
            2 => ".tar.gz",
            3 => ".tar.xz",
            4 => ".tar.bz2",
            5 => ".tar.zst",
            6 => ".tar",
            _ => ".zip"
        };

        string archiveFileName = rawName;
        if (!archiveFileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            archiveFileName += extension;
        }

        ArchivePath = Path.Combine(_currentDirectory, archiveFileName);
        ArchiveFormat = extension;
        Password = PasswordBox.IsEnabled && !string.IsNullOrEmpty(PasswordBox.Text) ? PasswordBox.Text : null;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnTogglePasswordClick(object? sender, RoutedEventArgs e)
    {
        PasswordBox.PasswordChar = PasswordBox.PasswordChar == '\0' ? '•' : '\0';
        UpdatePasswordIcon();
    }

    private void UpdatePasswordIcon()
    {
        TogglePasswordIcon.Source = PasswordBox.PasswordChar == '\0'
            ? IconService.Get("eye-off")
            : IconService.Get("eye");
    }
}
