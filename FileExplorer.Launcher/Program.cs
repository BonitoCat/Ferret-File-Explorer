using System.Diagnostics;
using FileExplorer.Startup;

string localGui = Path.Combine(AppContext.BaseDirectory, "ferret-gui");
string gui = File.Exists(localGui) ? localGui : "/usr/lib/ferret/ferret-gui";

if (LaunchRequest.IsPicker(args) || args.Contains("--help") || args.Contains("-h"))
{
    using Process? picker = StartGui(args);
    if (picker == null) return 1;
    await picker.WaitForExitAsync();
    return picker.ExitCode;
}

LaunchRequest request = new() { Args = args };
if (await request.TrySendAsync()) return 0;
if (args.Contains("--quit")) return 0;

// The GUI takes the session lock, so concurrent launchers can safely start it.
using Process? process = StartGui(["--resident"]);
if (process == null) return 1;
Stopwatch startup = Stopwatch.StartNew();
while (startup.Elapsed < TimeSpan.FromSeconds(30))
{
    if (await request.TrySendAsync(1000)) return 0;
    await Task.Delay(25);
}

Console.Error.WriteLine("Ferret did not become ready within 30 seconds.");
return 1;

Process? StartGui(string[] arguments)
{
    ProcessStartInfo info = new(gui) { UseShellExecute = false };
    foreach (string argument in arguments) info.ArgumentList.Add(argument);
    try
    {
        return Process.Start(info);
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
    {
        Console.Error.WriteLine($"Could not start Ferret: {ex.Message}");
        return null;
    }
}
