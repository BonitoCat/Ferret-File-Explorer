using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class CompressDialog : Window
{
    private readonly ArchiveService _archiveService = new();
    private readonly IReadOnlyList<string> _sourcePaths;
    private readonly string _currentDirectory;

    public string? ResultArchivePath { get; private set; }

    public CompressDialog() : this([], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    public CompressDialog(IReadOnlyList<string> sourcePaths, string currentDirectory)
    {
        InitializeComponent();
        UpdatePasswordIcon();

        _sourcePaths = sourcePaths;
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
                ResultArchivePath = null;
                Close(null);
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

    private async void OnCreateClick(object? sender, RoutedEventArgs e)
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

        string destinationPath = Path.Combine(_currentDirectory, archiveFileName);

        StatusLabel.IsVisible = false;
        ProgressBar.IsVisible = true;
        CreateButton.IsEnabled = false;

        string? password = PasswordBox.IsEnabled && !string.IsNullOrEmpty(PasswordBox.Text) ? PasswordBox.Text : null;

        (bool success, string error) = await Task.Run(() => _archiveService.CompressAsync(_sourcePaths, destinationPath, extension, password));

        ProgressBar.IsVisible = false;
        CreateButton.IsEnabled = true;

        if (success)
        {
            ResultArchivePath = destinationPath;
            Close(destinationPath);
            return;
        }

        StatusLabel.Text = string.IsNullOrEmpty(error) ? LocalizationService.Get("CompressionFailed") : error;
        StatusLabel.IsVisible = true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ResultArchivePath = null;
        Close(null);
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
