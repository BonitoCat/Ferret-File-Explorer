using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FileExplorer.Core.Addons;
using FileExplorer.Gui.Services;
using LoggerLib;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class AddonDependencyDialog : Window
{
    private readonly Addon? _addon;
    private readonly List<AddonDependency> _missing = new();
    private readonly List<string> _packages = new();
    private readonly List<string>? _command;
    private readonly string _manualCommand = "";
    private bool _isInstalling;

    public AddonDependencyDialog()
    {
        InitializeComponent();

        Opened += (_, _) => (InstallButton.IsVisible ? InstallButton : SkipButton).Focus();

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || _isInstalling)
            {
                return;
            }

            Close(false);
            e.Handled = true;
        }, Tunnel);

        Closing += (_, e) => e.Cancel = _isInstalling;
    }

    public AddonDependencyDialog(Addon addon, IReadOnlyList<AddonDependency> missing) : this()
    {
        _addon = addon;
        _missing.AddRange(missing);
        _packages.AddRange(AddonDependencyService.GetPackages(missing));
        _command = AddonDependencyService.BuildInstallCommand(_packages);
        _manualCommand = AddonDependencyService.GetManualCommand(_packages);

        string language = LocalizationService.Instance.LanguageCode;
        MessageText.Text = LocalizationService.Format("AddonDependenciesMessage", addon.GetName(language));
        foreach (AddonDependency dependency in missing)
        {
            DependenciesPanel.Children.Add(CreateRow(dependency));
        }

        bool canInstall = _command != null;
        InstallButton.IsVisible = canInstall;
        CopyButton.IsVisible = _manualCommand.Length > 0;
        SkipButton.Content = LocalizationService.Get(canInstall ? "AddonDependenciesSkip" : "Close");
        HintText.Text = canInstall
            ? LocalizationService.Format("AddonDependenciesHint", string.Join(' ', _command!))
            : LocalizationService.Get(AddonDependencyService.PackageManager == null ? "AddonDependenciesNoManager" : "AddonDependenciesManual");
    }

    public static async Task ResolveAsync(Window owner, Addon addon)
    {
        List<AddonDependency> missing = AddonDependencyService.GetMissing(addon);
        if (missing.Count == 0)
        {
            return;
        }

        AddonDependencyDialog dialog = new(addon, missing);
        await dialog.ShowDialog<bool?>(owner);
    }

    private static Control CreateRow(AddonDependency dependency)
    {
        string? package = dependency.GetPackage(AddonDependencyService.PackageManager);
        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
        };

        row.Children.Add(new TextBlock
        {
            Text = dependency.Command,
            FontFamily = new("monospace"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = package == null ? LocalizationService.Get("AddonDependencyNoPackage") : $"→ {package}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse(package == null ? "#E0A040" : "#999999")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_command == null || _addon == null || _isInstalling)
        {
            return;
        }

        SetInstalling(true);
        Logger.LogI($"Installing dependencies for addon {_addon.Id}: {string.Join(' ', _command)}");
        AddonDependencyResult result = await AddonDependencyService.InstallAsync(_command);
        SetInstalling(false);

        if (result.IsCancelled)
        {
            ShowStatus(LocalizationService.Get("AddonDependenciesCancelled"), "", false);
            return;
        }

        List<AddonDependency> stillMissing = _missing
            .Where(d => d.GetPackage(AddonDependencyService.PackageManager) != null && !d.IsInstalled())
            .ToList();
        if (result.IsSuccess && stillMissing.Count == 0)
        {
            AddonLogService.ForgetMissingDependencies(_addon);
            Close(true);
            return;
        }

        string message = result.IsSuccess
            ? LocalizationService.Format("AddonDependenciesStillMissing", string.Join(", ", stillMissing.Select(d => d.Command)))
            : LocalizationService.Format("AddonDependenciesFailed", result.ExitCode);
        ShowStatus(message, result.Output, true);
        AddonLogService.Add(_addon, LocalizationService.Get("AddonLogSourceDependencies"), AddonLogLevel.Error, message, $"{string.Join(' ', _command)}\n\n{result.Output}");
        InstallButton.IsVisible = false;
        SkipButton.Content = LocalizationService.Get("Close");
    }

    private void SetInstalling(bool installing)
    {
        _isInstalling = installing;
        InstallButton.IsEnabled = !installing;
        SkipButton.IsEnabled = !installing;
        CopyButton.IsEnabled = !installing;
        ResultPanel.IsVisible = true;
        BusyBar.IsVisible = installing;
        OutputBox.IsVisible = false;
        StatusText.Foreground = new SolidColorBrush(Color.Parse("#BBBBBB"));
        StatusText.Text = installing ? LocalizationService.Get("AddonDependenciesInstalling") : "";
    }

    private void ShowStatus(string message, string output, bool isError)
    {
        ResultPanel.IsVisible = true;
        BusyBar.IsVisible = false;
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(Color.Parse(isError ? "#FF7B72" : "#BBBBBB"));
        OutputBox.Text = output;
        OutputBox.IsVisible = output.Length > 0;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard == null || _manualCommand.Length == 0)
        {
            return;
        }

        await Clipboard.SetTextAsync(_manualCommand);
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
