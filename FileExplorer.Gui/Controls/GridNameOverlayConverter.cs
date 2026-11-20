using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FileExplorer.Gui.Controls;

public sealed class GridNameOverlayConverter : IMultiValueConverter
{
    public static readonly GridNameOverlayConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 5 || values[0] is not true || values[1] is not 1 || values[2] is not string name || values[3] is not int lines)
        {
            return false;
        }

        FontFamily fontFamily = values[4] as FontFamily ?? FontFamily.Default;
        return GridNameMeasurer.IsTruncated(name, lines, fontFamily);
    }
}
