using System;
using System.Collections.Generic;
using System.Linq;
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

public partial class KeybindsDialog : Window
{
    private readonly List<KeybindRowViewModel> _rows = new();
    private KeybindRowViewModel? _capturing;

    public KeybindsDialog()
    {
        InitializeComponent();

        AppGroups.ItemsSource = BuildAppGroups();
        List<KeybindGroupViewModel> addonGroups = BuildAddonGroups();
        AddonGroups.ItemsSource = addonGroups;
        NoAddonsText.IsVisible = addonGroups.Count == 0;

        AddHandler(KeyDownEvent, OnDialogKeyDown, Tunnel);
        Deactivated += (_, _) => StopCapture();
    }

    private List<KeybindGroupViewModel> BuildAppGroups()
    {
        return KeybindService.Definitions
            .GroupBy(definition => definition.CategoryKey)
            .Select(group => new KeybindGroupViewModel(
                LocalizationService.Get(group.Key),
                group.Select(definition => AddRow(definition.Id, LocalizationService.Get(definition.TitleKey), definition.DefaultGesture)).ToList()))
            .ToList();
    }

    private List<KeybindGroupViewModel> BuildAddonGroups()
    {
        string language = LocalizationService.Instance.LanguageCode;
        return AddonService.Instance.GetShortcutActions()
            .GroupBy(action => action.Addon)
            .Select(group => new KeybindGroupViewModel(
                group.Key.GetName(language),
                group.Select(action => AddRow(KeybindService.GetAddonId(action), action.GetTitle(language), action.Shortcut)).ToList()))
            .OrderBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private KeybindRowViewModel AddRow(string id, string title, string? defaultGesture)
    {
        KeybindRowViewModel row = new(id, title.TrimEnd('.', '…').Trim(), defaultGesture);
        _rows.Add(row);
        return row;
    }

    private void OnGestureClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: KeybindRowViewModel row })
        {
            return;
        }

        bool wasCapturing = _capturing == row;
        StopCapture();
        if (wasCapturing)
        {
            return;
        }

        _capturing = row;
        row.IsCapturing = true;
        ShowStatus(null);
    }

    private void OnDialogKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturing == null)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }

            return;
        }

        e.Handled = true;
        if (KeybindService.IsModifierKey(e.Key))
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            StopCapture();
            return;
        }

        _ = AssignAsync(_capturing, new(e.Key, e.KeyModifiers));
    }

    private async Task AssignAsync(KeybindRowViewModel row, KeyGesture gesture)
    {
        StopCapture();
        List<KeybindRowViewModel> conflicts = _rows.Where(other => other != row && gesture.Equals(other.Gesture)).ToList();
        if (conflicts.Count > 0 && !await ConfirmTakeOverAsync(gesture, conflicts))
        {
            return;
        }

        foreach (KeybindRowViewModel other in conflicts)
        {
            KeybindService.Instance.Set(other.Id, null, other.DefaultGesture);
            other.Refresh();
        }

        KeybindService.Instance.Set(row.Id, gesture, row.DefaultGesture);
        _ = Logger.LogI($"Shortcut {row.Id} set to {KeybindService.Format(gesture)}");
        row.Refresh();
        ShowStatus(null);
    }

    private async Task<bool> ConfirmTakeOverAsync(KeyGesture gesture, List<KeybindRowViewModel> conflicts)
    {
        ConfirmDialog dialog = new(
            LocalizationService.Get("KeybindInUseTitle"),
            LocalizationService.Format("KeybindInUseMessage", KeybindService.Format(gesture), string.Join(", ", conflicts.Select(c => c.Title))),
            LocalizationService.Get("KeybindInUseRemove"),
            true,
            false);
        return await dialog.ShowDialog<bool?>(this) == true;
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: KeybindRowViewModel row })
        {
            return;
        }

        StopCapture();
        KeybindService.Instance.Set(row.Id, null, row.DefaultGesture);
        row.Refresh();
        ShowStatus(null);
    }

    private void OnResetRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: KeybindRowViewModel row })
        {
            return;
        }

        StopCapture();
        KeybindService.Instance.Reset(row.Id);
        row.Refresh();
        ShowConflict(row);
    }

    private void OnResetAllClick(object? sender, RoutedEventArgs e)
    {
        StopCapture();
        KeybindService.Instance.ResetAll();
        foreach (KeybindRowViewModel row in _rows)
        {
            row.Refresh();
        }

        ShowStatus(null);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowConflict(KeybindRowViewModel row)
    {
        KeyGesture? gesture = row.Gesture;
        List<string> conflicts = gesture == null
            ? new()
            : _rows.Where(other => other != row && gesture.Equals(other.Gesture)).Select(other => other.Title).ToList();
        ShowStatus(conflicts.Count == 0 ? null : LocalizationService.Format("KeybindConflict", KeybindService.Format(gesture), string.Join(", ", conflicts)));
    }

    private void ShowStatus(string? message)
    {
        StatusText.Text = message ?? "";
        StatusText.IsVisible = message != null;
    }

    private void StopCapture()
    {
        if (_capturing == null)
        {
            return;
        }

        _capturing.IsCapturing = false;
        _capturing = null;
        Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopCapture();
        base.OnClosed(e);
    }
}
