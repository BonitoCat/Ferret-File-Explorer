using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private sealed record AddonDecorationEntry(string AddonId, AddonRun? Owner, FileItemDecoration Decoration, IReadOnlySet<string> Tags);

    private const byte DefaultDecorationBackgroundAlpha = 0x40;

    private readonly Dictionary<string, List<AddonDecorationEntry>> _addonDecorations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirtyDecorationPaths = new(StringComparer.Ordinal);
    private bool _decorationsDirty;

    private void InitializeAddonDecorations()
    {
        foreach (PaneViewModel pane in ViewModel.Panes)
        {
            pane.Items.CollectionChanged += OnPaneItemsChanged;
            pane.ContentChanged += SendAddonRefreshEvent;
        }
    }

    private void OnPaneItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_addonDecorations.Count == 0)
        {
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset && sender is IEnumerable<FileItemViewModel> all)
        {
            foreach (FileItemViewModel item in all)
            {
                ApplyAddonDecoration(item);
            }

            return;
        }

        foreach (FileItemViewModel item in e.NewItems?.OfType<FileItemViewModel>() ?? [])
        {
            ApplyAddonDecoration(item);
        }
    }

    private void SendAddonRefreshEvent(PaneViewModel pane)
    {
        if (pane.IsLoading || pane.CurrentPath.Length == 0 || _addonServices.Count == 0)
        {
            return;
        }

        BroadcastAddonEvent($"event:refresh {pane.CurrentPath}");
    }

    private void SetAddonDecoration(AddonRun run, string argument)
    {
        List<string> parts = SplitAddonArguments(argument);
        if (parts[0].Length == 0)
        {
            ReportInvalidAddonArgument(run, "decorate", argument);
            return;
        }

        if (!TryParseAddonDecoration(run, parts.Skip(1), out FileItemDecoration? decoration, out HashSet<string> tags))
        {
            ReportInvalidAddonArgument(run, "decorate", argument);
            return;
        }

        string path = NormalizeDecorationPath(ResolveAddonPath(Addon.ExpandHome(parts[0])));
        if (!_addonDecorations.TryGetValue(path, out List<AddonDecorationEntry>? entries))
        {
            entries = new();
            _addonDecorations[path] = entries;
        }

        entries.RemoveAll(e => e.AddonId == run.Addon.Id);
        entries.Add(new(run.Addon.Id, run.IsService ? run : null, decoration!, tags));
        MarkDecorationDirty(path);
    }

    private void RemoveAddonDecoration(AddonRun run, string argument)
    {
        List<string> paths = SplitAddonArguments(argument).Where(p => p.Length > 0).ToList();
        if (paths.Count == 0)
        {
            RemoveAddonDecorations(e => e.AddonId == run.Addon.Id);
            return;
        }

        foreach (string raw in paths)
        {
            string path = NormalizeDecorationPath(ResolveAddonPath(Addon.ExpandHome(raw)));
            if (_addonDecorations.TryGetValue(path, out List<AddonDecorationEntry>? entries) && entries.RemoveAll(e => e.AddonId == run.Addon.Id) > 0)
            {
                RemoveEmptyDecorationPath(path, entries);
                MarkDecorationDirty(path);
            }
        }
    }

    private void RemoveAddonDecorations(Func<AddonDecorationEntry, bool> predicate)
    {
        foreach (KeyValuePair<string, List<AddonDecorationEntry>> pair in _addonDecorations.ToList())
        {
            if (pair.Value.RemoveAll(e => predicate(e)) == 0)
            {
                continue;
            }

            RemoveEmptyDecorationPath(pair.Key, pair.Value);
            MarkDecorationDirty(pair.Key);
        }
    }

    private void RemoveEmptyDecorationPath(string path, List<AddonDecorationEntry> entries)
    {
        if (entries.Count == 0)
        {
            _addonDecorations.Remove(path);
        }
    }

    private IReadOnlySet<string> GetAddonTags(string addonId, string path)
    {
        if (!_addonDecorations.TryGetValue(NormalizeDecorationPath(path), out List<AddonDecorationEntry>? entries))
        {
            return new HashSet<string>();
        }

        return entries.FirstOrDefault(e => e.AddonId == addonId)?.Tags ?? new HashSet<string>();
    }

    private void MarkDecorationDirty(string path)
    {
        _dirtyDecorationPaths.Add(path);
        if (_decorationsDirty)
        {
            return;
        }

        _decorationsDirty = true;
        Dispatcher.UIThread.Post(FlushAddonDecorations, DispatcherPriority.Background);
    }

    private void FlushAddonDecorations()
    {
        _decorationsDirty = false;
        foreach (PaneViewModel pane in ViewModel.Panes)
        {
            foreach (FileItemViewModel item in pane.Items)
            {
                if (_dirtyDecorationPaths.Contains(NormalizeDecorationPath(item.FullPath)))
                {
                    ApplyAddonDecoration(item);
                }
            }
        }

        _dirtyDecorationPaths.Clear();
    }

    private void ApplyAddonDecoration(FileItemViewModel item)
    {
        if (!_addonDecorations.TryGetValue(NormalizeDecorationPath(item.FullPath), out List<AddonDecorationEntry>? entries) || entries.Count == 0)
        {
            item.Decoration = null;
            return;
        }

        item.Decoration = entries.Count == 1 ? entries[0].Decoration : MergeDecorations(entries.Select(e => e.Decoration).ToList());
    }

    private static FileItemDecoration MergeDecorations(List<FileItemDecoration> decorations)
    {
        FileItemDecoration? badge = decorations.LastOrDefault(d => d.HasBadge);
        List<string> tooltips = decorations.Select(d => d.Tooltip).OfType<string>().ToList();
        return new()
        {
            Foreground = decorations.LastOrDefault(d => d.Foreground != null)?.Foreground,
            Background = decorations.LastOrDefault(d => d.Background != null)?.Background,
            IconBackground = decorations.LastOrDefault(d => d.IconBackground != null)?.IconBackground,
            NameBackground = decorations.LastOrDefault(d => d.NameBackground != null)?.NameBackground,
            Icon = decorations.LastOrDefault(d => d.Icon != null)?.Icon,
            BadgeIcon = badge?.BadgeIcon,
            BadgeText = badge?.BadgeText,
            BadgeBackground = badge?.BadgeBackground,
            BadgeCorner = badge?.BadgeCorner ?? DecorationCorner.TopRight,
            Tooltip = tooltips.Count == 0 ? null : string.Join('\n', tooltips),
        };
    }

    private static string NormalizeDecorationPath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static bool TryParseAddonDecoration(AddonRun run, IEnumerable<string> parts, out FileItemDecoration? decoration, out HashSet<string> tags)
    {
        decoration = null;
        tags = new(StringComparer.OrdinalIgnoreCase);
        FileItemDecoration result = new();
        foreach (string part in parts.Where(p => p.Length > 0))
        {
            int equals = part.IndexOf('=');
            string key = (equals < 0 ? part : part[..equals]).Trim().ToLowerInvariant();
            string value = equals < 0 ? "" : part[(equals + 1)..].Trim();
            switch (key)
            {
                case "color" or "foreground":
                    if (!TryParseDecorationBrush(value, null, out IBrush? foreground))
                    {
                        return false;
                    }

                    result = result with { Foreground = foreground };
                    break;
                case "background":
                    if (!TryParseDecorationBrush(value, DefaultDecorationBackgroundAlpha, out IBrush? background))
                    {
                        return false;
                    }

                    result = result with { Background = background };
                    break;
                case "icon-background":
                    if (!TryParseDecorationBrush(value, DefaultDecorationBackgroundAlpha, out IBrush? iconBackground))
                    {
                        return false;
                    }

                    result = result with { IconBackground = iconBackground };
                    break;
                case "text-background" or "name-background":
                    if (!TryParseDecorationBrush(value, DefaultDecorationBackgroundAlpha, out IBrush? nameBackground))
                    {
                        return false;
                    }

                    result = result with { NameBackground = nameBackground };
                    break;
                case "badge-color":
                    if (!TryParseDecorationBrush(value, null, out IBrush? badgeBackground))
                    {
                        return false;
                    }

                    result = result with { BadgeBackground = badgeBackground };
                    break;
                case "icon":
                    IImage? icon = ResolveAddonDecorationIcon(run.Addon, value);
                    if (icon == null)
                    {
                        return false;
                    }

                    result = result with { Icon = icon };
                    break;
                case "badge":
                    IImage? badgeIcon = ResolveAddonDecorationIcon(run.Addon, value);
                    if (badgeIcon == null)
                    {
                        return false;
                    }

                    result = result with { BadgeIcon = badgeIcon };
                    break;
                case "badge-text":
                    string badgeText = TranslateAddonText(run, value).Trim();
                    result = result with { BadgeText = badgeText.Length == 0 ? null : badgeText };
                    break;
                case "badge-corner":
                    if (!TryParseDecorationCorner(value, out DecorationCorner corner))
                    {
                        return false;
                    }

                    result = result with { BadgeCorner = corner };
                    break;
                case "tooltip":
                    string tooltip = TranslateAddonText(run, value).Replace("\\n", "\n").Trim();
                    result = result with { Tooltip = tooltip.Length == 0 ? null : tooltip };
                    break;
                case "tag" or "tags":
                    tags.UnionWith(value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0));
                    break;
                default:
                    return false;
            }
        }

        decoration = result;
        return true;
    }

    private static bool TryParseDecorationBrush(string value, byte? defaultAlpha, out IBrush? brush)
    {
        brush = null;
        if (!Color.TryParse(value, out Color color))
        {
            return false;
        }

        bool hasExplicitAlpha = value.StartsWith('#') && value.Length is 5 or 9;
        if (defaultAlpha.HasValue && !hasExplicitAlpha)
        {
            color = Color.FromArgb(defaultAlpha.Value, color.R, color.G, color.B);
        }

        brush = new SolidColorBrush(color);
        return true;
    }

    private static bool TryParseDecorationCorner(string value, out DecorationCorner corner)
    {
        DecorationCorner? parsed = value.ToLowerInvariant() switch
        {
            "top-left" => DecorationCorner.TopLeft,
            "top-right" => DecorationCorner.TopRight,
            "bottom-left" => DecorationCorner.BottomLeft,
            "bottom-right" => DecorationCorner.BottomRight,
            _ => null,
        };

        corner = parsed ?? DecorationCorner.TopRight;
        return parsed.HasValue;
    }

    private static IImage? ResolveAddonDecorationIcon(Addon addon, string icon)
    {
        if (icon.Length == 0)
        {
            return null;
        }

        string? file = addon.ResolvePath(icon);
        if (file != null && File.Exists(file))
        {
            return IconService.LoadFile(file);
        }

        return IconService.Get(icon);
    }
}
