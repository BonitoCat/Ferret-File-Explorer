using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private sealed class AddonWatch : IDisposable
    {
        public string Path { get; }
        public FileSystemWatcher Watcher { get; }

        public AddonWatch(string path, FileSystemWatcher watcher)
        {
            Path = path;
            Watcher = watcher;
        }

        public void Dispose()
        {
            Watcher.EnableRaisingEvents = false;
            Watcher.Dispose();
        }
    }

    private const int MaxAddonWatches = 64;
    private static readonly TimeSpan AddonWatchDelay = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<AddonRun, List<AddonWatch>> _addonWatches = new();
    private readonly Dictionary<AddonRun, HashSet<string>> _pendingWatchEvents = new();

    private void AddAddonWatches(AddonRun run, string argument)
    {
        if (!run.IsService)
        {
            AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Get("AddonLogWatchServiceOnly"));
            return;
        }

        if (!_addonWatches.TryGetValue(run, out List<AddonWatch>? watches))
        {
            watches = new();
            _addonWatches[run] = watches;
        }

        foreach (string raw in SplitAddonArguments(argument).Where(p => p.Length > 0))
        {
            string path = NormalizeDecorationPath(ResolveAddonPath(Addon.ExpandHome(raw)));
            if (watches.Any(w => w.Path == path))
            {
                continue;
            }

            if (watches.Count >= MaxAddonWatches)
            {
                AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Format("AddonLogWatchLimit", MaxAddonWatches));
                return;
            }

            FileSystemWatcher? watcher = CreateAddonWatcher(run, path);
            if (watcher == null)
            {
                ReportInvalidAddonArgument(run, "watch", raw);
                continue;
            }

            watches.Add(new(path, watcher));
        }
    }

    private FileSystemWatcher? CreateAddonWatcher(AddonRun run, string path)
    {
        bool isDirectory = Directory.Exists(path);
        string? directory = isDirectory ? path : Path.GetDirectoryName(path);
        if (directory == null || (!isDirectory && !File.Exists(path)))
        {
            return null;
        }

        try
        {
            FileSystemWatcher watcher = new(directory)
            {
                Filter = isDirectory ? "*" : Path.GetFileName(path),
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
            };
            watcher.Changed += (_, e) => QueueAddonWatchEvent(run, e.FullPath);
            watcher.Created += (_, e) => QueueAddonWatchEvent(run, e.FullPath);
            watcher.Deleted += (_, e) => QueueAddonWatchEvent(run, e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                QueueAddonWatchEvent(run, e.OldFullPath);
                QueueAddonWatchEvent(run, e.FullPath);
            };
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private void QueueAddonWatchEvent(AddonRun run, string path)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (run.HasExited || !_addonWatches.ContainsKey(run))
            {
                return;
            }

            if (_pendingWatchEvents.TryGetValue(run, out HashSet<string>? pending))
            {
                pending.Add(path);
                return;
            }

            _pendingWatchEvents[run] = new(StringComparer.Ordinal) { path };
            DispatcherTimer.RunOnce(() => FlushAddonWatchEvents(run), AddonWatchDelay);
        });
    }

    private void FlushAddonWatchEvents(AddonRun run)
    {
        if (!_pendingWatchEvents.Remove(run, out HashSet<string>? paths))
        {
            return;
        }

        foreach (string path in paths)
        {
            SendAddonEvent(run, $"event:watch {path}");
        }
    }

    private void RemoveAddonWatches(AddonRun run, string argument)
    {
        List<string> paths = SplitAddonArguments(argument).Where(p => p.Length > 0).ToList();
        if (paths.Count == 0)
        {
            StopAddonWatches(run);
            return;
        }

        if (!_addonWatches.TryGetValue(run, out List<AddonWatch>? watches))
        {
            return;
        }

        foreach (string raw in paths)
        {
            string path = NormalizeDecorationPath(ResolveAddonPath(Addon.ExpandHome(raw)));
            foreach (AddonWatch watch in watches.Where(w => w.Path == path).ToList())
            {
                watch.Dispose();
                watches.Remove(watch);
            }
        }
    }

    private void StopAddonWatches(AddonRun run)
    {
        _pendingWatchEvents.Remove(run);
        if (!_addonWatches.Remove(run, out List<AddonWatch>? watches))
        {
            return;
        }

        foreach (AddonWatch watch in watches)
        {
            watch.Dispose();
        }
    }
}
