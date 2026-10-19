using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.Dialogs;

public partial class ThemeColorsDialog : Window
{
    private enum ColorSlot
    {
        Primary,
        Accent,
        Selection,
    }

    private string _primary = ThemeService.PrimaryColor;
    private string _accent = ThemeService.AccentColor;
    private string _selection = ThemeService.SelectionColor;
    private ColorSlot _slot = ColorSlot.Primary;
    private bool _syncingPicker;

    public ThemeColorsDialog()
    {
        InitializeComponent();
        BuildPresets();
        SelectSlot(ColorSlot.Primary);
    }

    private void BuildPresets()
    {
        List<(string Name, string? Hex)> presets = [("DefaultColor", null)];
        foreach ((string name, string hex) in FolderColorService.Presets)
        {
            presets.Add(($"Color{name}", hex));
        }

        foreach ((string name, string? hex) in presets)
        {
            Button swatch = new()
            {
                Classes = { "swatch" },
                Tag = hex,
                Content = new Border { Background = hex == null ? null : new SolidColorBrush(Color.Parse(hex)) },
            };
            ToolTip.SetTip(swatch, LocalizationService.Get(name));
            swatch.Click += OnPresetClick;
            PresetPanel.Children.Add(swatch);
        }

        UpdateDefaultSwatch();
    }

    private void UpdateDefaultSwatch()
    {
        if (PresetPanel.Children.Count == 0 || PresetPanel.Children[0] is not Button { Content: Border border })
        {
            return;
        }

        border.Background = new SolidColorBrush(Color.Parse(GetDefault(_slot)));
    }

    private static string GetDefault(ColorSlot slot)
    {
        return slot switch
        {
            ColorSlot.Accent => ThemeService.DefaultAccentColor,
            ColorSlot.Selection => ThemeService.DefaultSelectionColor,
            _ => ThemeService.DefaultPrimaryColor,
        };
    }

    private string GetCurrent()
    {
        return _slot switch
        {
            ColorSlot.Accent => _accent,
            ColorSlot.Selection => _selection,
            _ => _primary,
        };
    }

    private void SetCurrent(string hex)
    {
        switch (_slot)
        {
            case ColorSlot.Accent:
                _accent = hex;
                break;
            case ColorSlot.Selection:
                _selection = hex;
                break;
            default:
                _primary = hex;
                break;
        }
    }

    private void SelectSlot(ColorSlot slot)
    {
        _slot = slot;
        PrimarySlot.Classes.Set("selected", slot == ColorSlot.Primary);
        AccentSlot.Classes.Set("selected", slot == ColorSlot.Accent);
        SelectionSlot.Classes.Set("selected", slot == ColorSlot.Selection);
        UpdateDefaultSwatch();

        _syncingPicker = true;
        Picker.Color = Color.Parse(GetCurrent());
        _syncingPicker = false;

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        Color primary = Color.Parse(_primary);
        Color accent = Color.Parse(_accent);
        Color selection = Color.Parse(_selection);
        IImage? folder = IconService.GetColored("folder", _primary, false);

        PrimarySwatch.Background = new SolidColorBrush(primary);
        AccentSwatch.Background = new SolidColorBrush(accent);
        SelectionSwatch.Background = new SolidColorBrush(selection);

        PreviewFolder.Source = folder;
        PreviewSelectionIcon.Source = folder;
        PreviewRubberBand.Background = new SolidColorBrush(ThemeService.GetSelectionTranslucent(selection));
        PreviewRubberBand.BorderBrush = new SolidColorBrush(selection);
        PreviewSelection.Background = new SolidColorBrush(ThemeService.GetSelectionSubtle(selection));
        PreviewProgress.Foreground = new SolidColorBrush(accent);
        PreviewButton.Background = new SolidColorBrush(accent);

        HexText.Text = GetCurrent();
    }

    private void OnColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (_syncingPicker)
        {
            return;
        }

        SetCurrent(ThemeService.ToHex(e.NewColor));
        UpdatePreview();
    }

    private void OnPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        SetCurrent(button.Tag as string ?? GetDefault(_slot));
        SelectSlot(_slot);
    }

    private void OnPrimarySlotClick(object? sender, RoutedEventArgs e)
    {
        SelectSlot(ColorSlot.Primary);
    }

    private void OnAccentSlotClick(object? sender, RoutedEventArgs e)
    {
        SelectSlot(ColorSlot.Accent);
    }

    private void OnSelectionSlotClick(object? sender, RoutedEventArgs e)
    {
        SelectSlot(ColorSlot.Selection);
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _primary = ThemeService.DefaultPrimaryColor;
        _accent = ThemeService.DefaultAccentColor;
        _selection = ThemeService.DefaultSelectionColor;
        SelectSlot(_slot);
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        ThemeService.SetColors(_primary, _accent, _selection);
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
