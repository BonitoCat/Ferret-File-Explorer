using System.ComponentModel;
using System.Diagnostics;

namespace FileExplorer.Core.Utilities;

public static class ProcessUtils
{
    public static void TryKillTree(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or AggregateException or NotSupportedException)
        {
        }
    }

    public static void TryLowerPriority(Process process)
    {
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
}
