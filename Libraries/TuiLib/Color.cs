using System.Text.RegularExpressions;

namespace TuiLib;

public sealed record Color(int R, int G, int B)
{
    public enum AnsiType { Foreground, Background, Both }
    public static readonly Color Reset = new(-1, -1, -1);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Gray = new(128, 128, 128);
    public static readonly Color LightGray = new(192, 192, 192);
    public static readonly Color Red = new(255, 0, 0);
    public static readonly Color Yellow = new(255, 255, 0);
    public static readonly Color Orange = new(255, 165, 0);
    public static readonly Color Cyan = new(0, 255, 255);
    public Color Transform(int r, int g, int b) => new(Math.Clamp(R + r, 0, 255), Math.Clamp(G + g, 0, 255), Math.Clamp(B + b, 0, 255));
    public string ToAnsi(AnsiType type = AnsiType.Foreground)
    {
        if (type == AnsiType.Both) return ToAnsi() + ToAnsi(AnsiType.Background);
        if (R < 0) return type == AnsiType.Foreground ? "\x1b[39m" : "\x1b[49m";
        return $"\x1b[{(type == AnsiType.Foreground ? 38 : 48)};2;{Math.Clamp(R, 0, 255)};{Math.Clamp(G, 0, 255)};{Math.Clamp(B, 0, 255)}m";
    }
    public static string TrimAnsi(string text) => Regex.Replace(text, @"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)", "");
}
