using System.Globalization;

namespace FileExplorer.Core.Addons;

public enum AddonFieldType
{
    Text,
    Password,
    Number,
    Bool,
    Choice,
    File,
    Folder,
}

public sealed class AddonField
{
    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
    public AddonFieldType Type { get; }
    public string Default { get; }
    public IReadOnlyList<string> Choices { get; }
    public double? Min { get; }
    public double? Max { get; }

    public AddonField(string id, string label, string description, AddonFieldType type, string? defaultValue, IReadOnlyList<string>? choices = null, double? min = null, double? max = null)
    {
        Id = id;
        Label = label;
        Description = description;
        Type = type;
        Choices = choices ?? [];
        Min = min;
        Max = max;
        Default = "";
        Default = Normalize(defaultValue);
    }

    public string Normalize(string? value)
    {
        string text = value?.Trim() ?? "";
        switch (Type)
        {
            case AddonFieldType.Bool:
                if (text.Length == 0)
                {
                    return Default.Length > 0 ? Default : "false";
                }

                return text.ToLowerInvariant() is "true" or "yes" or "on" or "1" ? "true" : "false";
            case AddonFieldType.Number:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    return Default.Length > 0 ? Default : FormatNumber(Min ?? 0);
                }

                if (Min != null)
                {
                    number = Math.Max(number, Min.Value);
                }

                if (Max != null)
                {
                    number = Math.Min(number, Max.Value);
                }

                return FormatNumber(number);
            case AddonFieldType.Choice:
                string? match = Choices.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
                return match ?? (Default.Length > 0 ? Default : Choices.FirstOrDefault() ?? "");
            case AddonFieldType.File or AddonFieldType.Folder:
                return Addon.ExpandHome(text);
            default:
                return value ?? "";
        }
    }

    public static bool TryParseType(string? value, out AddonFieldType type)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "" or "text" or "string":
                type = AddonFieldType.Text;
                return true;
            case "password" or "secret":
                type = AddonFieldType.Password;
                return true;
            case "number" or "int" or "integer":
                type = AddonFieldType.Number;
                return true;
            case "bool" or "boolean" or "check" or "checkbox":
                type = AddonFieldType.Bool;
                return true;
            case "choice" or "select" or "choose":
                type = AddonFieldType.Choice;
                return true;
            case "file":
                type = AddonFieldType.File;
                return true;
            case "folder" or "directory":
                type = AddonFieldType.Folder;
                return true;
            default:
                type = AddonFieldType.Text;
                return false;
        }
    }

    public static string FormatNumber(double number)
    {
        return number.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
