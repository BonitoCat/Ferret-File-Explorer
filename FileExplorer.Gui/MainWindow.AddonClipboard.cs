using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Services;
using LoggerLib;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private async Task PasteAsync()
    {
        string directory = ViewModel.CurrentPath;
        List<AddonAction> handlers = AddonService.Instance.GetActions(AddonLocation.Paste, CurrentAddonMode);
        if (handlers.Count > 0)
        {
            (AddonClipboardContent content, List<string> files) = await ReadClipboardStateAsync();
            foreach (AddonAction handler in handlers.Where(h => h.AcceptsPaste(content, files)))
            {
                if (!await RunAddonPasteHandlerAsync(handler, content))
                {
                    return;
                }
            }
        }

        ViewModel.Paste(directory);
    }

    private bool HasAddonPasteHandlers()
    {
        return AddonService.Instance.GetActions(AddonLocation.Paste, CurrentAddonMode).Count > 0;
    }

    private async Task<bool> RunAddonPasteHandlerAsync(AddonAction action, AddonClipboardContent content)
    {
        string language = LocalizationService.Instance.LanguageCode;
        AddonLaunch launch = AddonRunner.Prepare(action, CreateAddonContext(), language);
        Dictionary<string, string> environment = new(launch.Environment)
        {
            ["FE_CLIPBOARD"] = FormatClipboardContent(content),
        };

        _ = Logger.LogI($"Running addon paste handler {action.Key}");
        AddonRun? run = StartAddonRun(action.Addon, action.Id, action.GetTitle(language), launch with { Environment = environment });
        if (run == null)
        {
            return true;
        }

        run.PasteDecision = new();
        return await run.PasteDecision.Task;
    }

    private static void PassAddonPaste(AddonRun run)
    {
        if (run.PasteDecision == null)
        {
            AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Get("AddonLogPassUnavailable"));
            return;
        }

        run.PasteDecision.TrySetResult(true);
    }

    private async Task<AddonClipboardContent> GetClipboardContentAsync()
    {
        return (await ReadClipboardStateAsync()).Content;
    }

    private async Task<(AddonClipboardContent Content, List<string> Files)> ReadClipboardStateAsync()
    {
        AddonClipboardContent content = AddonClipboardContent.None;
        (_, List<string> files) = await ReadClipboardFilesAsync();
        if (files.Count > 0)
        {
            content |= AddonClipboardContent.Files;
        }

        if (Clipboard == null)
        {
            return (content, files);
        }

        IReadOnlyList<DataFormat> formats = await Clipboard.GetDataFormatsAsync();
        if (formats.Contains(DataFormat.Bitmap))
        {
            content |= AddonClipboardContent.Image;
        }

        if (formats.Contains(DataFormat.Text))
        {
            content |= AddonClipboardContent.Text;
        }

        return (content, files);
    }

    private static string FormatClipboardContent(AddonClipboardContent content)
    {
        List<string> names = new();
        if (content.HasFlag(AddonClipboardContent.Files))
        {
            names.Add("files");
        }

        if (content.HasFlag(AddonClipboardContent.Image))
        {
            names.Add("image");
        }

        if (content.HasFlag(AddonClipboardContent.Text))
        {
            names.Add("text");
        }

        return string.Join(' ', names);
    }

    private async Task<(ClipboardMode Mode, List<string> Paths)> ReadClipboardFilesAsync()
    {
        ClipboardService.ReadPaths(out ClipboardMode mode, out string[] ferretPaths);
        if (Clipboard == null)
        {
            return (mode, ferretPaths.ToList());
        }

        IStorageItem[]? items = await Clipboard.TryGetFilesAsync();
        List<string> systemPaths = new();
        foreach (IStorageItem item in items ?? [])
        {
            string? path = item.TryGetLocalPath();
            if (path != null)
            {
                systemPaths.Add(Path.TrimEndingDirectorySeparator(path));
            }

            item.Dispose();
        }

        if (systemPaths.Count > 0)
        {
            bool isFerretCopy = systemPaths.Order(StringComparer.Ordinal).SequenceEqual(ferretPaths.Order(StringComparer.Ordinal));
            return (isFerretCopy ? mode : ClipboardMode.Copy, systemPaths);
        }

        if (ferretPaths.Length == 0)
        {
            return (ClipboardMode.Copy, new());
        }

        string? text = await Clipboard.TryGetTextAsync();
        bool isCurrent = text == null
            ? !(await Clipboard.GetDataFormatsAsync()).Contains(DataFormat.Bitmap)
            : text.Replace("\r\n", "\n").Trim('\n') == string.Join('\n', ferretPaths);
        return isCurrent ? (mode, ferretPaths.ToList()) : (ClipboardMode.Copy, new());
    }

    private async Task ReplyAddonClipboardTypesAsync(AddonRun run)
    {
        ReplyToRun(run, FormatClipboardContent(await GetClipboardContentAsync()));
    }

    private async Task ReplyAddonClipboardFilesAsync(AddonRun run)
    {
        (ClipboardMode mode, List<string> paths) = await ReadClipboardFilesAsync();
        if (paths.Count == 0)
        {
            ReplyToRun(run, "");
            return;
        }

        string modeName = mode == ClipboardMode.Cut ? "cut" : "copy";
        ReplyToRun(run, string.Join('\t', paths.Prepend(modeName)));
    }

    private async Task SaveAddonClipboardTextAsync(AddonRun run, string argument)
    {
        if (argument.Length == 0)
        {
            ReportInvalidAddonArgument(run, "clipboard-text", argument);
            ReplyToRun(run, "no");
            return;
        }

        string? text = Clipboard == null ? null : await Clipboard.TryGetTextAsync();
        bool saved = text != null && TryWriteAddonFile(run, argument, path => File.WriteAllText(path, text, new UTF8Encoding(false)));
        ReplyToRun(run, saved ? "yes" : "no");
    }

    private async Task SaveAddonClipboardImageAsync(AddonRun run, string argument)
    {
        if (argument.Length == 0)
        {
            ReportInvalidAddonArgument(run, "clipboard-image", argument);
            ReplyToRun(run, "no");
            return;
        }

        Bitmap? bitmap = Clipboard == null ? null : await Clipboard.TryGetBitmapAsync();
        if (bitmap == null)
        {
            ReplyToRun(run, "no");
            return;
        }

        using (bitmap)
        {
            ReplyToRun(run, TryWriteAddonFile(run, argument, path => bitmap.Save(path, new PngBitmapEncoderOptions())) ? "yes" : "no");
        }
    }

    private bool TryWriteAddonFile(AddonRun run, string argument, Action<string> write)
    {
        string path = ResolveAddonPath(Addon.ExpandHome(argument));
        try
        {
            write(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AddonLogService.Add(run.Addon, run.Source, AddonLogLevel.Warning, LocalizationService.Format("AddonLogWriteFailed", path, ex.Message));
            return false;
        }
    }
}
