using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Dialogs;

namespace FileExplorer.Gui.Services;

public static class AddonSettingsEditor
{
    public static async Task<bool> EditAsync(Window owner, Addon addon)
    {
        string language = LocalizationService.Instance.LanguageCode;
        List<AddonField> fields = addon.Settings
            .Select(s => new AddonField(s.Id, addon.Translate(s.Label, language), addon.Translate(s.Description, language), s.Type, s.Default, s.Choices, s.Min, s.Max))
            .ToList();

        AddonFormDialog dialog = new(LocalizationService.Format("AddonSettingsTitle", addon.GetName(language)), "", fields, AddonSettingsStore.GetValues(addon))
        {
            ShowReset = true,
            ConfirmText = LocalizationService.Get("Save"),
        };

        Dictionary<string, string>? values = await dialog.ShowDialog<Dictionary<string, string>?>(owner);
        if (values == null)
        {
            return false;
        }

        AddonSettingsStore.SetValues(addon, values);
        return true;
    }
}
