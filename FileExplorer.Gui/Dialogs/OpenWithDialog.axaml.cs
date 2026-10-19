using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public class OpenWithResult
{
    public required string Program { get; init; }
    public bool Always { get; init; }
}

public partial class OpenWithDialog : Window
{
    public OpenWithResult? Result { get; private set; }

    public OpenWithDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            Chooser.FocusInput();
        };

        Chooser.ProgramActivated += Confirm;

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Result = null;
                Close(null);
                e.Handled = true;
            }
        }, Tunnel);
    }

    public OpenWithDialog(string path) : this()
    {
        string extension = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(extension))
        {
            AlwaysOpenCheckBox.Content = $"{AlwaysOpenCheckBox.Content} ({extension})";
        }

        _ = Chooser.LoadAsync(path);
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        string? program = Chooser.SelectedProgram;
        if (string.IsNullOrEmpty(program))
        {
            return;
        }

        Result = new()
        {
            Program = program,
            Always = AlwaysOpenCheckBox.IsChecked ?? false
        };

        Close(Result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close(null);
    }
}
