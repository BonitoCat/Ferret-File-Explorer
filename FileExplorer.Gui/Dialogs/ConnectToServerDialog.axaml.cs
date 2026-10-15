using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Models;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class ConnectToServerDialog : Window
{
    private readonly NetworkService _networkService = new();

    public string? ResultMountPath { get; private set; }

    public ConnectToServerDialog()
    {
        InitializeComponent();

        ProtocolComboBox.SelectedIndex = 0;
        ProtocolComboBox.SelectionChanged += OnProtocolSelectionChanged;
        AnonymousCheckBox.IsCheckedChanged += OnAnonymousCheckedChanged;
        UpdatePasswordIcon();

        Opened += (_, _) =>
        {
            ServerBox.Focus();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ResultMountPath = null;
                Close(null);
                e.Handled = true;
            }
        }, Tunnel);
    }

    private void OnProtocolSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = ProtocolComboBox.SelectedIndex;

        switch (index)
        {
            case 0:
                PortBox.Text = "22";
                DomainLabel.IsVisible = false;
                DomainBox.IsVisible = false;
                UserLabel.IsVisible = true;
                UserBox.IsVisible = true;
                PasswordLabel.IsVisible = true;
                PasswordBox.IsVisible = true;
                AnonymousCheckBox.IsVisible = false;
                break;

            case 1:
                PortBox.Text = "445";
                DomainLabel.IsVisible = true;
                DomainBox.IsVisible = true;
                UserLabel.IsVisible = true;
                UserBox.IsVisible = true;
                PasswordLabel.IsVisible = true;
                PasswordBox.IsVisible = true;
                AnonymousCheckBox.IsVisible = true;
                break;

            case 2:
                PortBox.Text = "21";
                DomainLabel.IsVisible = false;
                DomainBox.IsVisible = false;
                UserLabel.IsVisible = true;
                UserBox.IsVisible = true;
                PasswordLabel.IsVisible = true;
                PasswordBox.IsVisible = true;
                AnonymousCheckBox.IsVisible = false;
                break;

            case 3:
                PortBox.Text = "21";
                DomainLabel.IsVisible = false;
                DomainBox.IsVisible = false;
                UserLabel.IsVisible = false;
                UserBox.IsVisible = false;
                PasswordLabel.IsVisible = false;
                PasswordBox.IsVisible = false;
                AnonymousCheckBox.IsVisible = false;
                break;

            case 4:
                PortBox.Text = "80";
                DomainLabel.IsVisible = false;
                DomainBox.IsVisible = false;
                UserLabel.IsVisible = true;
                UserBox.IsVisible = true;
                PasswordLabel.IsVisible = true;
                PasswordBox.IsVisible = true;
                AnonymousCheckBox.IsVisible = false;
                break;

            case 5:
                PortBox.Text = "443";
                DomainLabel.IsVisible = false;
                DomainBox.IsVisible = false;
                UserLabel.IsVisible = true;
                UserBox.IsVisible = true;
                PasswordLabel.IsVisible = true;
                PasswordBox.IsVisible = true;
                AnonymousCheckBox.IsVisible = false;
                break;
        }
    }

    private void OnAnonymousCheckedChanged(object? sender, RoutedEventArgs e)
    {
        bool isAnonymous = AnonymousCheckBox.IsChecked == true;

        UserBox.IsEnabled = !isAnonymous;
        PasswordBox.IsEnabled = !isAnonymous;
        DomainBox.IsEnabled = !isAnonymous;
    }

    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        string server = ServerBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(server))
        {
            StatusLabel.Text = "Please enter a server hostname or IP address.";
            StatusLabel.IsVisible = true;
            ServerBox.Focus();
            return;
        }

        StatusLabel.IsVisible = false;
        ProgressBar.IsVisible = true;
        ConnectButton.IsEnabled = false;

        int? port = null;
        if (int.TryParse(PortBox.Text?.Trim(), out int parsedPort) && parsedPort > 0)
        {
            port = parsedPort;
        }

        int protocolIndex = ProtocolComboBox.SelectedIndex;
        NetworkProtocol protocol = protocolIndex switch
        {
            1 => NetworkProtocol.Smb,
            2 => NetworkProtocol.Ftp,
            3 => NetworkProtocol.Ftp,
            4 => NetworkProtocol.Dav,
            5 => NetworkProtocol.Davs,
            _ => NetworkProtocol.Sftp
        };

        bool isAnonymous = protocolIndex == 3 || AnonymousCheckBox.IsChecked == true;

        NetworkConnectionInfo info = new()
        {
            Protocol = protocol,
            Host = server,
            Port = port,
            Path = PathBox.Text?.Trim(),
            Username = !isAnonymous ? UserBox.Text?.Trim() : null,
            Password = !isAnonymous ? PasswordBox.Text : null,
            Domain = protocol == NetworkProtocol.Smb && !isAnonymous ? DomainBox.Text?.Trim() : null,
            Anonymous = isAnonymous
        };

        (bool success, string mountPath, string error) = await Task.Run(() => _networkService.MountAsync(info));

        ProgressBar.IsVisible = false;
        ConnectButton.IsEnabled = true;

        if (success)
        {
            string bookmarkName = !string.IsNullOrEmpty(info.Username)
                ? $"{info.Username}@{info.Host}"
                : $"{protocol.ToString().ToUpperInvariant()}: {info.Host}";

            if (!string.IsNullOrEmpty(info.Path) && info.Path != "/")
            {
                bookmarkName += info.Path;
            }

            NetworkBookmark bookmark = new()
            {
                Name = bookmarkName,
                Uri = info.BuildUri(),
                Password = info.Password
            };

            await Task.Run(() => new NetworkBookmarkService().AddBookmark(bookmark));

            ResultMountPath = mountPath;
            Close(mountPath);
            return;
        }

        StatusLabel.Text = string.IsNullOrEmpty(error) ? "Failed to connect to server." : error;
        StatusLabel.IsVisible = true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ResultMountPath = null;
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
