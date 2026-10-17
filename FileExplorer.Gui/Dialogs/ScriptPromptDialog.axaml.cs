using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public enum ScriptAction
{
    Cancel,
    RunInTerminal,
    ShowContent,
    Run,
}

public partial class ScriptPromptDialog : Window
{
    public ScriptPromptDialog()
    {
        InitializeComponent();

        Opened += (_, _) => RunButton.Focus();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close(ScriptAction.Cancel);
            e.Handled = true;
        }, Tunnel);
    }

    public ScriptPromptDialog(string path) : this()
    {
        HeaderText.Text = $"“{Path.GetFileName(path)}” is an executable text file.";
    }

    private void OnRunInTerminalClick(object? sender, RoutedEventArgs e)
    {
        Close(ScriptAction.RunInTerminal);
    }

    private void OnShowContentClick(object? sender, RoutedEventArgs e)
    {
        Close(ScriptAction.ShowContent);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(ScriptAction.Cancel);
    }

    private void OnRunClick(object? sender, RoutedEventArgs e)
    {
        Close(ScriptAction.Run);
    }
}
