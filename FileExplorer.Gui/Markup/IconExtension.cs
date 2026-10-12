using System;
using Avalonia;
using Avalonia.Markup.Xaml;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.Markup;

public class IconExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public IconExtension()
    {
    }

    public IconExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return AvaloniaProperty.UnsetValue;

        return (object?) IconService.Get(Key) ?? AvaloniaProperty.UnsetValue;
    }
}
