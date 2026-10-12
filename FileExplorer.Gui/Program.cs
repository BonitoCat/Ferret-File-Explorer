using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;

namespace FileExplorer.Gui;

class Program
{
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
                 --multiple, --multi                  Allow selecting multiple items
                 --title <TEXT>, -t <TEXT>            Set dialog title
                 --path <PATH>, -p <PATH>             Set initial directory
                 -h, --help                           Show help message
                
                """);
            
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
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
