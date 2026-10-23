using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui.Markup;

public class KeybindExtension : MarkupExtension
{
    public string Id { get; set; } = "";

    public KeybindExtension() { }

    public KeybindExtension(string id)
    {
        Id = id;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding(nameof(KeybindService.Version))
        {
            Source = KeybindService.Instance,
            Mode = BindingMode.OneWay,
            Converter = new KeybindGestureConverter(Id),
        };
    }

    private sealed class KeybindGestureConverter : IValueConverter
    {
        private readonly string _id;

        public KeybindGestureConverter(string id)
        {
            _id = id;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return KeybindService.Instance.Get(_id);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
