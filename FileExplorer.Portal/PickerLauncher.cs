using System.Diagnostics;

namespace FileExplorer.Portal;

public sealed class PickerLauncher
{
    private const string PickerOverrideVariable = "FERRET_PICKER";

    private readonly string _pickerPath;

    public PickerLauncher(string pickerPath)
    {
        _pickerPath = pickerPath;
    }

    public static string? FindPicker()
    {
        string? overridePath = Environment.GetEnvironmentVariable(PickerOverrideVariable);
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        string baseDir = AppContext.BaseDirectory;
        foreach (string sibling in new[] { "ferret", "FileExplorer.Gui" })
        {
            string candidate = Path.Combine(baseDir, sibling);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin";
        return pathVariable
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, "ferret"))
            .FirstOrDefault(File.Exists);
    }

    public async Task<List<string>?> RunAsync(IReadOnlyList<string> arguments, CancellationToken token)
    {
        ProcessStartInfo psi = new()
        {
            FileName = _pickerPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(psi);
        if (process == null)
        {
            return null;
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }

            return null;
        }

        if (process.ExitCode != 0)
        {
            return null;
        }

        List<string> paths = (await output)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathRooted)
            .ToList();
        return paths.Count > 0 ? paths : null;
    }
}
