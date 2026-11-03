using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Models;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;
using LoggerLib;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class AddonsDialog : Window
{
    private readonly Action<string>? _openFolder;

    public AddonsDialog()
    {
        InitializeComponent();
        LoadAddons(null);

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

    public AddonsDialog(Action<string> openFolder) : this()
    {
        _openFolder = openFolder;
    }

    private AddonItemViewModel? SelectedAddon => AddonsList.SelectedItem as AddonItemViewModel;

    private void LoadAddons(string? selectId)
    {
        List<AddonItemViewModel> items = AddonService.Instance.Addons
            .Select(a => new AddonItemViewModel(a))
            .ToList();

        AddonsList.ItemsSource = items;
        AddonsList.SelectedItem = items.FirstOrDefault(i => i.Id == selectId) ?? items.FirstOrDefault();
        EmptyText.IsVisible = items.Count == 0;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        List<AddonItemViewModel> items = AddonsList.ItemsSource?.Cast<AddonItemViewModel>().ToList() ?? new();
        AddonItemViewModel? selected = SelectedAddon;
        int index = selected != null ? items.IndexOf(selected) : -1;

        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < items.Count - 1;
        UninstallButton.IsEnabled = selected != null;
        SettingsButton.IsEnabled = selected != null && AddonService.Instance.Find(selected.Id)?.Settings.Count > 0;
        DependenciesButton.IsEnabled = selected?.HasMissing == true;
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        AddonItemViewModel? selected = SelectedAddon;
        Addon? addon = selected == null ? null : AddonService.Instance.Find(selected.Id);
        if (addon == null || addon.Settings.Count == 0)
        {
            return;
        }

        await AddonSettingsEditor.EditAsync(this, addon);
    }

    private async void OnDependenciesClick(object? sender, RoutedEventArgs e)
    {
        AddonItemViewModel? selected = SelectedAddon;
        Addon? addon = selected == null ? null : AddonService.Instance.Find(selected.Id);
        if (addon == null)
        {
            return;
        }

        await AddonDependencyDialog.ResolveAsync(this, addon);
        LoadAddons(addon.Id);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e)
    {
        Move(-1);
    }

    private void OnMoveDownClick(object? sender, RoutedEventArgs e)
    {
        Move(1);
    }

    private void Move(int offset)
    {
        AddonItemViewModel? selected = SelectedAddon;
        if (selected == null)
        {
            return;
        }

        AddonService.Instance.Move(selected.Id, offset);
        LoadAddons(selected.Id);
    }

    private async void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        AddonItemViewModel? selected = SelectedAddon;
        if (selected == null)
        {
            return;
        }

        ConfirmDialog confirm = new(
            LocalizationService.Get("UninstallAddonTitle"),
            LocalizationService.Format("ConfirmUninstallAddon", selected.Name),
            LocalizationService.Get("Uninstall"),
            true,
            true);
        if (await confirm.ShowDialog<bool?>(this) != true)
        {
            return;
        }

        Addon? addon = AddonService.Instance.Find(selected.Id);
        if (addon != null)
        {
            await AddonHooks.RunAsync(addon, AddonHook.PreUninstall);
        }

        try
        {
            AddonService.Instance.Uninstall(selected.Id);
            Logger.LogI($"Uninstalled addon {selected.Id}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await AddonInstaller.ShowErrorAsync(this, LocalizationService.Format("AddonUninstallFailed", ex.Message));
        }

        LoadAddons(null);
    }

    private async void OnAddonStoreClick(object? sender, RoutedEventArgs e)
    {
        AddonStoreDialog dialog = new();
        await dialog.ShowDialog(this);
        LoadAddons(SelectedAddon?.Id);
    }

    private async void OnInstallFolderClick(object? sender, RoutedEventArgs e)
    {
        await InstallAsync(PickerMode.Folder);
    }

    private async void OnInstallZipClick(object? sender, RoutedEventArgs e)
    {
        await InstallAsync(PickerMode.File);
    }

    private async Task InstallAsync(PickerMode mode)
    {
        Addon? addon = await AddonInstaller.InstallAsync(this, mode);
        LoadAddons(addon?.Id ?? SelectedAddon?.Id);
    }

    private async void OnDonateClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || !AddonDonation.TryParse(url, out _))
        {
            return;
        }

        await Launcher.LaunchUriAsync(new Uri(url));
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        Close();
        _openFolder?.Invoke(AddonService.AddonsDirectory);
    }

    private void OnReloadClick(object? sender, RoutedEventArgs e)
    {
        string? selectedId = SelectedAddon?.Id;
        AddonService.Instance.Reload();
        AddonHooks.RequestServiceRestart();
        LoadAddons(selectedId);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
