using System.Security.Cryptography;
using FileExplorer.Core.Utilities;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FileExplorer.Core.Addons;

public sealed class AddonStoreIndex
{
    public int Version { get; set; }
    public List<AddonStoreEntry> Addons { get; set; } = new();
}

public sealed class AddonStoreEntry
{
    public string? Id { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Website { get; set; }
    public string? Icon { get; set; }
    public string? Download { get; set; }
    public string? Sha256 { get; set; }
    public long Size { get; set; }
    public Dictionary<string, string>? Name { get; set; }
    public Dictionary<string, string>? Description { get; set; }

    public string GetName(string languageCode)
    {
        string name = Localize(Name, languageCode);
        return name.Length > 0 ? name : Id ?? "";
    }

    public string GetDescription(string languageCode)
    {
        return Localize(Description, languageCode);
    }

    private static string Localize(Dictionary<string, string>? values, string languageCode)
    {
        if (values == null || values.Count == 0)
        {
            return "";
        }

        foreach (string language in Addon.GetLanguageChain(languageCode))
        {
            foreach (KeyValuePair<string, string> entry in values)
            {
                if (string.Equals(entry.Key, language, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    return entry.Value.Trim();
                }
            }
        }

        return values.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
    }
}

public sealed record AddonStoreCatalog(IReadOnlyList<AddonStoreEntry> Entries, bool IsOffline);

public sealed class AddonStoreService
{
    public const string RepositoryUrl = "https://raw.githubusercontent.com/BonitoCat/Ferret-Addons/main/";
    public const string IndexFileName = "index.yaml";
    public const int SupportedIndexVersion = 1;

    private const long MaxDownloadBytes = 100L * 1024 * 1024;
    private const int BufferSize = 81920;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] IconExtensions = [".png", ".svg", ".jpg", ".jpeg"];

    private static readonly Lazy<AddonStoreService> LazyInstance = new(() => new());

    public static AddonStoreService Instance => LazyInstance.Value;

    private static readonly IDeserializer IndexDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly HttpClient _client;
    private readonly string _cacheDir;

    public AddonStoreService()
    {
        _client = new() { Timeout = RequestTimeout };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Ferret-AddonStore/1.0");
        _cacheDir = AppPaths.GetCacheDir("addon-store");
    }

    public async Task<AddonStoreCatalog> FetchCatalogAsync(CancellationToken token)
    {
        string cachePath = Path.Combine(_cacheDir, IndexFileName);
        try
        {
            string yaml = await _client.GetStringAsync(BuildUrl(IndexFileName), token);
            List<AddonStoreEntry> entries = ParseIndex(yaml);
            TryWriteCache(cachePath, yaml);
            return new(entries, false);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or YamlException or AddonInstallException)
        {
            List<AddonStoreEntry>? cached = TryReadCache(cachePath);
            if (cached == null)
            {
                throw new AddonInstallException("AddonStoreErrorUnavailable", ex.Message);
            }

            return new(cached, true);
        }
    }

    public async Task<string?> GetIconPathAsync(AddonStoreEntry entry, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(entry.Icon) || string.IsNullOrWhiteSpace(entry.Id))
        {
            return null;
        }

        string extension = Path.GetExtension(entry.Icon).ToLowerInvariant();
        if (!IconExtensions.Contains(extension))
        {
            return null;
        }

        string iconsDir = AppPaths.GetCacheDir(Path.Combine("addon-store", "icons"));
        string hash = (entry.Sha256 ?? "").Length >= 12 ? entry.Sha256![..12] : entry.Version ?? "";
        string path = Path.Combine(iconsDir, SanitizeFileName($"{entry.Id}-{hash}{extension}"));
        if (File.Exists(path))
        {
            return path;
        }

        try
        {
            byte[] data = await _client.GetByteArrayAsync(BuildUrl(entry.Icon), token);
            await File.WriteAllBytesAsync(path, data, token);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or AddonInstallException)
        {
            return null;
        }
    }

    public async Task<string> DownloadAsync(AddonStoreEntry entry, IProgress<double>? progress, CancellationToken token)
    {
        string id = entry.Id ?? "";
        string expectedHash = (entry.Sha256 ?? "").Trim();
        long limit = entry.Size > 0 ? entry.Size : MaxDownloadBytes;
        string downloadsDir = AppPaths.GetCacheDir(Path.Combine("addon-store", "downloads"));
        string path = Path.Combine(downloadsDir, $"{Guid.NewGuid():N}.zip");

        try
        {
            using HttpResponseMessage response = await _client.GetAsync(BuildUrl(entry.Download), HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            await using Stream source = await response.Content.ReadAsStreamAsync(token);
            await using (FileStream target = File.Create(path))
            {
                using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[BufferSize];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    total += read;
                    if (total > limit || total > MaxDownloadBytes)
                    {
                        throw new AddonInstallException("AddonStoreErrorSizeMismatch", id);
                    }

                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), token);
                    progress?.Report(entry.Size > 0 ? (double) total / entry.Size : -1);
                }

                if (entry.Size > 0 && total != entry.Size)
                {
                    throw new AddonInstallException("AddonStoreErrorSizeMismatch", id);
                }

                string actualHash = Convert.ToHexString(sha.GetHashAndReset());
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new AddonInstallException("AddonStoreErrorHashMismatch", id);
                }
            }

            string installId = AddonService.Instance.GetInstallId(path);
            if (installId != id)
            {
                throw new AddonInstallException("AddonStoreErrorIdMismatch", installId);
            }

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException || (ex is TaskCanceledException && !token.IsCancellationRequested))
        {
            DeleteDownload(path);
            throw new AddonInstallException("AddonStoreErrorDownloadFailed", ex.Message);
        }
        catch
        {
            DeleteDownload(path);
            throw;
        }
    }

    public void DeleteDownload(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public static int CompareVersions(string? left, string? right)
    {
        string[] leftParts = SplitVersion(left);
        string[] rightParts = SplitVersion(right);
        int count = Math.Max(leftParts.Length, rightParts.Length);
        for (int i = 0; i < count; i++)
        {
            string a = i < leftParts.Length ? leftParts[i] : "0";
            string b = i < rightParts.Length ? rightParts[i] : "0";
            int result = long.TryParse(a, out long numberA) && long.TryParse(b, out long numberB)
                ? numberA.CompareTo(numberB)
                : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private static string[] SplitVersion(string? version)
    {
        return (version ?? "").Trim().TrimStart('v', 'V').Split(['.', '-', '+'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static List<AddonStoreEntry> ParseIndex(string yaml)
    {
        AddonStoreIndex? index = IndexDeserializer.Deserialize<AddonStoreIndex?>(yaml);
        if (index == null)
        {
            throw new AddonInstallException("AddonStoreErrorIndexInvalid", IndexFileName);
        }

        if (index.Version > SupportedIndexVersion)
        {
            throw new AddonInstallException("AddonStoreErrorIndexVersion", index.Version.ToString());
        }

        return (index.Addons ?? new())
            .Where(e => e != null && IsValidId(e.Id) && !string.IsNullOrWhiteSpace(e.Download) && (e.Sha256 ?? "").Trim().Length == 64)
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .ToList();
    }

    private static bool IsValidId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return id == id.Trim() && id == SanitizeFileName(id) && id != "." && id != "..";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }

    private static Uri BuildUrl(string? relativePath)
    {
        string path = (relativePath ?? "").Trim();
        string[] segments = path.Split('/');
        if (path.Length == 0 || path.Contains('\\') || path.Contains(':') || segments.Any(s => s.Length == 0 || s == "." || s == ".."))
        {
            throw new AddonInstallException("AddonStoreErrorInvalidPath", path);
        }

        return new(RepositoryUrl + string.Join('/', segments.Select(Uri.EscapeDataString)));
    }

    private static List<AddonStoreEntry>? TryReadCache(string cachePath)
    {
        try
        {
            return File.Exists(cachePath) ? ParseIndex(File.ReadAllText(cachePath)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteCache(string cachePath, string yaml)
    {
        try
        {
            File.WriteAllText(cachePath, yaml);
        }
        catch
        {
        }
    }
}
