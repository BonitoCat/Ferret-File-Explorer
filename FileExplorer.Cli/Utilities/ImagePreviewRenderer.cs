using System.Text;

using FileExplorer.Cli.Models;
using FileExplorer.Core.Entries;

using SkiaSharp;

using TuiLib.Components;
using Color = TuiLib.Color;

namespace FileExplorer.Cli.Utilities;

public static class ImagePreviewRenderer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".tiff", ".tif", ".gif",
    };

    private static string? _cachedPath;
    private static int _cachedWindowWidth;
    private static int _cachedWindowHeight;
    private static List<RenderPart> _cachedParts = new();

    public static bool IsImage(EntryListItem? item)
    {
        if (item is null)
        {
            return false;
        }
        if (item.Entry is FileEntry fileEntry && fileEntry.FileType == FileType.Image)
        {
            return true;
        }

        string extension = Path.GetExtension(item.Entry.FullPath);
        return ImageExtensions.Contains(extension);
    }

    public static List<RenderPart> GetPreviewParts(string filePath, int menuY, int menuMaxHeight, int windowWidth, int windowHeight)
    {
        if (windowWidth < 40 || menuMaxHeight < 4)
        {
            return [];
        }
        if (filePath == _cachedPath && windowWidth == _cachedWindowWidth && windowHeight == _cachedWindowHeight)
        {
            return _cachedParts;
        }

        _cachedPath = filePath;
        _cachedWindowWidth = windowWidth;
        _cachedWindowHeight = windowHeight;
        _cachedParts = GeneratePreview(filePath, menuY, menuMaxHeight, windowWidth);

        return _cachedParts;
    }

    public static void ClearCache()
    {
        _cachedPath = null;
        _cachedParts.Clear();
    }

    private static List<RenderPart> GeneratePreview(string filePath, int menuY, int menuMaxHeight, int windowWidth)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        SKBitmap? image = LoadScaled(filePath, windowWidth / 2 - 2, (int) (menuMaxHeight * 1.8f));
        if (image == null)
        {
            return [];
        }

        using (image)
        {
            int previewAreaX = windowWidth / 2 + 1;
            int availableWidth = windowWidth - previewAreaX - 1;
            int imageRows = (image.Height + 1) / 2;
            int drawX = previewAreaX + Math.Max(0, (availableWidth - image.Width) / 2);
            int drawY = menuY + Math.Max(0, (menuMaxHeight - imageRows) / 2);
            int leftPad = Math.Max(0, drawX - previewAreaX);
            int rightPad = Math.Max(0, availableWidth - leftPad - image.Width);

            List<RenderPart> parts = new();

            for (int row = 0; row < menuMaxHeight; row++)
            {
                int currentY = menuY + row;
                if (currentY < drawY || currentY >= drawY + imageRows)
                {
                    parts.Add(new RenderPart
                    {
                        X = previewAreaX,
                        Y = currentY,
                        Text = new string(' ', availableWidth),
                        Foreground = Color.Reset,
                        Background = Color.Reset,
                        ZIndex = 1,
                    });
                    continue;
                }

                int imageY = (currentY - drawY) * 2;
                StringBuilder builder = new();
                if (leftPad > 0)
                {
                    builder.Append(new string(' ', leftPad));
                }

                for (int x = 0; x < image.Width; x++)
                {
                    SKColor topPixel = image.GetPixel(x, imageY);
                    SKColor bottomPixel = imageY + 1 < image.Height
                        ? image.GetPixel(x, imageY + 1)
                        : new SKColor(0, 0, 0, 255);

                    Color topColor = ToTuiColor(topPixel);
                    Color bottomColor = ToTuiColor(bottomPixel);

                    builder.Append(topColor.ToAnsi(Color.AnsiType.Foreground));
                    if (imageY + 1 < image.Height)
                    {
                        builder.Append(bottomColor.ToAnsi(Color.AnsiType.Background));
                    }
                    else
                    {
                        builder.Append(Color.Reset.ToAnsi(Color.AnsiType.Background));
                    }

                    builder.Append('▀');
                }

                builder.Append(Color.Reset.ToAnsi(Color.AnsiType.Both));
                if (rightPad > 0)
                {
                    builder.Append(new string(' ', rightPad));
                }

                parts.Add(new RenderPart
                {
                    X = previewAreaX,
                    Y = currentY,
                    Text = builder.ToString(),
                    Foreground = Color.Reset,
                    Background = Color.Reset,
                    ZIndex = 1,
                });
            }

            return parts;
        }
    }

    private static SKBitmap? LoadScaled(string filePath, int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            return null;
        }

        try
        {
            using SKBitmap? source = SKBitmap.Decode(filePath);
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return null;
            }

            float scale = Math.Min((float) maxWidth / source.Width, (float) maxHeight / source.Height);
            int width = Math.Max(1, (int) (source.Width * scale));
            int height = Math.Max(1, (int) (source.Height * scale));
            SKImageInfo info = new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Color ToTuiColor(SKColor pixel)
    {
        if (pixel.Alpha == 255)
        {
            return new Color(pixel.Red, pixel.Green, pixel.Blue);
        }

        byte r = (byte) (pixel.Red * pixel.Alpha / 255);
        byte g = (byte) (pixel.Green * pixel.Alpha / 255);
        byte b = (byte) (pixel.Blue * pixel.Alpha / 255);

        return new Color(r, g, b);
    }
}
