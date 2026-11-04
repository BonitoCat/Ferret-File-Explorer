using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FileExplorer.Core.Models;
using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using FileLib;
using LoggerLib;
using Avalonia.Threading;

namespace FileExplorer.Gui;

public partial class App : Application
{
    internal ResidentWindowManager? ResidentWindows { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Task.Run(async () =>
        {
            try
            {
                Logger.LogDir = Path.Combine(DirectoryHelper.GetAppDataDirPath(), "fe", "logs");
                Logger.KeepLogs = 20;
                #if DEBUG
                    Logger.LogDebug = true;
                #endif

                Logger.LogFirstChance = false;
                Logger.LogToTerminal = true;
                await Logger.CreateFileAsync();
                await Logger.LogS($"{AppDomain.CurrentDomain.FriendlyName} GUI started");
            }
            catch
            {
            }
        });

        AppPaths.MigrateLegacyConfig();
        BackgroundStartupService.Apply();
        ThemeService.Load();
        InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnTextBoxKeyDown, RoutingStrategies.Tunnel);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            FilePickerService pickerService = new();
            FilePickerOptions? pickerOptions = pickerService.ParseCommandLineArgs(desktop.Args);

            if (pickerOptions != null)
            {
                MainWindow pickerWindow = new(pickerOptions.InitialPath, pickerOptions);
                pickerWindow.Closed += (_, _) =>
                {
                    if (pickerWindow.PickerResult?.IsSuccess == true)
                    {
                        foreach (string path in pickerWindow.PickerResult.SelectedPaths)
                        {
                            Console.Out.WriteLine(path);
                        }

                        Console.Out.Flush();
                        Environment.ExitCode = 0;
                    }
                    else
                    {
                        Environment.ExitCode = 1;
                    }
                };

                desktop.MainWindow = pickerWindow;
                base.OnFrameworkInitializationCompleted();
                return;
            }

            if (Program.ResidentServer != null)
            {
                Environment.SetEnvironmentVariable("DESKTOP_STARTUP_ID", null);
                Environment.SetEnvironmentVariable("XDG_ACTIVATION_TOKEN", null);
                ResidentWindows = new ResidentWindowManager(desktop);
                Program.ResidentServer.SetHandler(async request =>
                    await Dispatcher.UIThread.InvokeAsync(() => ResidentWindows.Handle(request)));
                if (Program.InitialRequest != null) ResidentWindows.Handle(Program.InitialRequest);
                ResidentWindows.SchedulePreparation();
                if (!GuiSettings.RunInBackground && Program.InitialRequest == null)
                {
                    // A launcher always sends a request right after --resident. If that
                    // handshake never arrives, do not leave an invisible process behind.
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (ResidentWindows is { HasOpenWindows: false }) ResidentWindows.Quit();
                    }, TimeSpan.FromSeconds(30));
                }
                desktop.Exit += (_, _) => ResidentWindows.Dispose();
            }
            else
            {
                desktop.MainWindow = new MainWindow(GetInitialPath(desktop.Args));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnTextBoxKeyDown(TextBox textBox, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            textBox.Paste();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Left || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        int start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        if (start == Math.Max(textBox.SelectionStart, textBox.SelectionEnd))
        {
            return;
        }

        textBox.ClearSelection();
        textBox.CaretIndex = start;
        e.Handled = true;
    }

    internal static string? GetInitialPath(string[]? args, string? workingDirectory = null)
    {
        if (args == null || args.Length == 0)
        {
            return null;
        }

        string? argument = null;
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--path" || args[i] == "-p") && i + 1 < args.Length)
            {
                argument = args[++i];
                break;
            }
            if (!args[i].StartsWith("-", StringComparison.Ordinal))
            {
                argument = args[i];
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(argument)) return null;
        string location = LocationResolver.Resolve(argument);
        if (location.Contains("://", StringComparison.Ordinal))
        {
            return location;
        }

        try
        {
            string resolved = Path.GetFullPath(location, workingDirectory ?? Environment.CurrentDirectory);

            if (Directory.Exists(resolved))
            {
                return resolved;
            }

            if (File.Exists(resolved))
            {
                return Path.GetDirectoryName(resolved);
            }
        }
        catch
        {
        }

        return null;
    }
}
