using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FileExplorer.Core.Addons;

namespace FileExplorer.Gui.Services;

public static class AddonHooks
{
    private static readonly HashSet<string> PendingInstalls = new(StringComparer.Ordinal);

    public static Func<Addon, AddonHook, Task>? Runner { get; set; }

    public static event Action? InstallFinished;

    public static event Action? ServiceRestartRequested;

    public static bool IsInstalling(string addonId)
    {
        return PendingInstalls.Contains(addonId);
    }

    public static void BeginInstall(string addonId)
    {
        PendingInstalls.Add(addonId);
    }

    public static async Task FinishInstallAsync(string addonId, Addon? addon)
    {
        try
        {
            if (addon != null)
            {
                await RunAsync(addon, AddonHook.PostInstall);
            }
        }
        finally
        {
            PendingInstalls.Remove(addonId);
            InstallFinished?.Invoke();
        }
    }

    public static void RequestServiceRestart()
    {
        ServiceRestartRequested?.Invoke();
    }

    public static Task RunAsync(Addon addon, AddonHook hook)
    {
        if (Runner == null || !addon.Hooks.ContainsKey(hook))
        {
            return Task.CompletedTask;
        }

        return Runner(addon, hook);
    }
}
