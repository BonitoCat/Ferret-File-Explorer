using TuiLib;

namespace FileExplorer.Cli.Models;

public class ColoredText
{
    public string Text { get; set; } = "";
    public Color ForeColor { get; set; } = Color.Reset;
    public Color BackColor { get; set; } = Color.Reset;
    public bool ResetColorAfter { get; set; }
    public int Length => Text.Length;

    public ColoredText() { }

    public ColoredText(string text)
    {
        Text = text;
    }

    public ColoredText(string text, Color? foreColor = null, Color? backColor = null)
    {
        Text = text;
        ForeColor = foreColor ?? Color.Reset;
        BackColor = backColor ?? Color.Reset;
    }
    
    public static implicit operator ColoredText(string text)
    {
        return new(text);
    }
    
    public override string ToString()
    {
        string str = $"{ForeColor.ToAnsi()}{BackColor.ToAnsi(Color.AnsiType.Background)}{Text}";
        return ResetColorAfter ? $"{str}\x1b[0m" : str;
    }
}