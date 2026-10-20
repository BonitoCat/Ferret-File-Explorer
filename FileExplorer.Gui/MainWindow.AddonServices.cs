using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Dialogs;
using FileExplorer.Gui.Models;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui;

public sealed class AddonPendingForm
{
    public string Title { get; }
    public List<AddonField> Fields { get; } = new();

    public AddonPendingForm(string title)
    {
        Title = title;
    }
}

public partial class MainWindow
{
    private const int ServiceInputCapacity = 500;
    private const string ServiceSource = "service";

    private static readonly TimeSpan ServiceCloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SelectionEventDelay = TimeSpan.FromMilliseconds(150);

    private sealed class DynamicAddonPlace
    {
        public required string AddonId { get; init; }
        public required string Section { get; init; }
        public required string Name { get; init; }
        public required string Path { get; init; }
        public string? Icon { get; init; }
        public AddonRun? Owner { get; init; }
    }

    private readonly Dictionary<string, AddonRun> _addonServices = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exitedAddonServices = new(StringComparer.Ordinal);
    private readonly List<DynamicAddonPlace> _dynamicAddonPlaces = new();
    private readonly Dictionary<AddonStatusItemViewModel, AddonRun?> _addonStatusOwners = new();
    private readonly DispatcherTimer _selectionEventTimer = new() { Interval = SelectionEventDelay };
    private string _lastNavigateEvent = "";
    private string _lastSelectionEvent = "";

    private void InitializeAddonServices()
    {
        _selectionEventTimer.Tick += OnSelectionEventTimerTick;
        ViewModel.PropertyChanged += OnAddonViewModelPropertyChanged;
        AddonSettingsStore.Changed += OnAddonSettingsChanged;
        AddonHooks.ServiceRestartRequested += RestartAddonServices;
    }

    private void ShutdownAddonServices()
    {
        _selectionEventTimer.Stop();
        ViewModel.PropertyChanged -= OnAddonViewModelPropertyChanged;
        AddonSettingsStore.Changed -= OnAddonSettingsChanged;
        AddonHooks.ServiceRestartRequested -= RestartAddonServices;
    }

    private void SyncAddonServices(bool restart)
    {
        if (!_isAddonHookHost || !_addonHooksReady)
        {
            return;
        }

        Dictionary<string, Addon> wanted = AddonService.Instance.Addons
            .Where(a => a.IsActive && a.ServicePath != null && a.SupportsMode(CurrentAddonMode) && !AddonHooks.IsInstalling(a.Id))
            .ToDictionary(GetAddonLoadKey, StringComparer.Ordinal);
        _exitedAddonServices.RemoveWhere(key => restart || !wanted.ContainsKey(key));

        foreach (string key in _addonServices.Keys.ToList())
        {
            if (!restart && wanted.ContainsKey(key))
            {
                continue;
            }

            AddonRun run = _addonServices[key];
            _addonServices.Remove(key);
            _ = StopAddonServiceAsync(run, ServiceCloseTimeout);
        }

        foreach ((string key, Addon addon) in wanted)
        {
            if (_addonServices.ContainsKey(key) || _exitedAddonServices.Contains(key))
            {
                continue;
            }

            StartAddonService(key, addon);
        }
    }

    private void RestartAddonServices()
    {
        SyncAddonServices(true);
    }

    private List<AddonRun> TakeAddonServices()
    {
        List<AddonRun> services = _addonServices.Values.ToList();
        _addonServices.Clear();
        return services;
    }

    private void StartAddonService(string key, Addon addon)
    {
        string language = LocalizationService.Instance.LanguageCode;
        AddonLaunch launch = AddonRunner.PrepareService(addon, CreateAddonContext(), language);
        AddonRun? run = StartAddonRun(addon, ServiceSource, addon.GetName(language), launch, true);
        if (run?.Process == null)
        {
            return;
        }

        _addonServices[key] = run;
        Channel<string> input = Channel.CreateBounded<string>(new BoundedChannelOptions(ServiceInputCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        run.Input = input;
        StreamWriter writer = run.Process.StandardInput;
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (string line in input.Reader.ReadAllAsync())
                {
                    await writer.WriteLineAsync(line);
                    await writer.FlushAsync();
                }

                writer.Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
            {
            }
        });

        SendAddonEvent(run, $"event:navigate {ViewModel.CurrentPath}");
        SendAddonEvent(run, $"event:selection {GetSelectionEventText()}");
    }

    private async Task StopAddonServiceAsync(AddonRun run, TimeSpan timeout)
    {
        run.WasStopped = true;
        SendAddonEvent(run, "event:close");
        run.Input?.Writer.TryComplete();

        Task finished = await Task.WhenAny(run.Exited.Task, Task.Delay(timeout));
        if (finished == run.Exited.Task)
        {
            return;
        }

        await StopAddonRunAsync(run, PreCloseStopGrace);
    }

    private void OnAddonServiceExited(AddonRun run)
    {
        foreach (KeyValuePair<AddonStatusItemViewModel, AddonRun?> entry in _addonStatusOwners.Where(e => e.Value == run).ToList())
        {
            _addonStatusOwners.Remove(entry.Key);
            ViewModel.AddonStatusItems.Remove(entry.Key);
        }

        if (_dynamicAddonPlaces.RemoveAll(p => p.Owner == run) > 0)
        {
            RefreshAddonPlaces();
        }

        if (!run.IsService)
        {
            return;
        }

        string? key = _addonServices.FirstOrDefault(e => e.Value == run).Key;
        if (key != null)
        {
            _addonServices.Remove(key);
            _exitedAddonServices.Add(key);
        }

        foreach (AddonStatusItemViewModel item in ViewModel.AddonStatusItems.Where(i => i.AddonId == run.Addon.Id))
        {
            item.IsClickable = IsAddonStatusClickable(run.Addon.Id, item.Id);
        }
    }

    private static void SendAddonEvent(AddonRun run, string line)
    {
        if (run.HasExited || run.Input == null)
        {
            return;
        }

        run.Input.Writer.TryWrite(line);
    }

    private void BroadcastAddonEvent(string line)
    {
        foreach (AddonRun run in _addonServices.Values)
        {
            SendAddonEvent(run, line);
        }
    }

    private void OnAddonViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainWindowViewModel.CurrentPath) or nameof(MainWindowViewModel.ActivePane)))
        {
            return;
        }

        string line = $"event:navigate {ViewModel.CurrentPath}";
        if (line == _lastNavigateEvent)
        {
            return;
        }

        _lastNavigateEvent = line;
        BroadcastAddonEvent(line);
        QueueAddonSelectionEvent();
    }

    private void QueueAddonSelectionEvent()
    {
        if (_addonServices.Count == 0)
        {
            return;
        }

        _selectionEventTimer.Stop();
        _selectionEventTimer.Start();
    }

    private void OnSelectionEventTimerTick(object? sender, EventArgs e)
    {
        _selectionEventTimer.Stop();
        string line = $"event:selection {GetSelectionEventText()}";
        if (line == _lastSelectionEvent)
        {
            return;
        }

        _lastSelectionEvent = line;
        BroadcastAddonEvent(line);
    }

    private string GetSelectionEventText()
    {
        return string.Join('\t', GetSelectedItems().Select(i => i.FullPath));
    }

    private void OnAddonSettingsChanged(string addonId, IReadOnlyDictionary<string, string> values)
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (AddonRun run in _addonServices.Values.Where(r => r.Addon.Id == addonId))
            {
                foreach (KeyValuePair<string, string> entry in values)
                {
                    SendAddonEvent(run, $"event:setting {entry.Key} {SanitizeAddonReply(entry.Value)}");
                }
            }
        });
    }

    private void ReplyToRun(AddonRun run, string? answer)
    {
        if (!run.IsService)
        {
            ReplyToAddon(run.Process, answer);
            return;
        }

        SendAddonEvent(run, answer == null ? "event:cancel" : $"event:reply {SanitizeAddonReply(answer)}");
    }

    private static string SanitizeAddonReply(string text)
    {
        return text.Replace("\r", " ").Replace('\n', ' ');
    }

    private async Task ShowAddonChoiceAsync(AddonRun run, string argument)
    {
        List<string> parts = SplitAddonArguments(argument);
        if (parts.Count < 2)
        {
            ReportInvalidAddonArgument(run, "choose", argument);
            ReplyToRun(run, null);
            return;
        }

        AddonField field = new("choice", "", "", AddonFieldType.Choice, parts[1], parts.Skip(1).ToList());
        AddonFormDialog dialog = new(run.AddonName, parts[0], [field]);
        Dictionary<string, string>? values = await dialog.ShowDialog<Dictionary<string, string>?>(this);
        ReplyToRun(run, values?.GetValueOrDefault(field.Id));
    }

    private void AddAddonFormField(AddonRun run, string argument)
    {
        List<string> parts = SplitAddonArguments(argument);
        if (parts.Count < 2 || parts[0].Length == 0 || !AddonField.TryParseType(parts[1], out AddonFieldType type))
        {
            ReportInvalidAddonArgument(run, "field", argument);
            return;
        }

        string label = parts.Count > 2 ? parts[2] : parts[0];
        string value = parts.Count > 3 ? parts[3] : "";
        List<string> choices = new();
        if (type == AddonFieldType.Choice)
        {
            choices = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => c.TrimStart('*').Trim())
                .Where(c => c.Length > 0)
                .ToList();
            if (choices.Count == 0)
            {
                ReportInvalidAddonArgument(run, "field", argument);
                return;
            }

            value = value.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault(c => c.StartsWith('*'))?.TrimStart('*').Trim() ?? choices[0];
        }

        run.PendingForm ??= new("");
        run.PendingForm.Fields.Add(new(parts[0], label, "", type, value, choices));
    }

    private async Task ShowAddonFormAsync(AddonRun run)
    {
        AddonPendingForm? form = run.PendingForm;
        run.PendingForm = null;
        if (form == null || form.Fields.Count == 0)
        {
            ReportInvalidAddonArgument(run, "form-show", "");
            ReplyToRun(run, null);
            return;
        }

        AddonFormDialog dialog = new(run.AddonName, form.Title, form.Fields);
        Dictionary<string, string>? values = await dialog.ShowDialog<Dictionary<string, string>?>(this);
        if (values == null)
        {
            ReplyToRun(run, null);
            return;
        }

        ReplyToRun(run, string.Join('\t', form.Fields.Select(f => values.GetValueOrDefault(f.Id, "").Replace('\t', ' '))));
    }

    private void SetAddonStatusItem(AddonRun run, string argument)
    {
        int space = argument.IndexOf(' ');
        string id = space < 0 ? argument : argument[..space];
        string text = space < 0 ? "" : argument[(space + 1)..].Trim();
        if (id.Length == 0)
        {
            ReportInvalidAddonArgument(run, "statusbar", argument);
            return;
        }

        AddonStatusItemViewModel? item = FindAddonStatusItem(run.Addon.Id, id);
        if (text.Length == 0)
        {
            if (item != null)
            {
                ViewModel.AddonStatusItems.Remove(item);
                _addonStatusOwners.Remove(item);
            }

            return;
        }

        if (item == null)
        {
            item = new(run.Addon.Id, id, IconService.LoadFile(run.Addon.IconPath));
            ViewModel.AddonStatusItems.Add(item);
        }

        _addonStatusOwners[item] = run.IsService ? run : null;
        item.Text = text;
        item.IsClickable = IsAddonStatusClickable(run.Addon.Id, id);
    }

    private void SetAddonStatusTooltip(AddonRun run, string argument)
    {
        int space = argument.IndexOf(' ');
        string id = space < 0 ? argument : argument[..space];
        AddonStatusItemViewModel? item = FindAddonStatusItem(run.Addon.Id, id);
        if (item == null)
        {
            ReportInvalidAddonArgument(run, "statusbar-tooltip", argument);
            return;
        }

        item.Tooltip = space < 0 ? "" : argument[(space + 1)..].Trim().Replace("\\n", "\n");
    }

    private AddonStatusItemViewModel? FindAddonStatusItem(string addonId, string id)
    {
        return ViewModel.AddonStatusItems.FirstOrDefault(i => i.AddonId == addonId && i.Id == id);
    }

    private bool IsAddonStatusClickable(string addonId, string id)
    {
        if (_addonServices.Values.Any(r => r.Addon.Id == addonId))
        {
            return true;
        }

        return AddonService.Instance.Find(addonId)?.Actions.Any(a => a.Id == id && a.SupportsMode(CurrentAddonMode)) == true;
    }

    private void OnAddonStatusItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AddonStatusItemViewModel item })
        {
            return;
        }

        AddonRun? service = _addonServices.Values.FirstOrDefault(r => r.Addon.Id == item.AddonId);
        if (service != null)
        {
            SendAddonEvent(service, $"event:click {item.Id}");
            return;
        }

        AddonAction? action = AddonService.Instance.Find(item.AddonId)?.Actions.FirstOrDefault(a => a.Id == item.Id && a.SupportsMode(CurrentAddonMode));
        if (action == null)
        {
            return;
        }

        RunAddonAction(action, CreateAddonContext());
    }

    private void SetAddonPlace(AddonRun run, string argument)
    {
        List<string> parts = SplitAddonArguments(argument);
        if (parts.Count < 3 || parts.Take(3).Any(p => p.Length == 0))
        {
            ReportInvalidAddonArgument(run, "place", argument);
            return;
        }

        string path = Addon.ExpandHome(parts[2]);
        if (!path.Contains("://", StringComparison.Ordinal) && !Path.IsPathRooted(path))
        {
            path = ResolveAddonPath(path);
        }

        _dynamicAddonPlaces.RemoveAll(p => p.AddonId == run.Addon.Id && p.Section == parts[0] && p.Name == parts[1]);
        _dynamicAddonPlaces.Add(new()
        {
            AddonId = run.Addon.Id,
            Section = parts[0],
            Name = parts[1],
            Path = path,
            Icon = parts.Count > 3 && parts[3].Length > 0 ? parts[3] : null,
            Owner = run.IsService ? run : null,
        });
        RefreshAddonPlaces();
    }

    private void RemoveAddonPlace(AddonRun run, string argument)
    {
        List<string> parts = SplitAddonArguments(argument);
        if (parts.Count == 0 || parts[0].Length == 0)
        {
            ReportInvalidAddonArgument(run, "place-remove", argument);
            return;
        }

        string? name = parts.Count > 1 && parts[1].Length > 0 ? parts[1] : null;
        if (_dynamicAddonPlaces.RemoveAll(p => p.AddonId == run.Addon.Id && p.Section == parts[0] && (name == null || p.Name == name)) > 0)
        {
            RefreshAddonPlaces();
        }
    }

    private void RefreshAddonPlaces()
    {
        string language = LocalizationService.Instance.LanguageCode;
        List<AddonPlaceEntry> entries = new();
        foreach (Addon addon in AddonService.Instance.Addons.Where(a => a.IsActive && a.SupportsMode(CurrentAddonMode)))
        {
            IEnumerable<AddonPlace> places = addon.Places.Concat(_dynamicAddonPlaces
                .Where(p => p.AddonId == addon.Id)
                .Select(p => new AddonPlace(p.Section, p.Name, p.Path, p.Icon)));
            foreach (AddonPlace place in places)
            {
                string section = addon.Translate(place.Section, language);
                entries.Add(new($"{addon.Id}\n{section}", section.ToUpper(CultureInfo.CurrentCulture), addon.Translate(place.Name, language), place.Path, ResolveAddonPlaceIcon(addon, place.Icon)));
            }
        }

        ViewModel.SetAddonPlaces(entries);
    }

    private static IImage? ResolveAddonPlaceIcon(Addon addon, string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return IconService.Get("folder");
        }

        string? file = addon.ResolvePath(icon);
        if (file != null && File.Exists(file))
        {
            return IconService.LoadFile(file);
        }

        return IconService.Get(icon.Trim()) ?? IconService.Get("folder");
    }

    private void RemoveInactiveAddonContent()
    {
        HashSet<string> active = AddonService.Instance.Addons
            .Where(a => a.IsActive && a.SupportsMode(CurrentAddonMode))
            .Select(a => a.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (AddonStatusItemViewModel item in ViewModel.AddonStatusItems.Where(i => !active.Contains(i.AddonId)).ToList())
        {
            ViewModel.AddonStatusItems.Remove(item);
            _addonStatusOwners.Remove(item);
        }

        _dynamicAddonPlaces.RemoveAll(p => !active.Contains(p.AddonId));
    }

    private void SaveAddonSetting(AddonRun run, string argument)
    {
        int space = argument.IndexOf(' ');
        string id = space < 0 ? argument : argument[..space];
        string value = space < 0 ? "" : argument[(space + 1)..].Trim();
        if (!AddonSettingsStore.TrySetValue(run.Addon, id, value))
        {
            ReportInvalidAddonArgument(run, "setting", argument);
        }
    }

    private static List<string> SplitAddonArguments(string argument)
    {
        return argument
            .Split('|')
            .Select(p => p.Trim())
            .ToList();
    }
}
