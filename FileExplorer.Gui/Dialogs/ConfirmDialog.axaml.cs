using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public string CancelText
    {
        set => CancelButton.Content = value;
    }

    public ConfirmDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            ConfirmButton.Focus();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Confirmed = true;
                Close(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Confirmed = false;
                Close(false);
                e.Handled = true;
            }
        }, Tunnel);
    }

    public ConfirmDialog(string title, string message) : this(title, message, "OK", false, false)
    {
    }

    public ConfirmDialog(string title, string message, string confirmText, bool showCancel, bool isDestructive) : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.IsVisible = showCancel;

        if (isDestructive)
        {
            ConfirmButton.Background = new SolidColorBrush(Color.Parse("#C0392B"));
        }
        else
        {
            ConfirmButton.Background = ThemeService.AccentBrush;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }
}
