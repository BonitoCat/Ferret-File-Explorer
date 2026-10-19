using System;
using System.Collections.Generic;
using Avalonia.Media;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.ViewModels;

public enum AddonStoreItemState
{
    NotInstalled,
    Installed,
    UpdateAvailable,
    Installing,
}

public class AddonStoreItemViewModel : ViewModelBase
{
    private AddonStoreItemState _state;
    private IImage? _icon;
    private double _progress;

    public AddonStoreEntry Entry { get; }
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Details { get; }

    public bool HasDescription => Description.Length > 0;
    public bool HasDetails => Details.Length > 0;
    public bool HasIcon => Icon != null;
    public bool IsInstalling => State == AddonStoreItemState.Installing;
    public bool CanInstall => State is AddonStoreItemState.NotInstalled or AddonStoreItemState.UpdateAvailable;
    public bool IsProgressIndeterminate => Progress < 0;

    public string ActionText => State switch
    {
        AddonStoreItemState.Installed => LocalizationService.Get("AddonStoreInstalled"),
        AddonStoreItemState.UpdateAvailable => LocalizationService.Get("AddonStoreUpdate"),
        AddonStoreItemState.Installing => LocalizationService.Get("AddonStoreInstalling"),
        _ => LocalizationService.Get("AddonStoreInstall"),
    };

    public AddonStoreItemState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(IsInstalling));
        }
    }

    public IImage? Icon
    {
        get => _icon;
        set
        {
            if (SetProperty(ref _icon, value))
            {
                OnPropertyChanged(nameof(HasIcon));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
            {
                OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }
    }

    public AddonStoreItemViewModel(AddonStoreEntry entry)
    {
        string language = LocalizationService.Instance.LanguageCode;

        Entry = entry;
        Id = entry.Id ?? "";
        Name = entry.GetName(language);
        Description = entry.GetDescription(language);

        List<string> details = new();
        if (!string.IsNullOrWhiteSpace(entry.Version))
        {
            details.Add(LocalizationService.Format("AddonVersion", entry.Version.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(entry.Author))
        {
            details.Add(LocalizationService.Format("AddonAuthor", entry.Author.Trim()));
        }

        Details = string.Join(" · ", details);
        RefreshState();
    }

    public void RefreshState()
    {
        Addon? installed = AddonService.Instance.Find(Id);
        if (installed == null)
        {
            State = AddonStoreItemState.NotInstalled;
            return;
        }

        State = AddonStoreService.CompareVersions(Entry.Version, installed.Version) > 0
            ? AddonStoreItemState.UpdateAvailable
            : AddonStoreItemState.Installed;
    }

    public bool Matches(string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        return Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (Entry.Author ?? "").Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
