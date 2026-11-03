using System.Collections.Generic;
using Avalonia.Input;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.ViewModels;

public sealed class KeybindRowViewModel : ViewModelBase
{
    private bool _isCapturing;

    public KeybindRowViewModel(string id, string title, string? defaultGesture)
    {
        Id = id;
        Title = title;
        DefaultGesture = defaultGesture;
    }

    public string Id { get; }
    public string Title { get; }
    public string? DefaultGesture { get; }

    public KeyGesture? Gesture => KeybindService.Instance.Get(Id, DefaultGesture);
    public bool IsCustomized => KeybindService.Instance.IsCustomized(Id);
    public bool HasGesture => Gesture != null;

    public string GestureText
    {
        get
        {
            if (IsCapturing)
            {
                return LocalizationService.Get("KeybindPressKeys");
            }

            KeyGesture? gesture = Gesture;
            return gesture == null ? LocalizationService.Get("KeybindNotSet") : KeybindService.Format(gesture);
        }
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (SetProperty(ref _isCapturing, value))
            {
                OnPropertyChanged(nameof(GestureText));
            }
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Gesture));
        OnPropertyChanged(nameof(GestureText));
        OnPropertyChanged(nameof(IsCustomized));
        OnPropertyChanged(nameof(HasGesture));
    }
}

public sealed class KeybindGroupViewModel
{
    public KeybindGroupViewModel(string title, List<KeybindRowViewModel> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }
    public List<KeybindRowViewModel> Rows { get; }
}
