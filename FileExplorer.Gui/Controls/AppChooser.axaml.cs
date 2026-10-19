using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FileExplorer.Core.Models;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.Controls;

public partial class AppChooser : UserControl
{
    public event Action? ProgramActivated;

    public AppAssociations? Associations { get; private set; }

    public string? SelectedProgram
    {
        get
        {
            string text = CommandTextBox.Text?.Trim() ?? "";
            if (text.Length > 0)
            {
                return text;
            }

            return AppsListBox.SelectedItem is ListBoxItem { Tag: string id } ? id : null;
        }
    }

    public AppChooser()
    {
        InitializeComponent();
    }

    public async Task LoadAsync(string path)
    {
        AppAssociations associations = await Task.Run(() => MimeService.GetAppAssociations(path));
        Associations = associations;

        List<ListBoxItem> items = associations.Apps
            .Select(a => new ListBoxItem
            {
                Content = a.Id == associations.DefaultApp ? LocalizationService.Format("AppDefault", a.Name) : a.Name,
                Tag = a.Id,
                FontWeight = a.Id == associations.DefaultApp ? FontWeight.SemiBold : FontWeight.Normal,
            })
            .ToList();

        AppsListBox.ItemsSource = items;
        if (CommandTextBox.Text?.Trim().Length > 0)
        {
            return;
        }

        AppsListBox.SelectedItem = items.FirstOrDefault(i => (string?) i.Tag == associations.DefaultApp);
    }

    public void FocusInput()
    {
        CommandTextBox.Focus();
        CommandTextBox.SelectAll();
    }

    private void OnAppSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AppsListBox.SelectedItem == null)
        {
            return;
        }

        CommandTextBox.Text = "";
    }

    private void OnCommandTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CommandTextBox.Text))
        {
            return;
        }

        AppsListBox.SelectedItem = null;
    }

    private void OnAppDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (AppsListBox.SelectedItem == null)
        {
            return;
        }

        ProgramActivated?.Invoke();
    }

    private async void OnBrowseProgramClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        string initialPath = "/";
        if (!Directory.Exists(initialPath))
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        FilePickerOptions options = new()
        {
            Mode = PickerMode.File,
            InitialPath = initialPath,
            Title = LocalizationService.Get("SelectApplication"),
            ActionButtonText = LocalizationService.Get("Select")
        };

        MainWindow picker = new(initialPath, options);
        FilePickerResult result = await picker.ShowPickerAsync(owner);

        if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.SelectedPath))
        {
            CommandTextBox.Text = result.SelectedPath;
        }
    }
}
