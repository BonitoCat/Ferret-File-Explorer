using Avalonia.Media;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.ViewModels;

public class AddonDonationViewModel
{
    public string Url { get; }
    public string ToolTip { get; }
    public IImage? Icon { get; }

    public AddonDonationViewModel(AddonDonation donation)
    {
        Url = donation.Url;
        ToolTip = LocalizationService.Format("AddonDonateTooltip", donation.Provider.Name);
        Icon = IconService.Get($"donate-{donation.Provider.Id}");
    }
}
