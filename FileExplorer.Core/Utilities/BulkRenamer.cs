using System.Globalization;
using System.Text.RegularExpressions;
using FileExplorer.Core.Models;

namespace FileExplorer.Core.Utilities;

public static class BulkRenamer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly Regex TrailingNumberRegex = new(@"[\s_-]*(\(\d+\)|\d+)$", RegexOptions.Compiled);

    public static string Apply(string name, bool isDirectory, int index, BulkRenameOptions options)
    {
        string number = (options.StartNumber + index).ToString().PadLeft(Math.Max(1, options.Digits), '0');
        (string stem, string extension) = SplitExtension(name, isDirectory);

        switch (options.Scope)
        {
            case BulkRenameScope.Full:
                return Transform(name, number, options);

            case BulkRenameScope.Extension:
                if (extension.Length == 0)
                {
                    return name;
                }

                string newExtension = Transform(extension, number, options);
                return newExtension.Length > 0 ? $"{stem}.{newExtension}" : stem;

            default:
                string newStem = Transform(stem, number, options);
                return extension.Length > 0 ? $"{newStem}.{extension}" : newStem;
        }
    }

    public static (string Stem, string Extension) SplitExtension(string name, bool isDirectory)
    {
        int dot = name.LastIndexOf('.');
        if (isDirectory || dot <= 0 || dot == name.Length - 1)
        {
            return (name, "");
        }

        return (name[..dot], name[(dot + 1)..]);
    }

    private static string Transform(string part, string number, BulkRenameOptions options)
    {
        return options.Mode switch
        {
            BulkRenameMode.Replace => Replace(part, options),
            BulkRenameMode.Insert => Insert(part, options.Text.Replace("{n}", number), options),
            BulkRenameMode.Remove => Remove(part, options),
            BulkRenameMode.Numbering => options.Pattern.Replace("{name}", options.StripNumbers ? StripTrailingNumber(part) : part).Replace("{n}", number),
            BulkRenameMode.Case => ChangeCase(part, options.Case),
            _ => part,
        };
    }

    private static string Replace(string part, BulkRenameOptions options)
    {
        if (options.Find.Length == 0)
        {
            return part;
        }

        if (options.UseRegex)
        {
            RegexOptions regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.Replace(part, options.Find, options.Replacement, regexOptions, RegexTimeout);
        }

        StringComparison comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return part.Replace(options.Find, options.Replacement, comparison);
    }

    private static string Insert(string part, string text, BulkRenameOptions options)
    {
        int offset = Math.Clamp(options.Position, 0, part.Length);
        int index = options.FromEnd ? part.Length - offset : offset;
        if (!options.Overwrite)
        {
            return part.Insert(index, text);
        }

        int end = Math.Min(part.Length, index + text.Length);
        return part[..index] + text + part[end..];
    }

    private static string Remove(string part, BulkRenameOptions options)
    {
        int offset = Math.Clamp(options.Position, 0, part.Length);
        int count = Math.Max(0, options.Count);
        int start = options.FromEnd ? Math.Max(0, part.Length - offset - count) : offset;
        int end = options.FromEnd ? part.Length - offset : Math.Min(part.Length, offset + count);

        return part[..start] + part[end..];
    }

    private static string StripTrailingNumber(string part)
    {
        string stripped = TrailingNumberRegex.Replace(part, "");
        return stripped.Length > 0 ? stripped : part;
    }

    private static string ChangeCase(string part, BulkRenameCase mode)
    {
        TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
        string lower = textInfo.ToLower(part);

        switch (mode)
        {
            case BulkRenameCase.Upper:
                return textInfo.ToUpper(part);

            case BulkRenameCase.Title:
                return textInfo.ToTitleCase(lower);

            case BulkRenameCase.Sentence:
                int first = lower.ToList().FindIndex(char.IsLetter);
                if (first < 0)
                {
                    return lower;
                }

                return lower[..first] + textInfo.ToUpper(lower[first]) + lower[(first + 1)..];

            default:
                return lower;
        }
    }
}
