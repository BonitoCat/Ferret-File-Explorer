using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileExplorer.Core.Addons;
using LoggerLib;

namespace FileExplorer.Gui.Services;

public enum AddonLogLevel
{
    Warning,
    Error,
}

public sealed class AddonLogEntry
{
    public DateTime Time { get; }
    public string AddonId { get; }
    public string AddonName { get; }
    public string Source { get; }
    public AddonLogLevel Level { get; }
    public string Message { get; }
    public string Detail { get; }

    public bool HasDetail => Detail.Length > 0;
    public bool IsError => Level == AddonLogLevel.Error;
    public double IconOpacity => IsError ? 1 : 0.55;
    public string Header => Source.Length > 0 ? $"{Time:HH:mm:ss}  ·  {AddonName}  ·  {Source}" : $"{Time:HH:mm:ss}  ·  {AddonName}";

    public AddonLogEntry(string addonId, string addonName, string source, AddonLogLevel level, string message, string detail)
    {
        Time = DateTime.Now;
        AddonId = addonId;
        AddonName = addonName;
        Source = source;
        Level = level;
        Message = message;
        Detail = detail.Trim();
    }

    public override string ToString()
    {
        StringBuilder builder = new();
        builder.Append($"[{Time:yyyy-MM-dd HH:mm:ss}] {Level} {AddonId}");
        if (Source.Length > 0)
        {
            builder.Append($" ({Source})");
        }

        builder.Append($": {Message}");
        if (HasDetail)
        {
            builder.AppendLine().Append(Detail);
        }

        return builder.ToString();
    }
}

public static class AddonLogService
{
    private const int MaxEntries = 200;

    private static readonly object Lock = new();
    private static readonly List<AddonLogEntry> LogEntries = new();
    private static readonly HashSet<string> ReportedLoadErrors = new();
    private static int _unseenCount;

    public static event Action? Changed;

    public static IReadOnlyList<AddonLogEntry> Entries
    {
        get
        {
            lock (Lock)
            {
                return LogEntries.AsEnumerable().Reverse().ToList();
            }
        }
    }

    public static int UnseenCount
    {
        get
        {
            lock (Lock)
            {
                return _unseenCount;
            }
        }
    }

    public static void Add(Addon addon, string source, AddonLogLevel level, string message, string detail = "")
    {
        Add(addon.Id, addon.GetName(LocalizationService.Instance.LanguageCode), source, level, message, detail);
    }

    public static void Add(string addonId, string addonName, string source, AddonLogLevel level, string message, string detail = "")
    {
        AddonLogEntry entry = new(addonId, addonName, source, level, message, detail);
        if (level == AddonLogLevel.Error)
        {
            Logger.LogW($"Addon {addonId} ({source}): {message} {entry.Detail}");
        }

        lock (Lock)
        {
            LogEntries.Add(entry);
            if (LogEntries.Count > MaxEntries)
            {
                LogEntries.RemoveAt(0);
            }

            _unseenCount = Math.Min(_unseenCount + 1, MaxEntries);
        }

        Changed?.Invoke();
    }

    public static void ReportLoadErrors(IEnumerable<Addon> addons)
    {
        foreach (Addon addon in addons.Where(a => a.HasError))
        {
            string key = $"{addon.RootPath}|{addon.ErrorKey}|{addon.ErrorDetail}";
            lock (Lock)
            {
                if (!ReportedLoadErrors.Add(key))
                {
                    continue;
                }
            }

            Add(addon, LocalizationService.Get("AddonLogSourceLoad"), AddonLogLevel.Error, LocalizationService.Format(addon.ErrorKey!, addon.ErrorDetail));
        }
    }

    public static void MarkSeen()
    {
        lock (Lock)
        {
            _unseenCount = 0;
        }

        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Lock)
        {
            LogEntries.Clear();
            _unseenCount = 0;
        }

        Changed?.Invoke();
    }
}
