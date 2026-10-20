using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;
using LoggerLib;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class AddonStoreDialog : Window
{
    private const int PageSize = 30;

    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _iconRequests = new();
    private List<AddonStoreItemViewModel> _items = new();
    private List<AddonStoreItemViewModel> _filtered = new();
    private int _page;
    private bool _isLoading;

    public AddonStoreDialog()
    {
        InitializeComponent();
        UpdatePaging();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close();
            e.Handled = true;
        }, Tunnel);

        Opened += async (_, _) => await LoadCatalogAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    private int PageCount => Math.Max(1, (int) Math.Ceiling(_filtered.Count / (double) PageSize));

    private async Task LoadCatalogAsync()
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        RefreshButton.IsEnabled = false;
        ShowStatus(LocalizationService.Get("AddonStoreLoading"));

        try
        {
            AddonStoreCatalog catalog = await AddonStoreService.Instance.FetchCatalogAsync(_cts.Token);
            _iconRequests.Clear();
            _items = catalog.Entries
                .Select(e => new AddonStoreItemViewModel(e))
                .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            OfflineText.IsVisible = catalog.IsOffline;
            ApplyFilter();
        }
        catch (AddonInstallException ex)
        {
            _items = new();
            _filtered = new();
            AddonsList.ItemsSource = null;
            UpdatePaging();
            ShowStatus(LocalizationService.Format(ex.Key, ex.Detail));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _isLoading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? "";

        _filtered = _items.Where(i => i.Matches(query)).ToList();
        _page = 0;
        ShowPage();
    }

    private void ShowPage()
    {
        _page = Math.Clamp(_page, 0, PageCount - 1);
        List<AddonStoreItemViewModel> pageItems = _filtered
            .Skip(_page * PageSize)
            .Take(PageSize)
            .ToList();

        AddonsList.ItemsSource = pageItems;
        UpdatePaging();

        if (_items.Count == 0)
        {
            ShowStatus(LocalizationService.Get("AddonStoreEmpty"));
        }
        else if (pageItems.Count == 0)
        {
            ShowStatus(LocalizationService.Get("AddonStoreNoResults"));
        }
        else
        {
            StatusText.IsVisible = false;
        }

        LoadIcons(pageItems);
    }

    private void UpdatePaging()
    {
        PageText.Text = LocalizationService.Format("AddonStorePage", _page + 1, PageCount);
        PreviousPageButton.IsEnabled = _page > 0;
        NextPageButton.IsEnabled = _page < PageCount - 1;
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.IsVisible = true;
    }

    private async void LoadIcons(List<AddonStoreItemViewModel> items)
    {
        foreach (AddonStoreItemViewModel item in items)
        {
            if (item.Icon != null || !_iconRequests.Add(item.Id))
            {
                continue;
            }

            string? path = await AddonStoreService.Instance.GetIconPathAsync(item.Entry, _cts.Token);
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            item.Icon = IconService.LoadFile(path);
        }
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AddonStoreItemViewModel item } || !item.CanInstall)
        {
            return;
        }

        bool isUpdate = item.State == AddonStoreItemState.UpdateAvailable;
        item.Progress = -1;
        item.State = AddonStoreItemState.Installing;

        string? zipPath = null;
        try
        {
            Progress<double> progress = new(value => item.Progress = value);
            zipPath = await AddonStoreService.Instance.DownloadAsync(item.Entry, progress, _cts.Token);

            Addon? addon = await AddonInstaller.InstallFromPathAsync(this, zipPath, !isUpdate);
            if (addon != null)
            {
                Logger.LogI($"Installed addon {addon.Id} {addon.Version} from the addon store");
            }
        }
        catch (AddonInstallException ex)
        {
            await AddonInstaller.ShowErrorAsync(this, LocalizationService.Format(ex.Key, ex.Detail));
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (zipPath != null)
            {
                AddonStoreService.Instance.DeleteDownload(zipPath);
            }

            item.RefreshState();
        }
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        ApplyFilter();
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        await LoadCatalogAsync();
    }

    private void OnPreviousPageClick(object? sender, RoutedEventArgs e)
    {
        _page--;
        ShowPage();
    }

    private void OnNextPageClick(object? sender, RoutedEventArgs e)
    {
        _page++;
        ShowPage();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
