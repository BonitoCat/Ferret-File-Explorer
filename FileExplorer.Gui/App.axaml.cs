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
using FileLib;
using LoggerLib;

namespace FileExplorer.Gui;

public partial class App : Application
{
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

            string? initialPath = GetInitialPath(desktop.Args);
            desktop.MainWindow = new MainWindow(initialPath);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnTextBoxKeyDown(TextBox textBox, KeyEventArgs e)
    {
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

    private static string? GetInitialPath(string[]? args)
    {
        if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return null;
        }

        string raw = args[0].Trim();
        try
        {
            string resolved = Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) && uri.IsFile
                ? uri.LocalPath
                : Path.GetFullPath(raw);

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
