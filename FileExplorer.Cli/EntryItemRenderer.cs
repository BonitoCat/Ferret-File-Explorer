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
        
        string entryText;
        if (isSelected)
        {
            string prefixString = "";
            if (entry.Prefixes.Count > 0)
            {
                prefixString = string.Concat(entry.Prefixes.Select(p => p.ToString()));
            }
            
            string suffixString = "";
            if (entry.Suffixes.Count > 0)
            {
                suffixString = string.Concat(entry.Suffixes.Select(s => s.ToString()));
            }
            
            if (isCut)
            {
                entryText = $"\x1b[38;2;180;180;180m{Color.TrimAnsi(entry.Icon.ToString())}\x1b[0m{prefixString}\x1b[47m\x1b[38;2;0;0;0m{entry.DisplayName.Text}\x1b[0m{suffixString}";
            }
            else
            {
                entryText = $"{entry.Icon}{prefixString}\x1b[47m\x1b[30m{entry.DisplayName.Text}\x1b[0m{suffixString}";
            }
        }
        else
        {
            if (isCut)
            {
                entryText = isFocused ? $"\x1b[38;2;200;200;200m{Color.TrimAnsi(entry.ToString())}\x1b[0m" : $"\x1b[38;2;150;150;150m{Color.TrimAnsi(entry.ToString())}\x1b[0m";
            }
            else
            {
                entryText = isFocused ? entry.ToString() : $"\x1b[38;2;180;180;180m{Color.TrimAnsi(entry.ToString())}\x1b[0m";
            }
        }
        
        string sizeText = "";
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

            if (index == entries.SelectedIndex || isSelected)
            {
                sizeText = Color.White.ToAnsi();
            }
            else
            {
                sizeText = Color.Gray.ToAnsi();
            }
            
            sizeText += $"{size:0.##} {units[unitIndex]}";
        }
        
        string rawVisibleText = Regex.Replace($"{cursor}{entryText}{sizeText}", @"\x1b\[[0-9;]*m", "");
        int textElementsLength = new StringInfo(rawVisibleText).LengthInTextElements;
        int targetWidth = context.HasActiveImagePreview ? entries.MaxWidth / 2 : entries.MaxWidth;
        int spacesNeeded = Math.Max(0, targetWidth - textElementsLength - 4);
        
        string finalPadding = new(' ', spacesNeeded);
        
        yield return new RenderPart
        {
            Text = $"{cursor}{entryText}{finalPadding}{sizeText}    ",
            Foreground = isFocused ? Color.Reset : new Color(100, 100, 100),
        };
    }
}
