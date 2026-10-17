using System.Diagnostics;
using FileExplorer.Cli.Utilities;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Services;
using InputLib;
using InputLib.EventArgs;
using TuiLib;
using TuiLib.Components;
using TuiLib.Controls;

namespace FileExplorer.Cli.Keybinds;

public enum OpenWithRowKind
{
    App,
    Custom,
    Checkbox,
    Info,
}

public sealed class OpenWithRow
{
    public required OpenWithRowKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string? DesktopId { get; init; }
    public bool IsDefault { get; init; }
    public bool IsSelectable => Kind != OpenWithRowKind.Info;
}

public class OpenWithKeybind : Keybind
{
    private const int PopupWidth = 60;

    private readonly object _renderLock = new();
    private TuiListBox<OpenWithRow> _list = new();
    private TuiBorder _border = new();
    private string _filePath = "";
    private string _typeLabel = "";
    private bool _always;

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (Context.Menu.SelectedItem?.Value?.Entry is not FileEntry file)
        {
            return;
        }

        _filePath = file.FullPath;
        _always = false;
        string extension = Path.GetExtension(_filePath);
        _typeLabel = string.IsNullOrEmpty(extension) ? "files of this type" : $"{extension} files";

        ShowPopup();
        Task.Run(LoadApplications);
    }

    private void ShowPopup()
    {
        Program.PauseRendering = true;
        Program.ModalKeyHandler = HandleModalKey;
        Program.ModalResizeHandler = Layout;

        _list = new TuiListBox<OpenWithRow>
        {
            AnchorPoint = AnchorPoint.Center,
            ScrollBarColor = Color.Gray,
            UseDefaultMarker = false,
            ScrollOvershoot = 1,
        };
        _list.ItemRenderer = RenderRow;

        _border = new TuiBorder
        {
            Title = new TuiLabel($" Open “{Path.GetFileName(_filePath)}” with ", Color.White),
            Color = Color.White,
            Child = _list,
        };

        SetRows([new() { Kind = OpenWithRowKind.Info, Text = "Loading applications…" }]);
    }

    private void LoadApplications()
    {
        string mime = MimeService.GetMimeType(_filePath);
        string? defaultApp = MimeService.GetDefaultApplication(mime);
        List<OpenWithRow> rows = MimeService.GetApplicationsForMimeType(mime)
            .Select(id => new OpenWithRow
            {
                Kind = OpenWithRowKind.App,
                DesktopId = id,
                Text = MimeService.GetDesktopAppName(id),
                IsDefault = id == defaultApp,
            })
            .OrderByDescending(row => row.IsDefault)
            .ThenBy(row => row.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(new() { Kind = OpenWithRowKind.Info, Text = $"No applications registered for {mime}" });
        }

        rows.Add(new() { Kind = OpenWithRowKind.Custom, Text = "Other application…" });
        rows.Add(new() { Kind = OpenWithRowKind.Info });
        rows.Add(new() { Kind = OpenWithRowKind.Checkbox });
        rows.Add(new() { Kind = OpenWithRowKind.Info, Text = "Enter open · Space toggle · Esc cancel" });

        SetRows(rows);
    }

    private void SetRows(List<OpenWithRow> rows)
    {
        lock (_renderLock)
        {
            _list.ClearItems();
            _list.AddItemRange(rows);
            _list.SelectedIndex = rows.FindIndex(row => row.IsSelectable);
            Layout();
        }
    }

    private void Layout()
    {
        lock (_renderLock)
        {
            int height = Math.Min(Console.WindowHeight - 4, Math.Max(_list.GetItemCount(), 1));
            _list.MaxWidth = Math.Min(Console.WindowWidth - 4, PopupWidth);
            _list.MaxHeight = height;
            _list.Height = height;
            _list.ViewRange = height;
            _list.X = Console.WindowWidth / 2;
            _list.Y = Console.WindowHeight / 2;

            Program.Renderer.ClearScreen();
            Render();
        }
    }

    private void Render()
    {
        lock (_renderLock)
        {
            List<RenderPart> parts = Program.GetExplorerRenderParts();
            parts.AddRange(_border.Render());
            Program.Renderer.Render(parts);
        }
    }

    private void RenderPopupOnly()
    {
        lock (_renderLock)
        {
            Program.Renderer.Render(_border.Render().ToList());
        }
    }

    private IEnumerable<RenderPart> RenderRow(OpenWithRow row)
    {
        int width = Math.Max(_list.MaxWidth - 1, 0);
        bool isSelected = _list.SelectedIndex >= 0 && _list.SelectedIndex < _list.Items.Count && _list.Items[_list.SelectedIndex].Value == row;

        string text = row.Kind switch
        {
            OpenWithRowKind.App => $" {(isSelected ? "›" : " ")} {row.Text}{(row.IsDefault ? "  (default)" : "")}",
            OpenWithRowKind.Custom => $" {(isSelected ? "›" : " ")} {row.Text}",
            OpenWithRowKind.Checkbox => $" {(isSelected ? "›" : " ")} [{(_always ? "x" : " ")}] Always open {_typeLabel} with the selected app",
            _ => $"   {row.Text}",
        };

        if (text.Length > width)
        {
            text = text[..Math.Max(width - 1, 0)] + "…";
        }

        Color foreground = row.Kind switch
        {
            OpenWithRowKind.Info => Color.Gray,
            OpenWithRowKind.Checkbox => _always ? new Color(120, 180, 255) : Color.LightGray,
            _ when row.IsDefault => new Color(120, 180, 255),
            _ => Color.White,
        };

        yield return new RenderPart
        {
            Text = text.PadRight(width),
            Foreground = isSelected ? Color.Black : foreground,
            Background = isSelected ? Color.White : Color.Reset,
            ZIndex = 10,
        };
    }

    private void HandleModalKey(KeyDownEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            ClosePopup();
            return;
        }

        if (args.Key == Key.ArrowUp || args.Key == Key.ArrowDown)
        {
            MoveSelection(args.Key == Key.ArrowUp ? -1 : 1);
            return;
        }

        if (args.Key == Key.Space || args.Key == Key.Tab)
        {
            _always = !_always;
            RenderPopupOnly();
            return;
        }

        if (args.Key != Key.Enter)
        {
            return;
        }

        OpenWithRow? row = _list.SelectedItem?.Value;
        if (row == null)
        {
            return;
        }

        if (row.Kind == OpenWithRowKind.Checkbox)
        {
            _always = !_always;
            RenderPopupOnly();
            return;
        }

        if (row.Kind == OpenWithRowKind.App && row.DesktopId != null)
        {
            ClosePopup();
            Launch(row.DesktopId, true);
            return;
        }

        if (row.Kind == OpenWithRowKind.Custom)
        {
            ClosePopup();
            string? command = Context.Input("Open with command: ");
            if (!string.IsNullOrWhiteSpace(command))
            {
                Launch(command.Trim(), false);
            }
        }
    }

    private void MoveSelection(int direction)
    {
        lock (_renderLock)
        {
            int index = _list.SelectedIndex + direction;
            while (index >= 0 && index < _list.Items.Count && !_list.Items[index].Value!.IsSelectable)
            {
                index += direction;
            }

            if (index < 0 || index >= _list.Items.Count)
            {
                return;
            }

            _list.SelectedIndex = index;
            if (index < _list.ViewIndex)
            {
                _list.ViewIndex = index;
            }
            else if (index >= _list.ViewIndex + _list.ViewRange)
            {
                _list.ViewIndex = index - _list.ViewRange + 1;
            }

            RenderPopupOnly();
        }
    }

    private void Launch(string program, bool isDesktopId)
    {
        string path = _filePath;
        bool always = _always;

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = isDesktopId ? "gtk-launch" : program,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (isDesktopId)
            {
                psi.ArgumentList.Add(program);
            }

            psi.ArgumentList.Add(path);
            Process.Start(psi);
            Program.Sidebar.StatusText = "";
        }
        catch (Exception ex)
        {
            Program.Sidebar.StatusText = $"Could not open with {program}: {ex.Message}";
            return;
        }

        if (!always)
        {
            return;
        }

        Task.Run(() =>
        {
            MimeService.SetDefaultApplication(path, program);
            Program.Sidebar.StatusText = $"{_typeLabel} now open with {(isDesktopId ? MimeService.GetDesktopAppName(program) : program)}";
        });
    }

    private void ClosePopup()
    {
        Program.ModalKeyHandler = null;
        Program.ModalResizeHandler = null;
        Program.PauseRendering = false;
        Program.Renderer.ClearScreen();
    }
}
