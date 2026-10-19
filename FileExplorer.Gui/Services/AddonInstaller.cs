using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Models;
using FileExplorer.Gui.Dialogs;

namespace FileExplorer.Gui.Services;

public static class AddonInstaller
{
    public static async Task<Addon?> InstallAsync(Window owner, PickerMode mode)
    {
        string initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        FilePickerOptions options = new()
        {
            Mode = mode,
            InitialPath = initialPath,
            Title = LocalizationService.Get(mode == PickerMode.Folder ? "SelectAddonFolder" : "SelectAddonZip"),
            ActionButtonText = LocalizationService.Get("Select"),
        };

        MainWindow picker = new(initialPath, options);
        FilePickerResult result = await picker.ShowPickerAsync(owner);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.SelectedPath))
        {
            return null;
        }

        return await InstallFromPathAsync(owner, result.SelectedPath);
    }

    public static async Task<Addon?> InstallFromPathAsync(Window owner, string sourcePath, bool confirmReplace = true)
    {
        AddonService service = AddonService.Instance;
        try
        {
            string id = service.GetInstallId(sourcePath);
            bool overwrite = service.IsInstalled(id);
            if (overwrite && confirmReplace)
            {
                ConfirmDialog confirm = new(
                    LocalizationService.Get("ReplaceAddonTitle"),
                    LocalizationService.Format("ConfirmReplaceAddon", id),
                    LocalizationService.Get("Replace"),
                    true,
                    true);
                if (await confirm.ShowDialog<bool?>(owner) != true)
                {
                    return null;
                }
            }

            return service.Install(sourcePath, overwrite);
        }
        catch (AddonInstallException ex)
        {
            await ShowErrorAsync(owner, LocalizationService.Format(ex.Key, ex.Detail));
            return null;
        }
    }

    public static async Task ShowErrorAsync(Window owner, string message)
    {
        ConfirmDialog dialog = new(LocalizationService.Get("AddonInstallFailedTitle"), message);
        await dialog.ShowDialog<bool?>(owner);
    }
}
