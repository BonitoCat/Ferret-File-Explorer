using System.ComponentModel;
using System.Diagnostics;

namespace FileExplorer.Core.Addons;

public sealed class AddonDependency
{
    public static readonly string[] PackageManagers = ["apt", "dnf", "pacman", "zypper"];

    public string Command { get; }
    public string? Package { get; }
    public IReadOnlyDictionary<string, string> Packages { get; }

    public AddonDependency(string command, string? package, IReadOnlyDictionary<string, string> packages)
    {
        Command = command;
        Package = package;
        Packages = packages;
    }

    public string? GetPackage(string? packageManager)
    {
        if (packageManager != null && Packages.TryGetValue(packageManager, out string? package))
        {
            return package;
        }

        return Package;
    }

    public bool IsInstalled()
    {
        return AddonDependencyService.IsCommandAvailable(Command);
    }
}

public sealed record AddonDependencyResult(int ExitCode, string Output)
{
    public const int StartFailedExitCode = -1;

    public bool IsSuccess => ExitCode == 0;
    public bool IsCancelled => ExitCode is 126 or 127;
}

public static class AddonDependencyService
{
    private static readonly Lazy<string?> DetectedPackageManager = new(DetectPackageManager);

    public static string? PackageManager => DetectedPackageManager.Value;

    public static List<AddonDependency> GetMissing(Addon addon)
    {
        return addon.Dependencies.Where(d => !d.IsInstalled()).ToList();
    }

    public static bool IsCommandAvailable(string command)
    {
        if (command.Contains('/'))
        {
            return File.Exists(Addon.ExpandHome(command));
        }

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, command)))
            {
                return true;
            }
        }

        return false;
    }

    public static List<string> GetPackages(IEnumerable<AddonDependency> dependencies)
    {
        return dependencies
            .Select(d => d.GetPackage(PackageManager))
            .OfType<string>()
            .SelectMany(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static List<string>? BuildInstallCommand(IReadOnlyList<string> packages)
    {
        if (PackageManager == null || packages.Count == 0)
        {
            return null;
        }

        List<string> command = PackageManager switch
        {
            "apt" => ["apt-get", "install", "-y"],
            "dnf" => ["dnf", "install", "-y"],
            "pacman" => ["pacman", "-S", "--needed", "--noconfirm"],
            "zypper" => ["zypper", "--non-interactive", "install"],
            _ => [],
        };
        if (command.Count == 0)
        {
            return null;
        }

        command.AddRange(packages);
        if (Environment.UserName != "root")
        {
            command.Insert(0, "pkexec");
        }

        return command;
    }

    public static string GetManualCommand(IReadOnlyList<string> packages)
    {
        string install = PackageManager switch
        {
            "apt" => "sudo apt install",
            "dnf" => "sudo dnf install",
            "pacman" => "sudo pacman -S",
            "zypper" => "sudo zypper install",
            _ => "",
        };

        return install.Length == 0 ? "" : $"{install} {string.Join(' ', packages)}";
    }

    public static async Task<AddonDependencyResult> InstallAsync(IReadOnlyList<string> command, CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = command[0],
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in command.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DEBIAN_FRONTEND"] = "noninteractive";

        using Process process = new() { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new(AddonDependencyResult.StartFailedExitCode, ex.Message);
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new(process.ExitCode, $"{await output}{await error}".Trim());
    }

    private static string? DetectPackageManager()
    {
        foreach (string manager in AddonDependency.PackageManagers)
        {
            string executable = manager == "apt" ? "apt-get" : manager;
            if (IsCommandAvailable(executable))
            {
                return manager;
            }
        }

        return null;
    }
}
