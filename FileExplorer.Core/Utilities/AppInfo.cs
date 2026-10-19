using System.Reflection;
using System.Runtime.InteropServices;

namespace FileExplorer.Core.Utilities;

public static class AppInfo
{
    public const string Name = "Ferret File Explorer";
    public const string RepositoryUrl = "https://github.com/BonitoCat/Ferret-File-Explorer";
    public const string IssuesUrl = RepositoryUrl + "/issues";

    public static string Version { get; } = ReadVersion();

    public static string RuntimeDescription => $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} · {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    private static string ReadVersion()
    {
        Assembly assembly = typeof(AppInfo).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Split('+')[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
