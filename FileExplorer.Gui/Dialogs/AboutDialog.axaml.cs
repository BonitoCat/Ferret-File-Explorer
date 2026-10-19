using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        NameText.Text = AppInfo.Name;
        VersionText.Text = LocalizationService.Format("AboutVersion", AppInfo.Version);
        RuntimeText.Text = $"{AppInfo.Name} {AppInfo.Version}\n{AppInfo.RuntimeDescription}";

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close();
            e.Handled = true;
        }, Tunnel);
    }

    private async void OnGitHubClick(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(AppInfo.RepositoryUrl));
    }

    private async void OnReportBugClick(object? sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(AppInfo.IssuesUrl));
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
