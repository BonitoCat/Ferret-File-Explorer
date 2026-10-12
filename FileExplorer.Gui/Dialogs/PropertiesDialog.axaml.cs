using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FileExplorer.Gui.ViewModels;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class PropertiesDialog : Window
{
    public PropertiesDialog()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            CloseButton.Focus();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }, Tunnel);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    public PropertiesDialog(FileItemViewModel item) : this()
    {
        NameLabel.Text = item.Name;
        TypeLabel.Text = item.TypeString;
        SizeValue.Text = string.IsNullOrEmpty(item.SizeString) ? "-" : item.SizeString;
        LocationValue.Text = Path.GetDirectoryName(item.FullPath) ?? "/";

        ItemIcon.Source = item.Icon;

        try
        {
            if (item.IsDirectory)
            {
                DirectoryInfo dir = new(item.FullPath);
                ModifiedValue.Text = dir.LastWriteTime.ToString("F");
                AccessedValue.Text = dir.LastAccessTime.ToString("F");
                PermissionsValue.Text = dir.Attributes.ToString();

                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    string owner = File.GetUnixFileMode(item.FullPath).ToString();
                    OwnerValue.Text = owner;
                }
                else
                {
                    OwnerValue.Text = "-";
                }
            }
            else
            {
                FileInfo file = new(item.FullPath);
                ModifiedValue.Text = file.LastWriteTime.ToString("F");
                AccessedValue.Text = file.LastAccessTime.ToString("F");
                PermissionsValue.Text = file.Attributes.ToString();

                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    string owner = File.GetUnixFileMode(item.FullPath).ToString();
                    OwnerValue.Text = owner;
                }
                else
                {
                    OwnerValue.Text = "-";
                }
            }
        }
        catch
        {
            ModifiedValue.Text = item.ModifiedString;
            AccessedValue.Text = "-";
            PermissionsValue.Text = "-";
            OwnerValue.Text = "-";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
