using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Services;
using LoggerLib;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class SmbShareDialog : Window
{
    private readonly SmbShareService _shareService = new();
    private readonly string _path = "";
    private SmbShareInfo? _existing;
    private SmbShareAvailability _availability;

    public SmbShareDialog()
    {
        InitializeComponent();

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

    public SmbShareDialog(string path) : this()
    {
        _path = path;
        HeaderText.Text = Path.GetFileName(path.TrimEnd('/'));
        NameTextBox.Text = SmbShareService.SuggestName(path);
        SetEnabled(false, false);

        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        (SmbShareAvailability availability, string detail, SmbShareInfo? existing) = await Task.Run(() =>
        {
            (SmbShareAvailability state, string message) = _shareService.GetAvailability();
            SmbShareInfo? share = state == SmbShareAvailability.Available ? _shareService.GetShareForPath(_path) : null;
            return (state, message, share);
        });

        _availability = availability;
        if (availability != SmbShareAvailability.Available)
        {
            ShowUnavailable(availability, detail);
            return;
        }

        UnavailableBorder.IsVisible = false;
        ApplyButton.IsEnabled = true;
        _existing = existing;
        if (existing != null)
        {
            NameTextBox.Text = existing.Name;
            CommentTextBox.Text = existing.Comment;
            WritableCheckBox.IsChecked = existing.Writable;
            GuestCheckBox.IsChecked = existing.GuestOk;
        }

        ShareCheckBox.IsChecked = existing != null;
        SetEnabled(true, existing != null);
        UpdateParentWarning();
    }

    private void ShowUnavailable(SmbShareAvailability availability, string detail)
    {
        bool needsRelogin = availability == SmbShareAvailability.NotPermitted && SmbShareService.IsInUsershareGroup();
        bool canSetup = SmbShareService.CanSetup && !needsRelogin;

        UnavailableText.Text = (availability, needsRelogin, canSetup) switch
        {
            (_, true, _) => LocalizationService.Get("SambaReloginRequired"),
            (SmbShareAvailability.NotInstalled, _, true) => LocalizationService.Get("SambaNotInstalled"),
            (SmbShareAvailability.NotInstalled, _, false) => LocalizationService.Get("SambaNotInstalledManual"),
            (_, _, true) => LocalizationService.Get("SambaNotEnabled"),
            _ => LocalizationService.Format("SambaNotPermitted", detail),
        };

        SetupButton.Content = LocalizationService.Get(availability == SmbShareAvailability.NotInstalled ? "SambaInstall" : "SambaEnable");
        SetupButton.IsVisible = canSetup;
        SetupButton.IsEnabled = true;
        UnavailableBorder.IsVisible = true;
        ApplyButton.IsEnabled = false;
        SetEnabled(false, false);
    }

    private async void OnSetupClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        SetupButton.IsEnabled = false;
        SetupButton.Content = LocalizationService.Get(_availability == SmbShareAvailability.NotInstalled ? "SambaInstalling" : "SambaEnabling");

        (SmbSetupResult result, string detail) = await _shareService.SetupAsync(_availability == SmbShareAvailability.NotInstalled);
        if (result == SmbSetupResult.Failed)
        {
            ShowError(LocalizationService.Format("SambaSetupFailed", detail));
        }

        Logger.LogI($"Samba setup finished: {result}");
        await LoadAsync();
    }

    private void SetEnabled(bool available, bool shared)
    {
        ShareCheckBox.IsEnabled = available;
        SettingsGrid.IsEnabled = available && shared;
        OptionsPanel.IsEnabled = available && shared;
    }

    private void OnShareCheckedChanged(object? sender, RoutedEventArgs e)
    {
        SetEnabled(ShareCheckBox.IsEnabled, ShareCheckBox.IsChecked == true);
        UpdateParentWarning();
    }

    private void OnGuestCheckedChanged(object? sender, RoutedEventArgs e)
    {
        UpdateParentWarning();
    }

    private void UpdateParentWarning()
    {
        string? blocking = ShareCheckBox.IsChecked == true && GuestCheckBox.IsChecked == true
            ? SmbShareService.FindBlockingParent(_path)
            : null;

        ParentWarningText.IsVisible = blocking != null;
        ParentWarningText.Text = blocking != null ? LocalizationService.Format("ShareParentBlocked", blocking) : "";
    }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;

        if (ShareCheckBox.IsChecked != true)
        {
            await StopSharingAsync();
            return;
        }

        string name = (NameTextBox.Text ?? "").Trim();
        if (!SmbShareService.IsValidName(name))
        {
            ShowError(LocalizationService.Format("ShareNameInvalid", SmbShareService.MaxNameLength));
            return;
        }

        if (SmbShareService.IsUserName(name))
        {
            ShowError(LocalizationService.Format("ShareNameIsUser", name));
            return;
        }

        bool writable = WritableCheckBox.IsChecked == true;
        if (!await EnsurePermissionsAsync(writable))
        {
            return;
        }

        string comment = (CommentTextBox.Text ?? "").Trim();
        bool guestOk = GuestCheckBox.IsChecked == true;
        string? previousName = _existing?.Name;
        string? error = await Task.Run(() => _shareService.SetShare(name, _path, comment, writable, guestOk, previousName));
        if (error != null)
        {
            ShowError(LocalizationService.Format("ShareFailed", error));
            return;
        }

        Logger.LogI("Shared folder via SMB");
        string status = LocalizationService.Format("FolderShared", name);
        if (_existing == null || !IsRestricted(_existing, name, writable, guestOk))
        {
            Close(status);
            return;
        }

        Close(await DisconnectAsync(_existing.Name, status));
    }

    private static bool IsRestricted(SmbShareInfo previous, string name, bool writable, bool guestOk)
    {
        bool renamed = !string.Equals(previous.Name, name, StringComparison.OrdinalIgnoreCase);
        return renamed || (previous.Writable && !writable) || (previous.GuestOk && !guestOk);
    }

    private async Task<string> DisconnectAsync(string name, string status)
    {
        IsEnabled = false;
        (SmbSetupResult result, string detail) = await _shareService.DisconnectShareAsync(name);
        IsEnabled = true;

        if (result == SmbSetupResult.Success)
        {
            return status;
        }

        Logger.LogW($"Could not disconnect share clients: {result} {detail}");
        return LocalizationService.Format("ShareClientsStillConnected", status);
    }

    private async Task StopSharingAsync()
    {
        if (_existing == null)
        {
            Close(null);
            return;
        }

        string name = _existing.Name;
        string? error = await Task.Run(() => _shareService.RemoveShare(name));
        if (error != null)
        {
            ShowError(LocalizationService.Format("ShareFailed", error));
            return;
        }

        Logger.LogI("Stopped sharing folder via SMB");
        Close(await DisconnectAsync(name, LocalizationService.Get("FolderUnshared")));
    }

    private async Task<bool> EnsurePermissionsAsync(bool writable)
    {
        UnixFileMode missing;
        try
        {
            missing = SmbShareService.GetMissingPermissions(_path, writable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(LocalizationService.Format("ShareFailed", ex.Message));
            return false;
        }

        if (missing == UnixFileMode.None)
        {
            return true;
        }

        ConfirmDialog confirm = new(
            LocalizationService.Get("SharingOptions"),
            LocalizationService.Get("SharePermissionsNeeded"),
            LocalizationService.Get("SharePermissionsAdd"),
            true,
            false);
        if (await confirm.ShowDialog<bool?>(this) != true)
        {
            return false;
        }

        try
        {
            SmbShareService.AddPermissions(_path, missing);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(LocalizationService.Format("ShareFailed", ex.Message));
            return false;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
