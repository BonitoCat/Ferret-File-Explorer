using System;
using Avalonia.Media;

namespace FileExplorer.Gui.ViewModels;

public class OperationProgressViewModel : ViewModelBase
{
    private string _title = "";
    private string _status = "";
    private string _text = "";
    private string _item = "";
    private double _value;
    private double _maximum = 100;
    private bool _isIndeterminate;
    private bool _isSelected;
    private IImage? _icon;

    public Action? CancelAction { get; set; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public string Item
    {
        get => _item;
        set => SetProperty(ref _item, value);
    }

    public double Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(PercentText));
            }
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            if (SetProperty(ref _maximum, Math.Max(value, 0)))
            {
                OnPropertyChanged(nameof(PercentText));
            }
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            if (SetProperty(ref _isIndeterminate, value))
            {
                OnPropertyChanged(nameof(PercentText));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public IImage? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool CanCancel => CancelAction != null;

    public string PercentText => IsIndeterminate || Maximum <= 0 ? "" : $"{Math.Clamp(Value / Maximum * 100, 0, 100):0}%";

    public void Cancel()
    {
        CancelAction?.Invoke();
    }
}
