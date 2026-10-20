using Avalonia.Media;

namespace FileExplorer.Gui.ViewModels;

public class AddonStatusItemViewModel : ViewModelBase
{
    private string _text = "";
    private string _tooltip = "";
    private bool _isClickable;

    public string AddonId { get; }
    public string Id { get; }
    public IImage? Icon { get; }

    public AddonStatusItemViewModel(string addonId, string id, IImage? icon)
    {
        AddonId = addonId;
        Id = id;
        Icon = icon;
    }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string Tooltip
    {
        get => _tooltip;
        set
        {
            if (SetProperty(ref _tooltip, value))
            {
                OnPropertyChanged(nameof(HasTooltip));
            }
        }
    }

    public bool HasTooltip => Tooltip.Length > 0;

    public bool IsClickable
    {
        get => _isClickable;
        set => SetProperty(ref _isClickable, value);
    }
}
