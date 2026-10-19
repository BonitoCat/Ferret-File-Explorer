using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FileExplorer.Gui.Services;

public static class ThemeService
{
    public const string DefaultPrimaryColor = FolderColorService.DefaultFolderColor;
    public const string DefaultAccentColor = "#3584E4";
    public const string DefaultSelectionColor = "#197EB1";

    private static readonly Color DefaultAccentSubtle = Color.Parse("#2A4B7C");
    private static readonly Color DefaultAccentLight = Color.Parse("#84B4FA");
    private static readonly Color SurfaceColor = Color.Parse("#1A1A1C");

    public static string PrimaryColor { get; private set; } = DefaultPrimaryColor;
    public static string AccentColor { get; private set; } = DefaultAccentColor;
    public static string SelectionColor { get; private set; } = DefaultSelectionColor;

    public static event Action? ColorsChanged;

    public static IBrush AccentBrush => new SolidColorBrush(Color.Parse(AccentColor));
    public static IBrush AccentSubtleBrush => new SolidColorBrush(GetAccentSubtle(Color.Parse(AccentColor)));

    public static void Load()
    {
        PrimaryColor = Normalize(GuiSettings.PrimaryColor) ?? DefaultPrimaryColor;
        AccentColor = Normalize(GuiSettings.AccentColor) ?? DefaultAccentColor;
        SelectionColor = Normalize(GuiSettings.SelectionColor) ?? DefaultSelectionColor;
        ApplyResources();
    }

    public static void SetColors(string primary, string accent, string? selection = null)
    {
        PrimaryColor = Normalize(primary) ?? DefaultPrimaryColor;
        AccentColor = Normalize(accent) ?? DefaultAccentColor;
        SelectionColor = Normalize(selection) ?? SelectionColor;

        GuiSettings.PrimaryColor = IsSame(PrimaryColor, DefaultPrimaryColor) ? null : PrimaryColor;
        GuiSettings.AccentColor = IsSame(AccentColor, DefaultAccentColor) ? null : AccentColor;
        GuiSettings.SelectionColor = IsSame(SelectionColor, DefaultSelectionColor) ? null : SelectionColor;

        IconService.ClearCache();
        ApplyResources();
        ColorsChanged?.Invoke();
    }

    public static Color GetAccentSubtle(Color accent)
    {
        return IsSame(ToHex(accent), DefaultAccentColor) ? DefaultAccentSubtle : Mix(accent, SurfaceColor, 0.47);
    }

    public static Color GetSelectionSubtle(Color selection)
    {
        return Mix(selection, SurfaceColor, 0.47);
    }

    public static Color GetSelectionTranslucent(Color selection)
    {
        return Color.FromArgb(0x33, selection.R, selection.G, selection.B);
    }

    public static Color GetAccentLight(Color accent)
    {
        return IsSame(ToHex(accent), DefaultAccentColor) ? DefaultAccentLight : Mix(accent, Colors.White, 0.4);
    }

    public static string ToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static void ApplyResources()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        Color primary = Color.Parse(PrimaryColor);
        Color accent = Color.Parse(AccentColor);
        Color selection = Color.Parse(SelectionColor);
        IResourceDictionary resources = app.Resources;

        resources["PrimaryBrush"] = new SolidColorBrush(primary);
        resources["AccentBrush"] = new SolidColorBrush(accent);
        resources["AccentSubtleBrush"] = new SolidColorBrush(GetAccentSubtle(accent));
        resources["AccentLightBrush"] = new SolidColorBrush(GetAccentLight(accent));
        resources["AccentTranslucentBrush"] = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B));
        resources["SelectionBrush"] = new SolidColorBrush(selection);
        resources["SelectionSubtleBrush"] = new SolidColorBrush(GetSelectionSubtle(selection));
        resources["SelectionTranslucentBrush"] = new SolidColorBrush(GetSelectionTranslucent(selection));

        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.15);
        resources["SystemAccentColorDark2"] = Mix(accent, Colors.Black, 0.3);
        resources["SystemAccentColorDark3"] = Mix(accent, Colors.Black, 0.45);
        resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.15);
        resources["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.3);
        resources["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.45);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromRgb(
            (byte) Math.Round(from.R + (to.R - from.R) * amount),
            (byte) Math.Round(from.G + (to.G - from.G) * amount),
            (byte) Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static string? Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || !Color.TryParse(hex, out Color color))
        {
            return null;
        }

        return ToHex(color);
    }

    private static bool IsSame(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
