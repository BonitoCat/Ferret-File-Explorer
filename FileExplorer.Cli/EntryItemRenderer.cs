using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

using FileExplorer.Cli.Context;
using FileExplorer.Cli.Models;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Services;

using TuiLib;
using TuiLib.Components;
using TuiLib.Controls;

namespace FileExplorer.Cli;

public class EntryItemRenderer
{
    public static IEnumerable<RenderPart> RenderModel(EntryContext context, EntryListItem entry, bool isFocused)
    {
        TuiListBox<EntryListItem> entries = context.Menu;
        if (entries is null)
        {
            yield break;
        }
        
        int index = entries.IndexOf(entry);
        int viewIndex = entries.ViewIndex;
        int viewRange = entries.ViewRange;
        int count = entries.Items.Count;
        
        string cursor;
        if (index == entries.SelectedIndex)
        {
            cursor = " > ";
        }
        else if (index == viewIndex + viewRange - 1 && viewIndex + viewRange < count)
        {
            cursor = "\x1b[38;2;140;140;140m ⌄ \x1b[0m";
        }
        else if (index == viewIndex && viewIndex > 0)
        {
            cursor = "\x1b[38;2;140;140;140m ⌃ \x1b[0m";
        }
        else
        {
            cursor = "   ";
        }
        
        bool isSelected = context.SelectedItems.Contains(entry);
        bool isCut = context.Clipboard.Mode == ClipboardMode.Cut && context.Clipboard.Items.Contains(entry.Entry.FullPath);

        string prefixString = string.Concat(entry.Prefixes.Select(p => p.ToString()));
        string suffixString = string.Concat(entry.Suffixes.Select(s => s.ToString()));

        string sizeText = "";
        string sizeVisible = "";
        if (context.ShowFileSizes && entry.Entry is FileEntry fileEntry)
        {
            double size = fileEntry.FileSize;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unitIndex = 0;
            
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            sizeVisible = $"{size:0.##} {units[unitIndex]}";
            sizeText = (index == entries.SelectedIndex || isSelected ? Color.White.ToAnsi() : Color.Gray.ToAnsi()) + sizeVisible;
        }

        int targetWidth = context.HasActiveImagePreview ? entries.MaxWidth / 2 : entries.MaxWidth;
        int fixedWidth = VisibleLength(cursor) + VisibleLength(entry.Icon.ToString()) + VisibleLength(prefixString) + VisibleLength(suffixString) + sizeVisible.Length + 5;
        string name = Truncate(entry.DisplayName.Text, targetWidth - fixedWidth);
        ColoredText displayName = new(name, entry.DisplayName.ForeColor, entry.DisplayName.BackColor);
        string plainEntry = $"{entry.Icon}{prefixString}{displayName}{suffixString}\x1b[0m";

        string entryText;
        if (isSelected)
        {
            if (isCut)
            {
                entryText = $"\x1b[38;2;180;180;180m{Color.TrimAnsi(entry.Icon.ToString())}\x1b[0m{prefixString}\x1b[47m\x1b[38;2;0;0;0m{name}\x1b[0m{suffixString}";
            }
            else
            {
                entryText = $"{entry.Icon}{prefixString}\x1b[47m\x1b[30m{name}\x1b[0m{suffixString}";
            }
        }
        else
        {
            if (isCut)
            {
                entryText = isFocused ? $"\x1b[38;2;200;200;200m{Color.TrimAnsi(plainEntry)}\x1b[0m" : $"\x1b[38;2;150;150;150m{Color.TrimAnsi(plainEntry)}\x1b[0m";
            }
            else
            {
                entryText = isFocused ? plainEntry : $"\x1b[38;2;180;180;180m{Color.TrimAnsi(plainEntry)}\x1b[0m";
            }
        }
        
        int textElementsLength = VisibleLength($"{cursor}{entryText}{sizeText}");
        int spacesNeeded = Math.Max(0, targetWidth - textElementsLength - 4);
        
        string finalPadding = new(' ', spacesNeeded);
        
        yield return new RenderPart
        {
            Text = $"{cursor}{entryText}{finalPadding}{sizeText}    ",
            Foreground = isFocused ? Color.Reset : new Color(100, 100, 100),
        };
    }

    private static int VisibleLength(string text)
    {
        string stripped = Regex.Replace(text, @"\x1b\[[0-9;]*m", "");
        return new StringInfo(stripped).LengthInTextElements;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 1)
        {
            return text.Length > 0 ? "…" : text;
        }

        StringInfo info = new(text);
        if (info.LengthInTextElements <= maxLength)
        {
            return text;
        }

        return info.SubstringByTextElements(0, maxLength - 1) + "…";
    }
}
