using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class InputDialog : Window
{
    public string? ResultText { get; private set; }
    public bool SelectNameOnly { get; set; }

    public InputDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            InputTextBox.Focus();
            SelectInitialText();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                ResultText = null;
                Close(null);
                e.Handled = true;
            }
        }, Tunnel);
    }

    public InputDialog(string title, string prompt, string defaultText = "", bool isPassword = false) : this()
    {
        Title = title;
        PromptLabel.Text = prompt;
        InputTextBox.Text = defaultText;

        if (isPassword)
        {
            InputTextBox.PasswordChar = '•';
            TogglePasswordButton.IsVisible = true;
            UpdatePasswordIcon();
        }
    }

    private void SelectInitialText()
    {
        string text = InputTextBox.Text ?? "";
        int nameLength = SelectNameOnly ? GetNameLength(text) : text.Length;
        InputTextBox.SelectionStart = 0;
        InputTextBox.SelectionEnd = nameLength;
    }

    public static int GetNameLength(string fileName)
    {
        return FileUtils.GetNameLength(fileName);
    }

    private void OnTogglePasswordClick(object? sender, RoutedEventArgs e)
    {
        InputTextBox.PasswordChar = InputTextBox.PasswordChar == '\0' ? '•' : '\0';
        UpdatePasswordIcon();
    }

    private void UpdatePasswordIcon()
    {
        TogglePasswordIcon.Source = InputTextBox.PasswordChar == '\0'
            ? IconService.Get("eye-off")
            : IconService.Get("eye");
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        ResultText = InputTextBox.Text;
        Close(ResultText);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        ResultText = null;
        Close(null);
    }
}
