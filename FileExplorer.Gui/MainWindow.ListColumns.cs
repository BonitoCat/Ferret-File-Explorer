using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;

namespace FileExplorer.Gui;

public partial class MainWindow
{
    private void InitializeListColumns()
    {
        FileItemViewModel.LoadsMetadata = ViewModel.Columns.NeedsMetadata;
        ViewModel.Columns.VisibilityChanged += OnListColumnsChanged;
    }

    private void OnListColumnsChanged()
    {
        bool needsMetadata = ViewModel.Columns.NeedsMetadata;
        if (needsMetadata == FileItemViewModel.LoadsMetadata)
        {
            return;
        }

        FileItemViewModel.LoadsMetadata = needsMetadata;
        foreach (FileItemViewModel item in ViewModel.Panes.SelectMany(p => p.Items))
        {
            item.RefreshMetadata();
        }
    }

    private void OnColumnMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        menu.Items.Clear();
        foreach (ListColumn column in ViewModel.Columns.All)
        {
            MenuItem item = new()
            {
                Header = LocalizationService.Get(column.TitleKey),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = column.IsVisible,
                StaysOpenOnClick = true,
            };
            item.Click += (_, _) =>
            {
                column.IsVisible = !column.IsVisible;
                item.IsChecked = column.IsVisible;
            };
            menu.Items.Add(item);
        }
    }
}
