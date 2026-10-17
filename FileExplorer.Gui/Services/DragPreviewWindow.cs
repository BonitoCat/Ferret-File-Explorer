using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui.Services;

public class DragPreviewWindow : Window
{
    private readonly StackPanel _container;
    private readonly Image _iconImage;
    private readonly Border _badgeBorder;
    private readonly TextBlock _badgeBlock;
    private readonly TextBlock _nameBlock;
    private readonly Border _nameBorder;

    public DragPreviewWindow(IWindowImpl? impl = null) : base(impl ?? PlatformManager.CreateWindow())
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        Focusable = false;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0.85;

        _iconImage = new()
        {
            Width = 56,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _badgeBlock = new()
        {
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _badgeBorder = new()
        {
            Background = new SolidColorBrush(Color.Parse("#3584E4")),
            CornerRadius = new(10),
            MinWidth = 20,
            Height = 20,
            Padding = new(6, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            BoxShadow = new(new BoxShadow
            {
                Blur = 4,
                Color = Color.FromArgb(120, 0, 0, 0),
                OffsetY = 1,
            }),
            Child = _badgeBlock,
            IsVisible = false,
        };

        Panel iconPanel = new()
        {
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconPanel.Children.Add(_iconImage);
        iconPanel.Children.Add(_badgeBorder);

        _nameBlock = new()
        {
            FontSize = 12,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 130,
        };

        _nameBorder = new()
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 24, 24, 26)),
            CornerRadius = new(5),
            Padding = new(6, 2),
            Margin = new(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = _nameBlock,
            IsVisible = false,
        };

        _container = new()
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _container.Children.Add(iconPanel);
        _container.Children.Add(_nameBorder);

        Content = _container;
    }

    public (int Width, int Height) UpdateContent(FileItemViewModel? item, int count)
    {
        if (item != null)
        {
            _iconImage.Source = item.Icon;
            _nameBlock.Text = item.Name ?? string.Empty;
            _nameBorder.IsVisible = !string.IsNullOrEmpty(item.Name);
        }
        else
        {
            _iconImage.Source = null;
            _nameBlock.Text = string.Empty;
            _nameBorder.IsVisible = false;
        }

        if (count > 1)
        {
            _badgeBlock.Text = count.ToString();
            _badgeBorder.IsVisible = true;
        }
        else
        {
            _badgeBorder.IsVisible = false;
        }

        _container.Measure(new(160, 200));
        _container.Arrange(new(0, 0, _container.DesiredSize.Width, _container.DesiredSize.Height));

        int width = Math.Max(64, (int) Math.Ceiling(_container.DesiredSize.Width));
        int height = Math.Max(64, (int) Math.Ceiling(_container.DesiredSize.Height));

        Width = width;
        Height = height;

        return (width, height);
    }

    public void Reset()
    {
        _iconImage.Source = null;
        _nameBlock.Text = string.Empty;
        _nameBorder.IsVisible = false;
        _badgeBorder.IsVisible = false;
    }
}
