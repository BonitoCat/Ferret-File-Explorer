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
        ServerBox.TextChanged += OnServerTextChanged;
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

    public ConnectToServerDialog(NetworkConnectionInfo login) : this()
    {
        ProtocolComboBox.SelectedIndex = login.Protocol switch
        {
            NetworkProtocol.Smb => 1,
            NetworkProtocol.Ftp => 2,
            NetworkProtocol.Dav => 4,
            NetworkProtocol.Davs => 5,
            _ => 0,
        };

        ServerBox.Text = login.Host;
        PathBox.Text = login.Path ?? "";
        DomainBox.Text = login.Domain ?? "";
        UserBox.Text = string.IsNullOrEmpty(login.Username) ? Environment.UserName : login.Username;
        AnonymousCheckBox.IsChecked = false;
        if (login.Port.HasValue)
        {
            PortBox.Text = login.Port.Value.ToString();
        }

        string share = (login.Path ?? "").Trim('/');
        Title = LocalizationService.Format("NetworkLoginTitle", share.Length > 0 ? share : login.Host, NetworkBrowseService.GetLocationName("smb://" + login.Host));

        Opened += (_, _) =>
        {
            PasswordBox.Focus();
        };
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

    private void OnServerTextChanged(object? sender, TextChangedEventArgs e)
    {
        string text = ServerBox.Text?.Trim() ?? "";
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            return;
        }

        NetworkConnectionInfo parsed = NetworkService.NormalizeServerInput(new() { Host = text, Protocol = (NetworkProtocol) (-1) });
        int index = parsed.Protocol switch
        {
            NetworkProtocol.Sftp => 0,
            NetworkProtocol.Smb => 1,
            NetworkProtocol.Ftp => 2,
            NetworkProtocol.Dav => 4,
            NetworkProtocol.Davs => 5,
            _ => -1,
        };

        if (index >= 0 && ProtocolComboBox.SelectedIndex != index && !(index == 2 && ProtocolComboBox.SelectedIndex == 3))
        {
            ProtocolComboBox.SelectedIndex = index;
        }

        if (parsed.Port.HasValue)
        {
            PortBox.Text = parsed.Port.Value.ToString();
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
            StatusLabel.Text = LocalizationService.Get("EnterServerHost");
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

        NetworkMountResult result = await Task.Run(() => _networkService.ConnectAsync(info));

        ProgressBar.IsVisible = false;
        ConnectButton.IsEnabled = true;

        if (result.Success)
        {
            NetworkBookmark bookmark = new()
            {
                Name = result.DisplayName,
                Uri = result.Info.BuildUri(),
                Password = result.Info.Password
            };

            await Task.Run(() => new NetworkBookmarkService().AddBookmark(bookmark));

            ResultMountPath = result.MountPath;
            Close(result.MountPath);
            return;
        }

        StatusLabel.Text = string.IsNullOrEmpty(result.ErrorMessage) ? LocalizationService.Get("ConnectFailed") : result.ErrorMessage;
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
