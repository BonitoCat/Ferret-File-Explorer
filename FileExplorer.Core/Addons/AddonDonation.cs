using System.Text.RegularExpressions;

namespace FileExplorer.Core.Addons;

public sealed record AddonDonationProvider(string Id, string Name, IReadOnlyList<Regex> Patterns);

public sealed record AddonDonation(AddonDonationProvider Provider, string Url)
{
    private const string NamePattern = "[a-z0-9_.-]{1,64}";

    public static IReadOnlyList<AddonDonationProvider> Providers { get; } =
    [
        Create("kofi", "Ko-fi", @"https://(www\.)?ko-fi\.com/[a-z0-9_]{1,64}/?"),
        Create("paypal", "PayPal",
            $@"https://(www\.)?paypal\.me/{NamePattern}/?",
            $@"https://(www\.)?paypal\.com/paypalme/{NamePattern}/?",
            @"https://(www\.)?paypal\.com/donate/?\?hosted_button_id=[a-z0-9]{1,32}"),
        Create("patreon", "Patreon", @"https://(www\.)?patreon\.com/(c/)?[a-z0-9_-]{1,64}/?"),
        Create("github-sponsors", "GitHub Sponsors", @"https://github\.com/sponsors/[a-z0-9-]{1,39}/?"),
        Create("buymeacoffee", "Buy Me a Coffee", $@"https://(www\.)?buymeacoffee\.com/{NamePattern}/?"),
        Create("liberapay", "Liberapay", $@"https://(www\.)?liberapay\.com/{NamePattern}/?"),
        Create("opencollective", "Open Collective", @"https://(www\.)?opencollective\.com/[a-z0-9-]{1,64}/?"),
    ];

    public static bool TryParse(string? url, out AddonDonation? donation)
    {
        donation = null;
        string trimmed = url?.Trim() ?? "";
        if (trimmed.Length == 0 || !trimmed.All(char.IsAscii))
        {
            return false;
        }

        AddonDonationProvider? provider = Providers.FirstOrDefault(p => p.Patterns.Any(r => r.IsMatch(trimmed)));
        if (provider == null)
        {
            return false;
        }

        donation = new(provider, trimmed);
        return true;
    }

    private static AddonDonationProvider Create(string id, string name, params string[] patterns)
    {
        List<Regex> regexes = patterns
            .Select(p => new Regex($@"^(?:{p})\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        return new(id, name, regexes);
    }
}
