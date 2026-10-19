using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class ExtractDialog : Window
{
    public bool IntoFolder => IntoFolderCheckBox.IsChecked == true;
    public string FolderName => (FolderNameTextBox.Text ?? "").Trim();

    public ExtractDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            FolderNameTextBox.Focus();
            FolderNameTextBox.SelectAll();
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

    public ExtractDialog(string archiveName) : this()
    {
        PromptLabel.Text = LocalizationService.Format("ExtractPrompt", archiveName);
        FolderNameTextBox.Text = archiveName[..FileUtils.GetNameLength(archiveName)];
    }

    private void OnIntoFolderChanged(object? sender, RoutedEventArgs e)
    {
        FolderNameTextBox.IsEnabled = IntoFolder;
        UpdateExtractButton();
    }

    private void OnFolderNameChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateExtractButton();
    }

    private void UpdateExtractButton()
    {
        ExtractButton.IsEnabled = !IntoFolder || IsValidFolderName(FolderName);
    }

    private static bool IsValidFolderName(string name)
    {
        return name.Length > 0 && name != "." && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private void OnExtractClick(object? sender, RoutedEventArgs e)
    {
        if (!ExtractButton.IsEnabled)
        {
            return;
        }

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
