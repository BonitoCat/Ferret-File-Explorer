using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Models;
using FileExplorer.Gui.Dialogs;
using FileExplorer.Gui.Services;
using LoggerLib;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private const string AddonItemClass = "addon-item";

    private readonly Dictionary<string, KeyGesture?> _addonGestures = new();

    private void InitializeAddons()
    {
        AddonService.Instance.AddonsChanged += OnAddonsChanged;
        RebuildAddonsMenu();
    }

    private void OnAddonsChanged()
    {
        Dispatcher.UIThread.Post(RebuildAddonsMenu);
    }

    private AddonContext CreateAddonContext()
    {
        return new(ViewModel.CurrentPath, GetSelectedItems().Select(i => i.FullPath));
    }

    private void RebuildAddonsMenu()
    {
        RemoveAddonItems(AddonsMenuItem.Items);

        List<AddonAction> actions = AddonService.Instance.GetActions(AddonLocation.AddonsMenu);
        if (actions.Count == 0)
        {
            return;
        }

        AddonsMenuItem.Items.Add(CreateAddonSeparator());

        string language = LocalizationService.Instance.LanguageCode;
        foreach (IGrouping<Addon, AddonAction> group in actions.GroupBy(a => a.Addon))
        {
            if (group.Count() == 1)
            {
                AddItems(AddonsMenuItem.Items, WrapWithSeparators(group.First(), CreateAddonMenuItem(group.First(), CreateAddonContext)));
                continue;
            }

            MenuItem submenu = new() { Header = group.Key.GetName(language) };
            submenu.Classes.Add(AddonItemClass);
            SetAddonIcon(submenu, group.Key.IconPath);
            foreach (AddonAction action in group)
            {
                AddItems(submenu.Items, WrapWithSeparators(action, CreateAddonMenuItem(action, CreateAddonContext)));
            }

            CollapseAddonSeparators(submenu.Items);
            AddonsMenuItem.Items.Add(submenu);
        }

        CollapseAddonSeparators(AddonsMenuItem.Items);
    }

    private void InjectAddonContextItems(ContextMenu menu)
    {
        RemoveAddonItems(menu.Items);
        if (ViewModel.IsPickerMode)
        {
            return;
        }

        AddonContext context = CreateAddonContext();
        List<AddonAction> actions = AddonService.Instance.GetActions(AddonLocation.ContextMenu, context);
        if (actions.Count == 0)
        {
            return;
        }

        List<AddonAction> indexed = new();
        Dictionary<Control, Control> lastAfter = new();
        foreach (AddonAction action in actions)
        {
            Control? target = FindMenuAnchor(menu.Items, action.After ?? action.Before);
            if (target == null)
            {
                indexed.Add(action);
                continue;
            }

            List<Control> unit = WrapWithSeparators(action, CreateAddonMenuItem(action, () => context));
            if (action.After != null)
            {
                Control previous = lastAfter.GetValueOrDefault(target, target);
                InsertItems(menu.Items, menu.Items.IndexOf(previous) + 1, unit);
                lastAfter[target] = unit[^1];
                continue;
            }

            InsertItems(menu.Items, menu.Items.IndexOf(target), unit);
        }

        List<Control> anchors = menu.Items
            .OfType<Control>()
            .Where(c => c.IsVisible)
            .ToList();
        Dictionary<Control, List<Control>> units = new();
        List<(int? Index, Control Item)> inserts = new();
        foreach (AddonAction action in indexed)
        {
            MenuItem item = CreateAddonMenuItem(action, () => context);
            units[item] = WrapWithSeparators(action, item);
            inserts.Add((action.Index, item));
        }

        List<Control> merged = AddonService.Merge(anchors, inserts);

        List<Control> trailing = new();
        for (int i = 0; i < merged.Count; i++)
        {
            if (!units.TryGetValue(merged[i], out List<Control>? unit))
            {
                continue;
            }

            Control? anchor = merged.Skip(i + 1).FirstOrDefault(c => !units.ContainsKey(c));
            if (anchor == null)
            {
                trailing.AddRange(unit);
                continue;
            }

            InsertItems(menu.Items, menu.Items.IndexOf(anchor), unit);
        }

        InsertTrailingGroup(menu.Items, trailing);
        CollapseAddonSeparators(menu.Items);
    }

    private static Control? FindMenuAnchor(ItemCollection items, string? id)
    {
        if (id == null)
        {
            return null;
        }

        return items
            .OfType<Control>()
            .FirstOrDefault(c => c.IsVisible && c.Tag is string tag && string.Equals(tag, id, StringComparison.OrdinalIgnoreCase));
    }

    private static void InsertTrailingGroup(ItemCollection items, List<Control> group)
    {
        if (group.Count == 0)
        {
            return;
        }

        Separator? lastSeparator = items.OfType<Separator>().LastOrDefault(s => !IsAddonControl(s));
        int insertAt = lastSeparator != null ? items.IndexOf(lastSeparator) : items.Count;
        group.Insert(0, CreateAddonSeparator());
        InsertItems(items, insertAt, group);
    }

    private static List<Control> WrapWithSeparators(AddonAction action, MenuItem item)
    {
        List<Control> unit = new();
        if (action.SeparatorBefore)
        {
            unit.Add(CreateAddonSeparator());
        }

        unit.Add(item);

        if (action.SeparatorAfter)
        {
            unit.Add(CreateAddonSeparator());
        }

        return unit;
    }

    private static void AddItems(ItemCollection items, IEnumerable<Control> controls)
    {
        foreach (Control control in controls)
        {
            items.Add(control);
        }
    }

    private static void InsertItems(ItemCollection items, int index, IEnumerable<Control> controls)
    {
        foreach (Control control in controls)
        {
            items.Insert(index, control);
            index++;
        }
    }

    private static void CollapseAddonSeparators(ItemCollection items)
    {
        List<Control> visible = items
            .OfType<Control>()
            .Where(c => c.IsVisible)
            .ToList();

        Control? lastKept = null;
        foreach (Control control in visible)
        {
            if (control is Separator && lastKept is null or Separator)
            {
                if (IsAddonControl(control))
                {
                    control.IsVisible = false;
                    continue;
                }

                if (lastKept != null && IsAddonControl(lastKept))
                {
                    lastKept.IsVisible = false;
                }
            }

            lastKept = control;
        }

        if (lastKept is Separator && IsAddonControl(lastKept))
        {
            lastKept.IsVisible = false;
        }
    }

    private static bool IsAddonControl(Control control)
    {
        return control.Classes.Contains(AddonItemClass);
    }

    private static void RemoveAddonItems(ItemCollection items)
    {
        List<Control> stale = items
            .OfType<Control>()
            .Where(IsAddonControl)
            .ToList();
        foreach (Control control in stale)
        {
            items.Remove(control);
        }
    }

    private static Separator CreateAddonSeparator()
    {
        Separator separator = new();
        separator.Classes.Add(AddonItemClass);
        return separator;
    }

    private MenuItem CreateAddonMenuItem(AddonAction action, Func<AddonContext> contextFactory)
    {
        MenuItem item = new()
        {
            Header = action.GetTitle(LocalizationService.Instance.LanguageCode),
            Tag = action.Key,
        };
        item.Classes.Add(AddonItemClass);
        SetAddonIcon(item, action.IconPath);

        KeyGesture? gesture = GetAddonGesture(action);
        if (gesture != null)
        {
            item.InputGesture = gesture;
        }

        item.Click += (_, _) => RunAddonAction(action, contextFactory());
        return item;
    }

    private static void SetAddonIcon(MenuItem item, string? iconPath)
    {
        IImage? icon = IconService.LoadFile(iconPath);
        if (icon == null)
        {
            return;
        }

        item.Icon = new Image
        {
            Source = icon,
            Width = 16,
            Height = 16,
        };
    }

    private KeyGesture? GetAddonGesture(AddonAction action)
    {
        if (action.Shortcut == null)
        {
            return null;
        }

        if (_addonGestures.TryGetValue(action.Shortcut, out KeyGesture? cached))
        {
            return cached;
        }

        KeyGesture? gesture = null;
        try
        {
            gesture = KeyGesture.Parse(action.Shortcut);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Logger.LogW($"Invalid shortcut '{action.Shortcut}' in addon action {action.Key}");
        }

        _addonGestures[action.Shortcut] = gesture;
        return gesture;
    }

    private bool TryRunAddonShortcut(KeyEventArgs e)
    {
        if (ViewModel.IsPickerMode)
        {
            return false;
        }

        bool hasCommandModifier = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;
        bool isTyping = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        if (isTyping && !hasCommandModifier)
        {
            return false;
        }

        AddonContext? context = null;
        foreach (AddonAction action in AddonService.Instance.GetShortcutActions())
        {
            if (GetAddonGesture(action)?.Matches(e) != true)
            {
                continue;
            }

            context ??= CreateAddonContext();
            if (!action.Matches(context))
            {
                continue;
            }

            RunAddonAction(action, context);
            return true;
        }

        return false;
    }

    private bool TryRunAddonPathBarCommand(string keyword, string input)
    {
        AddonAction? action = AddonService.Instance.FindPathBarAction(keyword);
        if (action == null)
        {
            return false;
        }

        RunAddonAction(action, new(ViewModel.CurrentPath, GetSelectedItems().Select(i => i.FullPath), input));
        return true;
    }

    private async void RunAddonAction(AddonAction action, AddonContext context)
    {
        string language = LocalizationService.Instance.LanguageCode;
        string addonName = action.Addon.GetName(language);

        if (action.ConfirmKey != null)
        {
            ConfirmDialog confirm = new(action.GetTitle(language), action.Addon.Translate(action.ConfirmKey, language), LocalizationService.Get("OK"), true, false);
            if (await confirm.ShowDialog<bool?>(this) != true)
            {
                return;
            }
        }

        AddonLaunch launch = AddonRunner.Prepare(action, context, language);
        Logger.LogI($"Running addon action {action.Key}");

        if (action.Terminal)
        {
            ViewModel.RunInTerminalAt(launch.WorkingDirectory, launch.Command, launch.Environment);
            return;
        }

        Process? process = null;
        try
        {
            process = AddonRunner.Start(
                launch,
                command => Dispatcher.UIThread.Post(() => HandleAddonCommand(addonName, command, process)),
                (exitCode, stderr) => Dispatcher.UIThread.Post(() => OnAddonExited(action, addonName, exitCode, stderr)));
        }
        catch (Exception ex)
        {
            Logger.LogE($"Addon action {action.Key} failed to start: {ex.Message}");
            ViewModel.StatusText = LocalizationService.Format("AddonRunFailed", addonName, ex.Message);
        }
    }

    private void OnAddonExited(AddonAction action, string addonName, int exitCode, string stderr)
    {
        if (exitCode == 0)
        {
            return;
        }

        Logger.LogW($"Addon action {action.Key} exited with code {exitCode}: {stderr}");

        string? lastError = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
        ViewModel.StatusText = string.IsNullOrEmpty(lastError)
            ? LocalizationService.Format("AddonExitedWithCode", addonName, exitCode)
            : LocalizationService.Format("AddonExitedWithError", addonName, lastError);
    }

    private async void HandleAddonCommand(string addonName, AddonCommand command, Process? process)
    {
        string argument = command.Argument.Trim();
        switch (command.Kind)
        {
            case AddonCommandKind.Navigate:
                ViewModel.NavigateTo(ResolveAddonPath(argument));
                break;
            case AddonCommandKind.Refresh:
                ViewModel.Refresh();
                break;
            case AddonCommandKind.Status:
                ViewModel.StatusText = command.Argument;
                break;
            case AddonCommandKind.Message:
                ConfirmDialog dialog = new(addonName, command.Argument);
                await dialog.ShowDialog<bool?>(this);
                break;
            case AddonCommandKind.Open:
                OpenAddonPath(ResolveAddonPath(argument));
                break;
            case AddonCommandKind.Prompt:
                InputDialog input = new(addonName, command.Argument);
                string? answer = await input.ShowDialog<string?>(this);
                ReplyToAddon(process, answer);
                break;
        }
    }

    private static void ReplyToAddon(Process? process, string? answer)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            if (answer == null)
            {
                process.StandardInput.Close();
                return;
            }

            process.StandardInput.WriteLine(answer.Replace("\r", " ").Replace('\n', ' '));
            process.StandardInput.Flush();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Logger.LogW($"Could not send prompt answer to addon: {ex.Message}");
        }
    }

    private string ResolveAddonPath(string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ViewModel.CurrentPath, path));
    }

    private void OpenAddonPath(string path)
    {
        if (Directory.Exists(path))
        {
            ViewModel.NavigateTo(path);
            return;
        }

        if (!File.Exists(path))
        {
            return;
        }

        ViewModel.ShowFileContent(path);
    }

    private async void OnMenuManageAddonsClick(object? sender, RoutedEventArgs e)
    {
        AddonsDialog dialog = new(ViewModel.NavigateTo);
        await dialog.ShowDialog(this);
    }

    private async void OnMenuAddonStoreClick(object? sender, RoutedEventArgs e)
    {
        AddonStoreDialog dialog = new();
        await dialog.ShowDialog(this);
    }

    private async void OnMenuInstallAddonFolderClick(object? sender, RoutedEventArgs e)
    {
        await InstallAddonAsync(PickerMode.Folder);
    }

    private async void OnMenuInstallAddonZipClick(object? sender, RoutedEventArgs e)
    {
        await InstallAddonAsync(PickerMode.File);
    }

    private async Task InstallAddonAsync(PickerMode mode)
    {
        Addon? addon = await AddonInstaller.InstallAsync(this, mode);
        if (addon == null)
        {
            return;
        }

        Logger.LogI($"Installed addon {addon.Id}");
        ViewModel.StatusText = LocalizationService.Format("AddonInstalled", addon.GetName(LocalizationService.Instance.LanguageCode));
    }

    private void OnMenuOpenAddonsFolderClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.NavigateTo(AddonService.AddonsDirectory);
    }

    private void OnMenuReloadAddonsClick(object? sender, RoutedEventArgs e)
    {
        AddonService.Instance.Reload();
    }
}
