using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Models;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public class OpenWithResult
{
    public required string Program { get; init; }
    public bool Always { get; init; }
}

public partial class OpenWithDialog : Window
{
    public OpenWithResult? Result { get; private set; }

    public OpenWithDialog()
    {
        InitializeComponent();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Result = null;
                Close(null);
                e.Handled = true;
            }
        }, Tunnel);
    }

    public OpenWithDialog(string extension) : this()
    {
        if (!string.IsNullOrEmpty(extension))
        {
            AlwaysOpenCheckBox.Content = $"{AlwaysOpenCheckBox.Content} ({extension})";
        }
    }

    private async void OnBrowseProgramClick(object? sender, RoutedEventArgs e)
    {
        string initialPath = "/";
        if (!Directory.Exists(initialPath))
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        FilePickerOptions options = new()
        {
            Mode = PickerMode.File,
            InitialPath = initialPath,
            Title = "Select Application / Executable",
            ActionButtonText = "Select"
        };

        MainWindow picker = new(initialPath, options);
        FilePickerResult result = await picker.ShowPickerAsync(this);

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.SelectedPath))
        {
            CommandTextBox.Text = result.SelectedPath;
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        string text = CommandTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Result = new()
        {
            Program = text,
            Always = AlwaysOpenCheckBox.IsChecked ?? false
        };

        Close(Result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close(null);
    }
}
