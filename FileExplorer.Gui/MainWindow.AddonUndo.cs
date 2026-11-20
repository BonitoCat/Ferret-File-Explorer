using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Services;
using LoggerLib;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private void RecordAddonCreated(AddonRun run, string argument)
    {
        List<string> paths = ResolveAddonPaths(SplitAddonArguments(argument));
        if (paths.Count == 0)
        {
            ReportInvalidAddonArgument(run, "undo-created", argument);
            return;
        }

        ViewModel.UndoHistory.Record(FileUndoKind.Create, paths.Select(path => (path, "")));
    }

    private void PushAddonUndo(AddonRun run, string data)
    {
        Addon addon = run.Addon;
        string title = run.Title;
        if (run.IsService)
        {
            ViewModel.UndoHistory.RecordCustom(title, isUndo => Task.FromResult(SendAddonUndoEvent(addon.Id, isUndo, data)));
            return;
        }

        AddonAction? action = addon.Actions.FirstOrDefault(a => a.Id == run.Source);
        if (action == null || run.Context == null)
        {
            ReportInvalidAddonArgument(run, "undo-push", data);
            return;
        }

        AddonContext context = run.Context;
        string actionId = action.Id;
        ViewModel.UndoHistory.RecordCustom(title, isUndo => RunAddonUndoAsync(addon.Id, actionId, context, isUndo, data));
    }

    private bool SendAddonUndoEvent(string addonId, bool isUndo, string data)
    {
        AddonRun? service = _addonServices.Values.FirstOrDefault(r => r.Addon.Id == addonId && !r.HasExited);
        if (service == null)
        {
            return false;
        }

        SendAddonEvent(service, $"event:{(isUndo ? "undo" : "redo")} {SanitizeAddonReply(data)}");
        return true;
    }

    private async Task<bool> RunAddonUndoAsync(string addonId, string actionId, AddonContext context, bool isUndo, string data)
    {
        Addon? addon = AddonService.Instance.Addons.FirstOrDefault(a => a.Id == addonId && a.IsActive);
        AddonAction? action = addon?.Actions.FirstOrDefault(a => a.Id == actionId);
        if (addon == null || action == null)
        {
            return false;
        }

        string language = LocalizationService.Instance.LanguageCode;
        AddonLaunch launch = AddonRunner.Prepare(action, context, language);
        Dictionary<string, string> environment = new(launch.Environment)
        {
            ["FE_UNDO"] = isUndo ? "undo" : "redo",
            ["FE_UNDO_DATA"] = data,
        };

        _ = Logger.LogI($"Running addon {(isUndo ? "undo" : "redo")} for {action.Key}");
        AddonRun? run = StartAddonRun(addon, action.Id, action.GetTitle(language), launch with { Environment = environment });
        if (run == null)
        {
            return false;
        }

        run.Context = context;
        return await run.Exited.Task == 0;
    }
}
