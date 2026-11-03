using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class AddonLogDialog : Window
{
    public AddonLogDialog()
    {
        InitializeComponent();
        LoadEntries();

        AddonLogService.Changed += OnLogChanged;
        Closed += (_, _) => AddonLogService.Changed -= OnLogChanged;

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

    private void OnLogChanged()
    {
        Dispatcher.UIThread.Post(LoadEntries);
    }

    private void LoadEntries()
    {
        IReadOnlyList<AddonLogEntry> entries = AddonLogService.Entries;
        EntriesList.ItemsSource = entries;
        EmptyText.IsVisible = entries.Count == 0;
        CopyButton.IsEnabled = entries.Count > 0;
        ClearButton.IsEnabled = entries.Count > 0;

        if (AddonLogService.UnseenCount > 0 && IsVisible)
        {
            AddonLogService.MarkSeen();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        AddonLogService.MarkSeen();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard == null)
        {
            return;
        }

        string text = string.Join("\n\n", AddonLogService.Entries.Select(entry => entry.ToString()));
        await Clipboard.SetTextAsync(text);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        AddonLogService.Clear();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
