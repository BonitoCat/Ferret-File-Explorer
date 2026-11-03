using System.Diagnostics;

namespace SecretsLib;

/// <summary>Stores credentials in the current Linux user's Secret Service collection.</summary>
public static class SecretsManager
{
    private static string _application = "org.fe.fileexplorer";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static void Initialize(string application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        _application = application;
    }

    public static void StoreSecret(string key, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        RunAsync(["store", "--label=Ferret File Explorer", "application", _application, "key", key], secret)
            .GetAwaiter().GetResult();
    }

    public static string? LookupSecret(string key)
    {
        // With redirected stdout secret-tool emits the secret without adding a newline.
        return RunAsync(["lookup", "application", _application, "key", key], null, allowMissing: true)
            .GetAwaiter().GetResult();
    }

    public static void ClearSecret(string key)
    {
        RunAsync(["clear", "application", _application, "key", key], null, allowMissing: true)
            .GetAwaiter().GetResult();
    }

    private static async Task<string?> RunAsync(string[] arguments, string? secret, bool allowMissing = false)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Persistent credentials require Linux Secret Service.");

        ProcessStartInfo info = new("secret-tool")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);

        using Process process = Process.Start(info) ?? throw new IOException("Cannot start the credential provider.");
        using CancellationTokenSource timeout = new(Timeout);
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            if (secret != null)
                await process.StandardInput.WriteAsync(secret.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            string result = await output;
            string diagnostic = await error;
            if (process.ExitCode == 0)
                return result;
            if (allowMissing && process.ExitCode == 1 && diagnostic.Length == 0)
                return null;
            // Provider output can contain sensitive details; deliberately do not include it.
            throw new IOException("The system credential provider is unavailable or rejected the operation.");
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("The system credential provider did not respond.");
        }
    }
}
