using System;
using Avalonia.Markup.Xaml;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.Markup;

public class TranslateExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TranslateExtension() { }

    public TranslateExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return LocalizationService.Get(Key);
    }
}
