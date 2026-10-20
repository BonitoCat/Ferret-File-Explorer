using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Models;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Dialogs;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;
using LoggerLib;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private const string AddonItemClass = "addon-item";

    private static readonly TimeSpan AddonStopGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PreCloseStopGrace = TimeSpan.FromSeconds(1);

    private sealed class AddonRun
    {
        public Addon Addon { get; }
        public string Source { get; }
        public string Title { get; }
        public Process? Process { get; set; }
        public OperationProgressViewModel? Progress { get; set; }
        public bool IsLoading { get; set; }
        public bool HasExited { get; set; }
        public bool WasStopped { get; set; }
        public bool IsService { get; init; }
        public Channel<string>? Input { get; set; }
        public AddonPendingForm? PendingForm { get; set; }
        public TaskCompletionSource<int> Exited { get; } = new();

        public string AddonName => Addon.GetName(LocalizationService.Instance.LanguageCode);

        public AddonRun(Addon addon, string source, string title)
        {
            Addon = addon;
            Source = source;
            Title = title;
        }
    }

    private readonly Dictionary<string, KeyGesture?> _addonGestures = new();
    private readonly HashSet<string> _loadedAddons = new(StringComparer.Ordinal);
    private bool _isAddonHookHost;
    private bool _addonHooksReady;
    private bool _preCloseHooksDone;

    private AddonMode CurrentAddonMode => ViewModel.PickerMode switch
    {
        PickerMode.File => AddonMode.FilePicker,
        PickerMode.Folder => AddonMode.FolderPicker,
        PickerMode.Save => AddonMode.SavePicker,
        _ => AddonMode.Explorer,
    };

    private void InitializeAddons()
    {
        AddonService.Instance.AddonsChanged += OnAddonsChanged;
        AddonLogService.Changed += OnAddonLogChanged;
        InitializeAddonServices();
        RebuildAddonsMenu();
        RefreshAddonPlaces();
        AddonLogService.ReportLoadErrors(AddonService.Instance.Addons);
        AddonLogService.ReportMissingDependencies(AddonService.Instance.Addons);
        UpdateAddonErrorCount();
        Closed += (_, _) => ShutdownAddons();

        if (AddonHooks.Runner != null)
        {
            return;
        }

        _isAddonHookHost = true;
        AddonHooks.Runner = RunAddonHookAsync;
        AddonHooks.InstallFinished += RunPendingLoadHooks;
        Opened += OnAddonHostOpened;
        Closing += OnAddonHostClosing;
    }

    private void ShutdownAddons()
    {
        AddonLogService.Changed -= OnAddonLogChanged;
        ShutdownAddonServices();
        if (!_isAddonHookHost)
        {
            return;
        }

        AddonHooks.InstallFinished -= RunPendingLoadHooks;
        AddonHooks.Runner = null;
    }

    private void OnAddonsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RebuildAddonsMenu();
            AddonLogService.ReportLoadErrors(AddonService.Instance.Addons);
            AddonLogService.ReportMissingDependencies(AddonService.Instance.Addons);
            RemoveInactiveAddonContent();
            RefreshAddonPlaces();
            if (_isAddonHookHost)
            {
                RunPendingLoadHooks();
            }
        });
    }

    private void OnAddonLogChanged()
    {
        Dispatcher.UIThread.Post(UpdateAddonErrorCount);
    }

    private void UpdateAddonErrorCount()
    {
        ViewModel.AddonErrorCount = AddonLogService.UnseenCount;
    }

    private async void OnAddonErrorsClick(object? sender, RoutedEventArgs e)
    {
        AddonLogDialog dialog = new();
        await dialog.ShowDialog(this);
    }

    private void OnAddonHostOpened(object? sender, EventArgs e)
    {
        Opened -= OnAddonHostOpened;
        _addonHooksReady = true;
        RunPendingLoadHooks();

        foreach (Addon addon in AddonService.Instance.GetHookAddons(AddonHook.PostStart, CurrentAddonMode))
        {
            _ = RunAddonHookAsync(addon, AddonHook.PostStart);
        }
    }

    private async void OnAddonHostClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_preCloseHooksDone)
        {
            return;
        }

        _preCloseHooksDone = true;
        List<Addon> addons = AddonService.Instance.GetHookAddons(AddonHook.PreClose, CurrentAddonMode);
        List<AddonRun> services = TakeAddonServices();
        if (addons.Count == 0 && services.Count == 0)
        {
            return;
        }

        e.Cancel = true;
        await Task.WhenAll(addons
            .Select(a => RunAddonHookAsync(a, AddonHook.PreClose))
            .Concat(services.Select(s => StopAddonServiceAsync(s, ServiceCloseTimeout))));
        Close();
    }

    private void RunPendingLoadHooks()
    {
        if (!_addonHooksReady)
        {
            return;
        }

        List<Addon> active = AddonService.Instance.Addons
            .Where(a => a.IsActive && a.SupportsMode(CurrentAddonMode))
            .ToList();
        HashSet<string> activeKeys = active.Select(GetAddonLoadKey).ToHashSet(StringComparer.Ordinal);
        _loadedAddons.RemoveWhere(key => !activeKeys.Contains(key));

        foreach (Addon addon in active)
        {
            if (AddonHooks.IsInstalling(addon.Id) || !_loadedAddons.Add(GetAddonLoadKey(addon)))
            {
                continue;
            }

            if (addon.Hooks.ContainsKey(AddonHook.PostLoad))
            {
                _ = RunAddonHookAsync(addon, AddonHook.PostLoad);
            }
        }

        AddonLogService.ReportMissingDependencies(active);
        SyncAddonServices(false);
    }

    private static string GetAddonLoadKey(Addon addon)
    {
        return $"{addon.RootPath}|{addon.Version}";
    }

    private static TimeSpan? GetHookTimeout(AddonHook hook)
    {
        return hook switch
        {
            AddonHook.PreClose => TimeSpan.FromSeconds(3),
            AddonHook.PreUninstall => TimeSpan.FromSeconds(30),
            AddonHook.PostInstall => TimeSpan.FromMinutes(5),
            _ => null,
        };
    }

    private async Task RunAddonHookAsync(Addon addon, AddonHook hook)
    {
        string language = LocalizationService.Instance.LanguageCode;
        string hookName = AddonRunner.GetHookName(hook);
        AddonLaunch launch = AddonRunner.PrepareHook(addon, hook, CreateAddonContext(), language);
        Logger.LogI($"Running addon hook {addon.Id}/{hookName}");

        AddonRun? run = StartAddonRun(addon, hookName, addon.GetName(language), launch);
        if (run == null)
        {
            return;
        }

        TimeSpan? timeout = GetHookTimeout(hook);
        if (timeout == null)
        {
            await run.Exited.Task;
            return;
        }

        Task finished = await Task.WhenAny(run.Exited.Task, Task.Delay(timeout.Value));
        if (finished == run.Exited.Task)
        {
            return;
        }

        AddonLogService.Add(addon, hookName, AddonLogLevel.Warning, LocalizationService.Format("AddonLogHookTimeout", (int) timeout.Value.TotalSeconds));
        await StopAddonRunAsync(run, hook == AddonHook.PreClose ? PreCloseStopGrace : AddonStopGrace);
    }

    private AddonContext CreateAddonContext()
    {
        return new(ViewModel.CurrentPath, GetSelectedItems().Select(i => i.FullPath), "", CurrentAddonMode);
    }

    private void RebuildAddonsMenu()
    {
        RemoveAddonItems(AddonsMenuItem.Items);

        List<AddonAction> actions = AddonService.Instance.GetActions(AddonLocation.AddonsMenu, CurrentAddonMode);
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
        AddonAction? action = AddonService.Instance.FindPathBarAction(keyword, CurrentAddonMode);
        if (action == null)
        {
            return false;
        }

        RunAddonAction(action, new(ViewModel.CurrentPath, GetSelectedItems().Select(i => i.FullPath), input, CurrentAddonMode));
        return true;
    }

    private async void RunAddonAction(AddonAction action, AddonContext context)
    {
        string language = LocalizationService.Instance.LanguageCode;

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

        StartAddonRun(action.Addon, action.Id, action.GetTitle(language), launch);
    }

    private AddonRun? StartAddonRun(Addon addon, string source, string title, AddonLaunch launch, bool isService = false)
    {
        AddonRun run = new(addon, source, title) { IsService = isService };
        try
        {
            run.Process = AddonRunner.Start(
                launch,
                command => Dispatcher.UIThread.Post(() => HandleAddonCommand(run, command)),
                (exitCode, stderr) => Dispatcher.UIThread.Post(() => OnAddonExited(run, exitCode, stderr)));
        }
        catch (Exception ex)
        {
            Logger.LogE($"Addon {addon.Id} ({source}) failed to start: {ex.Message}");
            ViewModel.StatusText = LocalizationService.Format("AddonRunFailed", run.AddonName, ex.Message);
            AddonLogService.Add(addon, source, AddonLogLevel.Error, LocalizationService.Format("AddonLogStartFailed", ex.Message), string.Join(' ', launch.Command));
            return null;
        }

        return run;
    }

    private static async Task StopAddonRunAsync(AddonRun run, TimeSpan grace)
    {
        run.WasStopped = true;
        if (run.Process == null)
        {
            return;
        }

        await AddonRunner.StopAsync(run.Process, grace);
    }

    private void OnAddonExited(AddonRun run, int exitCode, string stderr)
    {
        run.HasExited = true;
        EndAddonProgress(run);
        SetAddonLoading(run, false);
        run.Input?.Writer.TryComplete();
        OnAddonServiceExited(run);
        run.Exited.TrySetResult(exitCode);

        if (exitCode == 0 || run.WasStopped)
        {
            return;
        }

        string? lastError = TranslateAddonText(run, stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim());
        ViewModel.ShowStatusMessage(string.IsNullOrEmpty(lastError)
            ? LocalizationService.Format("AddonExitedWithCode", run.AddonName, exitCode)
            : LocalizationService.Format("AddonExitedWithError", run.AddonName, lastError));
        AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Error, LocalizationService.Format("AddonLogExitCode", exitCode), stderr);
    }

    private async void HandleAddonCommand(AddonRun run, AddonCommand command)
    {
        string argument = command.Argument.Trim();
        string text = TranslateAddonText(run, command.Argument);
        switch (command.Kind)
        {
            case AddonCommandKind.Navigate:
                ViewModel.NavigateTo(ResolveAddonPath(argument));
                break;
            case AddonCommandKind.Refresh:
                ViewModel.Refresh();
                break;
            case AddonCommandKind.Status:
                ViewModel.ShowStatusMessage(text);
                break;
            case AddonCommandKind.Message:
                ConfirmDialog dialog = new(run.AddonName, text);
                await dialog.ShowDialog<bool?>(this);
                break;
            case AddonCommandKind.Open:
                OpenAddonPath(ResolveAddonPath(argument));
                break;
            case AddonCommandKind.Prompt:
                InputDialog input = new(run.AddonName, text);
                string? answer = await input.ShowDialog<string?>(this);
                ReplyToRun(run, answer);
                break;
            case AddonCommandKind.Confirm:
                ConfirmDialog confirm = new(run.AddonName, text, LocalizationService.Get("OK"), true, false);
                bool confirmed = await confirm.ShowDialog<bool?>(this) == true;
                ReplyToRun(run, confirmed ? "yes" : "no");
                break;
            case AddonCommandKind.Choose:
                await ShowAddonChoiceAsync(run, command.Argument);
                break;
            case AddonCommandKind.Form:
                run.PendingForm = new(text.Trim());
                break;
            case AddonCommandKind.Field:
                AddAddonFormField(run, argument);
                break;
            case AddonCommandKind.FormShow:
                await ShowAddonFormAsync(run);
                break;
            case AddonCommandKind.StatusBar:
                SetAddonStatusItem(run, argument);
                break;
            case AddonCommandKind.StatusBarTooltip:
                SetAddonStatusTooltip(run, argument);
                break;
            case AddonCommandKind.Place:
                SetAddonPlace(run, argument);
                break;
            case AddonCommandKind.PlaceRemove:
                RemoveAddonPlace(run, argument);
                break;
            case AddonCommandKind.Setting:
                SaveAddonSetting(run, argument);
                break;
            case AddonCommandKind.Progress:
                UpdateAddonProgress(run, argument);
                break;
            case AddonCommandKind.ProgressMax:
                SetAddonProgressMaximum(run, argument);
                break;
            case AddonCommandKind.ProgressTitle:
                EditAddonProgress(run, p => p.Title = argument.Length > 0 ? text.Trim() : run.Title);
                break;
            case AddonCommandKind.ProgressText:
                EditAddonProgress(run, p => p.Text = text.Trim());
                break;
            case AddonCommandKind.ProgressStatus:
                EditAddonProgress(run, p => p.Status = text.Trim());
                break;
            case AddonCommandKind.ProgressItem:
                EditAddonProgress(run, p => p.Item = argument);
                break;
            case AddonCommandKind.Loading:
                SetAddonLoadingFromCommand(run, argument);
                break;
            case AddonCommandKind.Select:
                SelectByPattern(argument);
                break;
            case AddonCommandKind.Pick:
                await PickFromAddonAsync(run, argument);
                break;
            case AddonCommandKind.Clipboard:
                await ReplyAddonClipboardAsync(run);
                break;
            case AddonCommandKind.Copy:
                await CopyFromAddonAsync(command.Argument);
                break;
            case AddonCommandKind.CopyFiles:
                CopyAddonPathsToClipboard(run, argument, ClipboardMode.Copy);
                break;
            case AddonCommandKind.CutFiles:
                CopyAddonPathsToClipboard(run, argument, ClipboardMode.Cut);
                break;
            case AddonCommandKind.CopyTo:
                await TransferFromAddonAsync(run, argument, true);
                break;
            case AddonCommandKind.MoveTo:
                await TransferFromAddonAsync(run, argument, false);
                break;
            case AddonCommandKind.Trash:
                await TrashFromAddonAsync(run, argument);
                break;
            case AddonCommandKind.Reveal:
                RevealFromAddon(run, argument);
                break;
            case AddonCommandKind.Terminal:
                ViewModel.OpenTerminal(argument.Length > 0 ? ResolveAddonPath(Addon.ExpandHome(argument)) : null);
                break;
            case AddonCommandKind.Notify:
                await NotifyFromAddonAsync(run, text.Trim());
                break;
            case AddonCommandKind.Log:
                LogFromAddon(run, argument);
                break;
            case AddonCommandKind.Unknown:
                AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Format("AddonLogUnknownCommand", command.Argument));
                break;
        }
    }

    private static string TranslateAddonText(AddonRun run, string? text)
    {
        return run.Addon.TranslateText(text, LocalizationService.Instance.LanguageCode);
    }

    private OperationProgressViewModel? EnsureAddonProgress(AddonRun run)
    {
        if (run.HasExited)
        {
            return null;
        }

        if (run.Progress != null)
        {
            return run.Progress;
        }

        run.Progress = new()
        {
            Title = run.Title,
            Icon = IconService.LoadFile(run.Addon.IconPath) ?? IconService.Get("extension"),
            IsIndeterminate = true,
            CancelAction = () => _ = StopAddonRunAsync(run, AddonStopGrace),
        };
        ViewModel.AddOperation(run.Progress);
        return run.Progress;
    }

    private void EndAddonProgress(AddonRun run)
    {
        if (run.Progress == null)
        {
            return;
        }

        ViewModel.RemoveOperation(run.Progress);
        run.Progress = null;
    }

    private void EditAddonProgress(AddonRun run, Action<OperationProgressViewModel> edit)
    {
        OperationProgressViewModel? progress = EnsureAddonProgress(run);
        if (progress == null)
        {
            return;
        }

        edit(progress);
    }

    private void UpdateAddonProgress(AddonRun run, string argument)
    {
        string[] parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string value = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        if (value is "done" or "end" or "hide" or "off")
        {
            EndAddonProgress(run);
            return;
        }

        string[] fraction = value.TrimEnd('%').Split('/');
        double current = 0;
        double maximum = 0;
        bool isBusy = value is "" or "busy" or "indeterminate";
        bool isValid = isBusy || (fraction.Length <= 2 && TryParseAddonNumber(fraction[0], out current) && (fraction.Length == 1 || TryParseAddonNumber(fraction[1], out maximum)));
        if (!isValid)
        {
            ReportInvalidAddonArgument(run, "progress", argument);
            return;
        }

        OperationProgressViewModel? progress = EnsureAddonProgress(run);
        if (progress == null)
        {
            return;
        }

        if (parts.Length > 1)
        {
            progress.Text = TranslateAddonText(run, parts[1]);
        }

        if (isBusy)
        {
            progress.IsIndeterminate = true;
            return;
        }

        if (fraction.Length == 2)
        {
            progress.Maximum = maximum;
        }

        progress.Value = current;
        progress.IsIndeterminate = false;
    }

    private void SetAddonProgressMaximum(AddonRun run, string argument)
    {
        if (!TryParseAddonNumber(argument, out double maximum))
        {
            ReportInvalidAddonArgument(run, "progress-max", argument);
            return;
        }

        EditAddonProgress(run, p => p.Maximum = maximum);
    }

    private void SetAddonLoadingFromCommand(AddonRun run, string argument)
    {
        switch (argument.ToLowerInvariant())
        {
            case "" or "on" or "true" or "1" or "start":
                SetAddonLoading(run, true);
                break;
            case "off" or "false" or "0" or "stop" or "done":
                SetAddonLoading(run, false);
                break;
            default:
                ReportInvalidAddonArgument(run, "loading", argument);
                break;
        }
    }

    private void SetAddonLoading(AddonRun run, bool loading)
    {
        if ((loading && run.HasExited) || run.IsLoading == loading)
        {
            return;
        }

        run.IsLoading = loading;
        ViewModel.SetExternalLoading(loading);
    }

    private async Task PickFromAddonAsync(AddonRun run, string argument)
    {
        if (!ViewModel.IsPickerMode)
        {
            AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Get("AddonLogPickUnavailable"));
            return;
        }

        string path = ResolveAddonPath(argument);
        if (!await ViewModel.TryCompletePickerAsync(path))
        {
            AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Format("AddonLogPickInvalid", path));
        }
    }

    private async Task ReplyAddonClipboardAsync(AddonRun run)
    {
        string text = Clipboard == null ? "" : await Clipboard.TryGetTextAsync() ?? "";
        ReplyToRun(run, EscapeAddonReply(text.TrimEnd('\r', '\n')));
    }

    private async Task CopyFromAddonAsync(string text)
    {
        if (Clipboard == null)
        {
            return;
        }

        await Clipboard.SetTextAsync(text);
    }

    private List<string> ResolveAddonPaths(IEnumerable<string> parts)
    {
        return parts
            .Where(p => p.Length > 0)
            .Select(p => ResolveAddonPath(Addon.ExpandHome(p)))
            .ToList();
    }

    private void CopyAddonPathsToClipboard(AddonRun run, string argument, ClipboardMode mode)
    {
        List<string> paths = ResolveAddonPaths(SplitAddonArguments(argument))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToList();
        if (paths.Count == 0)
        {
            ReportInvalidAddonArgument(run, mode == ClipboardMode.Cut ? "cut-files" : "copy-files", argument);
            return;
        }

        ViewModel.WriteClipboardPaths(mode, paths);
    }

    private async Task TransferFromAddonAsync(AddonRun run, string argument, bool isCopy)
    {
        List<string> parts = SplitAddonArguments(argument);
        List<string> sources = ResolveAddonPaths(parts.Skip(1));
        string destination = parts[0].Length > 0 ? ResolveAddonPath(Addon.ExpandHome(parts[0])) : "";
        if (sources.Count == 0 || !Directory.Exists(destination))
        {
            ReportInvalidAddonArgument(run, isCopy ? "copy-to" : "move-to", argument);
            ReplyToRun(run, "no");
            return;
        }

        bool completed = await ViewModel.TransferItemsToAsync(sources, destination, isCopy);
        ReplyToRun(run, completed ? "yes" : "no");
    }

    private async Task TrashFromAddonAsync(AddonRun run, string argument)
    {
        int count = await ViewModel.TrashPathsAsync(ResolveAddonPaths(SplitAddonArguments(argument)));
        if (count == 0)
        {
            ReportInvalidAddonArgument(run, "trash", argument);
            return;
        }

        ViewModel.ShowStatusMessage(LocalizationService.Format("AddonTrashed", run.AddonName, count));
    }

    private void RevealFromAddon(AddonRun run, string argument)
    {
        List<string> paths = ResolveAddonPaths(SplitAddonArguments(argument))
            .Select(p => Path.TrimEndingDirectorySeparator(p))
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToList();
        string? folder = paths.Count > 0 ? Path.GetDirectoryName(paths[0]) : null;
        if (folder == null)
        {
            ReportInvalidAddonArgument(run, "reveal", argument);
            return;
        }

        List<string> targets = paths.Where(p => Path.GetDirectoryName(p) == folder).ToList();
        PaneViewModel pane = ViewModel.ActivePane;
        SelectWhenLoaded(pane, targets);
        if (!IsSameAddonFolder(pane.CurrentPath, folder))
        {
            ViewModel.NavigateTo(folder);
            return;
        }

        if (targets.All(t => pane.Items.Any(i => i.FullPath == t)))
        {
            OnPaneContentLoaded(pane);
            return;
        }

        ViewModel.Refresh();
    }

    private static bool IsSameAddonFolder(string a, string b)
    {
        return a.Length > 0 && Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
    }

    private async Task NotifyFromAddonAsync(AddonRun run, string text)
    {
        if (text.Length == 0)
        {
            ReportInvalidAddonArgument(run, "notify", text);
            return;
        }

        if (await NotificationService.ShowAsync(run.AddonName, text, run.Addon.IconPath))
        {
            return;
        }

        ViewModel.ShowStatusMessage(text);
        AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Get("AddonLogNotifyFailed"));
    }

    private static void LogFromAddon(AddonRun run, string argument)
    {
        int space = argument.IndexOf(' ');
        string first = (space < 0 ? argument : argument[..space]).ToLowerInvariant();
        AddonLogLevel? level = first switch
        {
            "info" or "debug" => AddonLogLevel.Info,
            "warning" or "warn" => AddonLogLevel.Warning,
            "error" => AddonLogLevel.Error,
            _ => null,
        };
        string message = level == null ? argument : space < 0 ? "" : argument[(space + 1)..].Trim();
        if (message.Length == 0)
        {
            ReportInvalidAddonArgument(run, "log", argument);
            return;
        }

        AddonLogService.Add(run.Addon, run.Source, level ?? AddonLogLevel.Info, message);
    }

    private static string EscapeAddonReply(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\n", "\\n");
    }

    private static bool TryParseAddonNumber(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    private static void ReportInvalidAddonArgument(AddonRun run, string command, string argument)
    {
        AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Format("AddonLogInvalidArgument", $"{AddonCommand.Prefix}{command}", argument));
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
        AddonHooks.RequestServiceRestart();
    }
}
