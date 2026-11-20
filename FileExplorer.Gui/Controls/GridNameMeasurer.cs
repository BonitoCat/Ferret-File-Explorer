using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace FileExplorer.Gui.Controls;

public interface INamedItem
{
    string Name { get; }
}

public static class GridNameMeasurer
{
    public const double FontSize = 13;
    public const double TextWidth = 110;
    private const int MaxCachedNames = 50000;

    private static readonly Dictionary<string, int> LineCounts = new(StringComparer.Ordinal);
    private static FontFamily? _fontFamily;
    private static double _lineHeight;

    public static double GetExtraHeight(string name, int maxLines, int baseLines, FontFamily fontFamily)
    {
        EnsureFont(fontFamily);
        int lines = GetLineCount(name);
        if (maxLines > 0)
        {
            lines = Math.Min(lines, maxLines);
        }

        return Math.Max(0, lines - baseLines) * _lineHeight;
    }

    public static bool IsTruncated(string name, int maxLines, FontFamily fontFamily)
    {
        if (maxLines <= 0)
        {
            return false;
        }

        EnsureFont(fontFamily);
        return GetLineCount(name) > maxLines;
    }

    private static void EnsureFont(FontFamily fontFamily)
    {
        if (_fontFamily != null && _fontFamily.Equals(fontFamily))
        {
            return;
        }

        _fontFamily = fontFamily;
        LineCounts.Clear();
        using TextLayout sample = CreateLayout("Xg");
        _lineHeight = sample.Height;
    }

    private static int GetLineCount(string name)
    {
        if (LineCounts.TryGetValue(name, out int count))
        {
            return count;
        }

        if (LineCounts.Count >= MaxCachedNames)
        {
            LineCounts.Clear();
        }

        using TextLayout layout = CreateLayout(name);
        count = Math.Max(1, layout.TextLines.Count);
        LineCounts[name] = count;
        return count;
    }

    private static TextLayout CreateLayout(string text)
    {
        return new(
            text,
            new Typeface(_fontFamily ?? FontFamily.Default),
            FontSize,
            null,
            TextAlignment.Center,
            TextWrapping.Wrap,
            maxWidth: TextWidth);
    }
}
