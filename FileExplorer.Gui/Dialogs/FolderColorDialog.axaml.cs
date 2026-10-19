using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.Dialogs;

public partial class FolderColorDialog : Window
{
    public FolderColorDialog()
    {
        InitializeComponent();
    }

    public FolderColorDialog(string? initialHex) : this()
    {
        Picker.Color = Color.Parse(initialHex ?? ThemeService.PrimaryColor);
        UpdatePreview(Picker.Color);
    }

    private void OnColorChanged(object? sender, ColorChangedEventArgs e)
    {
        UpdatePreview(e.NewColor);
    }

    private void UpdatePreview(Color color)
    {
        string hex = ToHex(color);
        HexText.Text = hex;
        PreviewImage.Source = IconService.GetColored("folder", hex, false);
    }

    private static string ToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        Close(ToHex(Picker.Color));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
