using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using FileExplorer.Core.Services;
using FileExplorer.Gui.Services;

namespace FileExplorer.Gui;

class Program
{
    private static readonly string[] DetachFlags = ["-d", "--detach"];
    private const string DetachScript = "if command -v setsid >/dev/null 2>&1; then exec setsid -f \"$0\" \"$@\" </dev/null >/dev/null 2>&1; fi; nohup \"$0\" \"$@\" </dev/null >/dev/null 2>&1 &";

    [STAThread]
    public static void Main(string[] args)
    {
        if (args != null && (args.Contains("-h") || args.Contains("--help")))
        {
            Console.WriteLine(
                $"""
                
                Usage:
                 {Process.GetCurrentProcess().ProcessName} [OPTIONS] [PATH]
                
                Options:
                 --file-picker, --file-selection      Open as a file picker
                 --folder-picker, --directory         Open as a folder picker
                 --save-picker, --save                Open as a save dialog
                 --multiple, --multi                  Allow selecting multiple items
                 --title <TEXT>, -t <TEXT>            Set dialog title
                 --path <PATH>, -p <PATH>             Set initial directory
                 --name <NAME>                        Set the initial file name (save dialog)
                 --accept-label <TEXT>                Set the confirm button text
                 --filter "<NAME>|<PATTERNS>"         Add a file filter, e.g. "Images|*.png *.jpg"
                 -d, --detach                         Run in the background and give the terminal back
                 -h, --help                           Show help message
                
                """);
            
            return;
        }

        if (args != null && args.Any(DetachFlags.Contains))
        {
            Environment.ExitCode = Detach(args.Where(a => !DetachFlags.Contains(a)).ToArray());
            return;
        }

        StartupWarmup.Start();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
    }

    private static int Detach(string[] args)
    {
        if (new FilePickerService().ParseCommandLineArgs(args) != null)
        {
            Console.Error.WriteLine("--detach can't be used with picker options, because the picked path is printed to the terminal");
            return 2;
        }

        string? executable = Environment.ProcessPath;
        if (executable == null)
        {
            Console.Error.WriteLine("Could not find the Ferret executable to detach");
            return 1;
        }

        List<string> command = [executable];
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet")
        {
            command.Add(typeof(Program).Assembly.Location);
        }

        command.AddRange(args);

        ProcessStartInfo psi = new() { UseShellExecute = false };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = command[0];
            psi.CreateNoWindow = true;
            foreach (string arg in command.Skip(1))
            {
                psi.ArgumentList.Add(arg);
            }
        }
        else
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(DetachScript);
            foreach (string arg in command)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        try
        {
            using Process? process = Process.Start(psi);
            if (!OperatingSystem.IsWindows())
            {
                process?.WaitForExit();
            }

            return 0;
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine($"Could not start Ferret in the background: {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .With(new X11PlatformOptions
                     {
                         WmClass = "fe-file-explorer",
                         EnableMultiTouch = true,
                     });

#if DEBUG
        builder.LogToTrace();
#endif

        return builder;
    }
}
