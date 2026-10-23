using Avalonia;
using Avalonia.Media;

namespace FileExplorer.Gui.Services;

public sealed class LazyIcon : IImage
{
    private readonly string _key;
    private IImage? _image;
    private bool _resolved;

    public LazyIcon(string key)
    {
        _key = key;
    }

    public Size Size => Resolve()?.Size ?? default;

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
    {
        Resolve()?.Draw(context, sourceRect, destRect);
    }

    private IImage? Resolve()
    {
        if (_resolved)
        {
            return _image;
        }

        _image = IconService.Get(_key);
        _resolved = true;
        return _image;
    }
}
