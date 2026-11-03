using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace FileExplorer.Gui.Controls;

public class ThumbnailImage : Image
{
    private const int NearestScalingMaxSize = 68;

    protected override Type StyleKeyOverride => typeof(Image);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != SourceProperty)
        {
            return;
        }

        bool isSmall = Source is Bitmap { PixelSize: { Width: <= NearestScalingMaxSize, Height: <= NearestScalingMaxSize } };
        RenderOptions.SetBitmapInterpolationMode(this, isSmall ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
    }
}
