using System.Collections.Generic;
using Avalonia.Media;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.ViewModels;

public class AddonItemViewModel : ViewModelBase
{
    private bool _isEnabled;

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Details { get; }
    public string Error { get; }
    public IImage? Icon { get; }

    public bool HasDescription => Description.Length > 0;
    public bool HasDetails => Details.Length > 0;
    public bool HasError => Error.Length > 0;
    public bool HasIcon => Icon != null;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                AddonService.Instance.SetEnabled(Id, value);
            }
        }
    }

    public AddonItemViewModel(Addon addon)
    {
        string language = LocalizationService.Instance.LanguageCode;

        Id = addon.Id;
        Name = addon.GetName(language);
        Description = addon.GetDescription(language);
        Error = addon.HasError ? LocalizationService.Format(addon.ErrorKey!, addon.ErrorDetail) : "";
        Icon = IconService.LoadFile(addon.IconPath);
        _isEnabled = addon.IsEnabled;

        List<string> details = new();
        if (addon.Version.Length > 0)
        {
            details.Add(LocalizationService.Format("AddonVersion", addon.Version));
        }

        if (addon.Author.Length > 0)
        {
            details.Add(LocalizationService.Format("AddonAuthor", addon.Author));
        }

        Details = string.Join(" · ", details);
    }
}
