using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class NetworkLoginDialog : Window
{
    public NetworkLoginDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            PasswordTextBox.Focus();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close(null);
            e.Handled = true;
        }, Tunnel);
    }

    public NetworkLoginDialog(string serverName, bool rejected) : this()
    {
        Title = LocalizationService.Format("NetworkServerLoginTitle", serverName);
        PromptText.Text = LocalizationService.Format("NetworkServerLoginPrompt", serverName);
        UserTextBox.Text = Environment.UserName;
        ErrorText.Text = LocalizationService.Get("NetworkLoginRejected");
        ErrorText.IsVisible = rejected;
    }

    private void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        string user = (UserTextBox.Text ?? "").Trim();
        if (user.Length == 0)
        {
            UserTextBox.Focus();
            return;
        }

        string domain = (DomainTextBox.Text ?? "").Trim();
        Close(new NetworkCredentials(user, domain.Length > 0 ? domain : null, PasswordTextBox.Text ?? ""));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
