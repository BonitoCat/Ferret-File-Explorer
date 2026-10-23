using System;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Core.Addons;
using FileExplorer.Core.Services;
using Svg.Skia;

namespace FileExplorer.Gui.Services;

public static class StartupWarmup
{
    private const string WarmupSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><path d=\"M4 4h16v16H4z\" fill=\"#000\"/></svg>";

    public static void Start()
    {
        Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static void Run()
    {
        TryRun(() => _ = AddonService.Instance);
        TryRun(MimeService.WarmUp);
        TryRun(WarmUpSvg);
    }

    private static void WarmUpSvg()
    {
        using SKSvg svg = new();
        svg.FromSvg(WarmupSvg);
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch
        {
        }
    }
}
