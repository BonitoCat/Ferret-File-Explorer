using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TuiLib.Components;

namespace TuiLib.Rendering;

public static class TerminalText
{
    private static readonly Regex Tokens = new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)|[^\x1B]+", RegexOptions.Compiled);
    internal static IEnumerable<(string Text, int Width, bool Style)> Read(string text)
    {
        foreach (Match match in Tokens.Matches(text))
        {
            if (match.Value[0] == '\x1b')
            {
                // Text may contain colors, but never allow cursor movement or OSC commands.
                if (match.Value.StartsWith("\x1b[") && match.Value.EndsWith('m')) yield return (match.Value, 0, true);
                continue;
            }
            var elements = StringInfo.GetTextElementEnumerator(match.Value);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                int rune = Rune.GetRuneAt(element, 0).Value;
                var category = Rune.GetUnicodeCategory(Rune.GetRuneAt(element, 0));
                if (category == UnicodeCategory.Control) continue;
                int width = category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format ? 0
                    : element.Contains('\ufe0f') || element.Contains('\u20e3') || rune is >= 0x1100 and <= 0x115f or >= 0x2329 and <= 0x232a or >= 0x2e80 and <= 0xa4cf or >= 0xac00 and <= 0xd7a3 or >= 0xf900 and <= 0xfaff or >= 0xfe10 and <= 0xfe19 or >= 0xfe30 and <= 0xfe6f or >= 0xff00 and <= 0xff60 or >= 0xffe0 and <= 0xffe6 or >= 0x1f1e6 and <= 0x1f1ff or >= 0x1f300 and <= 0x1faff or >= 0x20000 and <= 0x3fffd ? 2 : 1;
                yield return (element, width, false);
            }
        }
    }
    public static string Clip(string text, int skip, int width)
    {
        StringBuilder result = new();
        int position = 0, end = Math.Max(skip, 0) + Math.Max(width, 0);
        foreach (var token in Read(text))
        {
            if (token.Style) { if (position < end) result.Append(token.Text); continue; }
            if (position >= end) break;
            if (position >= skip && position + token.Width <= end) result.Append(token.Text);
            position += token.Width;
        }
        return result.ToString();
    }
}

public class TuiRenderer
{
    private readonly object _lock = new();
    private string[] _previous = Array.Empty<string>();
    private int _width;
    public void ClearScreen()
    {
        lock (_lock) { _previous = Array.Empty<string>(); if (!Console.IsOutputRedirected) Console.Write("\x1b[0m\x1b[2J\x1b[H"); }
    }
    public void Render(IEnumerable<RenderPart> parts)
    {
        if (Console.IsOutputRedirected) return;
        lock (_lock)
        {
            int width = Math.Max(Console.WindowWidth, 0), height = Math.Max(Console.WindowHeight, 0);
            var lines = Compose(parts, width, height);
            if (_width != width) _previous = Array.Empty<string>();
            StringBuilder output = new();
            for (int y = 0; y < height; y++)
                if (y >= _previous.Length || _previous[y] != lines[y]) output.Append($"\x1b[{y + 1};1H\x1b[0m\x1b[2K").Append(lines[y]).Append("\x1b[0m");
            Console.Write(output.ToString());
            _previous = lines;
            _width = width;
        }
    }
    public static string[] Compose(IEnumerable<RenderPart> parts, int width, int height)
    {
        width = Math.Max(width, 0); height = Math.Max(height, 0);
        var cells = new string?[height, width];
        var continuations = new bool[height, width];
        foreach (var part in parts.OrderBy(p => p.ZIndex))
        {
            if (part.Y < 0 || part.Y >= height) continue;
            int x = part.X;
            string style = part.Foreground.ToAnsi() + part.Background.ToAnsi(Color.AnsiType.Background);
            foreach (var token in TerminalText.Read(part.Text))
            {
                if (token.Style) { style = token.Text is "\x1b[0m" or "\x1b[m" ? token.Text : style + token.Text; continue; }
                if (x >= width) break;
                if (token.Width == 0) continue;
                if (x >= 0 && x + token.Width <= width)
                {
                    // Replacing either half of a wide glyph clears the other half too.
                    if (continuations[part.Y, x] && x > 0) cells[part.Y, x - 1] = null;
                    if (x + 1 < width && continuations[part.Y, x + 1]) { continuations[part.Y, x + 1] = false; cells[part.Y, x + 1] = null; }
                    if (token.Width == 2 && x + 2 < width && continuations[part.Y, x + 2]) { continuations[part.Y, x + 2] = false; cells[part.Y, x + 2] = null; }
                    cells[part.Y, x] = "\x1b[0m" + style + token.Text;
                    continuations[part.Y, x] = false;
                    if (token.Width == 2) { cells[part.Y, x + 1] = ""; continuations[part.Y, x + 1] = true; }
                }
                x += token.Width;
            }
        }
        string[] lines = new string[height];
        for (int y = 0; y < height; y++)
        {
            StringBuilder line = new();
            for (int x = 0; x < width; x++) line.Append(cells[y, x] ?? "\x1b[0m ");
            lines[y] = line.ToString();
        }
        return lines;
    }
}
