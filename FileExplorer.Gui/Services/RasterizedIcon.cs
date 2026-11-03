using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace FileExplorer.Gui.Services;

public sealed class RasterizedIcon : IImage
{
    private const double Oversample = 2;

    private readonly IImage _inner;
    private readonly Dictionary<PixelSize, RenderTargetBitmap> _bitmaps = new();

    public RasterizedIcon(IImage inner)
    {
        _inner = inner;
    }

    public Size Size => _inner.Size;

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        RenderTargetBitmap? bitmap = GetBitmap(destRect.Size);
        if (bitmap == null)
        {
            _inner.Draw(context, sourceRect, destRect);
            return;
        }

        context.DrawImage(bitmap, new Rect(bitmap.Size), destRect);
    }

    private RenderTargetBitmap? GetBitmap(Size size)
    {
        PixelSize pixelSize = new((int) Math.Ceiling(size.Width * Oversample), (int) Math.Ceiling(size.Height * Oversample));
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0 || _inner.Size.Width <= 0 || _inner.Size.Height <= 0)
        {
            return null;
        }

        if (_bitmaps.TryGetValue(pixelSize, out RenderTargetBitmap? cached))
        {
            return cached;
        }

        try
        {
            RenderTargetBitmap bitmap = new(pixelSize, new Vector(96, 96));
            using (DrawingContext context = bitmap.CreateDrawingContext())
            {
                _inner.Draw(context, new Rect(_inner.Size), new Rect(bitmap.Size));
            }

            _bitmaps[pixelSize] = bitmap;
            return bitmap;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
