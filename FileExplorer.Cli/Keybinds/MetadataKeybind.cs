using System.Diagnostics;
using FileExplorer.Cli.Context;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;
using InputLib;
using InputLib.EventArgs;
using TuiLib;
using TuiLib.Components;
using TuiLib.Controls;
using TuiLib.Rendering;

namespace FileExplorer.Cli.Keybinds;

public class MetadataKeybind : Keybind
{
    private readonly object _renderLock = new();
    private readonly Dictionary<string, List<(string Key, string Value)>> _sections = new();

    private TuiListBox<TuiLabel> _list = new();
    private TuiBorder _border = new();
    private string _filePath = "";
    private CancellationTokenSource? _scanCts;
    private const int PopupWidth = 64;

    public override void OnKeyDown(KeyDownEventArgs e)
    {
        if (Context.Menu.SelectedItem?.Value?.Entry == null) return;
        if (Context.Menu.SelectedItem.Value.Entry.FullName == "..") return;

        _filePath = Path.GetFullPath(Context.Menu.SelectedItem.Value.Entry.FullPath);
        if (!File.Exists(_filePath) && !Directory.Exists(_filePath)) return;

        ShowPopup();
    }

    private void ShowPopup()
    {
        Program.PauseRendering = true;
        Program.ModalKeyHandler = HandleModalKey;
        Program.ModalResizeHandler = OnWindowResize;

        int popupWidth = Math.Min(Console.WindowWidth - 4, PopupWidth);
        int popupHeight = 4;

        _list = new TuiListBox<TuiLabel>
        {
            MaxWidth = popupWidth,
            MaxHeight = popupHeight,
            Height = popupHeight,
            ViewRange = popupHeight,
            X = Console.WindowWidth / 2,
            Y = Console.WindowHeight / 2,
            AnchorPoint = AnchorPoint.Center,
            ScrollBarColor = Color.Gray,
            UseDefaultMarker = false,
            ItemRenderer = label =>
            [
                new RenderPart
                {
                    Text = label.Text,
                    Foreground = label.Style.Foreground,
                    Background = label.Style.Background,
                    ZIndex = 10,
                },
            ],
        };

        string titleName = Path.GetFileName(_filePath);
        if (string.IsNullOrEmpty(titleName))
        {
            titleName = _filePath;
        }

        _border = new TuiBorder
        {
            Title = new TuiLabel($" {titleName} ", Color.White),
            Color = Color.White,
            Child = _list,
        };

        _sections.Clear();
        _sections["General"] = [("Loading…", "")];
        _sections["Permissions"] = [("Loading…", "")];
        _sections["Volume"] = [("Loading…", "")];
        _sections["Details"] = [];

        _scanCts?.Cancel();
        _scanCts = new();
        CancellationToken token = _scanCts.Token;

        RebuildList();

        Task.Run(() => LoadGeneralSection(token));
        Task.Run(LoadPermissionsSection);
        Task.Run(LoadVolumeSection);
        Task.Run(LoadTypeSpecificSection);
    }

    private void HandleModalKey(KeyDownEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            ClosePopup();
            return;
        }

        if (args.Key == Key.ArrowDown)
        {
            lock (_renderLock)
            {
                if (_list.GetItemCount() > _list.MaxHeight && _list.Scroll(1))
                {
                    RenderPopupOnly();
                }
            }
            return;
        }

        if (args.Key == Key.ArrowUp)
        {
            lock (_renderLock)
            {
                if (_list.GetItemCount() > _list.MaxHeight && _list.Scroll(-1))
                {
                    RenderPopupOnly();
                }
            }
            return;
        }

        if (args.Key == Key.PageDown)
        {
            lock (_renderLock)
            {
                if (_list.GetItemCount() > _list.MaxHeight && _list.Scroll(5))
                {
                    RenderPopupOnly();
                }
            }
            return;
        }

        if (args.Key == Key.PageUp)
        {
            lock (_renderLock)
            {
                if (_list.GetItemCount() > _list.MaxHeight && _list.Scroll(-5))
                {
                    RenderPopupOnly();
                }
            }
            return;
        }
    }

    private void ClosePopup()
    {
        _scanCts?.Cancel();
        Program.ModalKeyHandler = null;
        Program.ModalResizeHandler = null;
        Program.PauseRendering = false;

        Program.Renderer.ClearScreen();
        Program.RenderExplorer();
    }

    private void OnWindowResize()
    {
        lock (_renderLock)
        {
            int itemCount = _list.GetItemCount();
            int newHeight = Math.Min(Console.WindowHeight - 4, Math.Max(2, itemCount));

            _list.MaxWidth = Math.Min(Console.WindowWidth - 4, PopupWidth);
            _list.MaxHeight = newHeight;
            _list.Height = newHeight;
            _list.ViewRange = newHeight;
            _list.X = Console.WindowWidth / 2;
            _list.Y = Console.WindowHeight / 2;

            Program.Renderer.ClearScreen();
            RenderPopup();
        }
    }

    private void RenderPopup()
    {
        lock (_renderLock)
        {
            List<RenderPart> allParts = Program.GetExplorerRenderParts();
            allParts.AddRange(_border.Render());
            Program.Renderer.Render(allParts);
        }
    }

    private void RenderPopupOnly()
    {
        lock (_renderLock)
        {
            Program.Renderer.Render(_border.Render().ToList());
        }
    }

    private void SetSection(string sectionTitle, List<(string Key, string Value)> rows)
    {
        lock (_renderLock)
        {
            _sections[sectionTitle] = rows;
            RebuildList();
        }
    }

    private void RebuildList()
    {
        lock (_renderLock)
        {
            int prevViewIndex = _list.ViewIndex;
            _list.ClearItems();

            bool isFirst = true;
            foreach ((string sectionTitle, List<(string Key, string Value)> rows) in _sections)
            {
                if (rows.Count == 0)
                {
                    continue;
                }

                if (!isFirst)
                {
                    _list.AddItem(new TuiLabel("", Color.Reset));
                }

                isFirst = false;
                _list.AddItem(new TuiLabel($" ── {sectionTitle} ──", Color.Cyan));

                foreach ((string key, string value) in rows)
                {
                    string padded = string.IsNullOrEmpty(value) ? $"  {key}" : $"  {key,-16} {TruncateValue(value)}";
                    _list.AddItem(new TuiLabel(padded, Color.White));
                }
            }

            int itemCount = _list.GetItemCount();
            int newHeight = Math.Min(Console.WindowHeight - 4, Math.Max(2, itemCount));
            bool sizeChanged = newHeight != _list.MaxHeight || _list.MaxWidth != Math.Min(Console.WindowWidth - 4, PopupWidth);

            _list.MaxWidth = Math.Min(Console.WindowWidth - 4, PopupWidth);
            _list.MaxHeight = newHeight;
            _list.Height = newHeight;
            _list.ViewRange = newHeight;
            _list.X = Console.WindowWidth / 2;
            _list.Y = Console.WindowHeight / 2;
            _list.ViewIndex = Math.Min(prevViewIndex, Math.Max(0, itemCount - newHeight));

            if (sizeChanged)
            {
                RenderPopup();
                return;
            }

            RenderPopupOnly();
        }
    }

    private void LoadGeneralSection(CancellationToken token)
    {
        bool isDirectory = Directory.Exists(_filePath);
        UnixStat? stat = OperatingSystem.IsLinux() ? UnixStat.Load(_filePath) : null;
        List<(string Key, string Value)> head = new();
        List<(string Key, string Value)> dates = new();
        List<(string Key, string Value)> tail = new();

        try
        {
            FileSystemInfo info = isDirectory ? new DirectoryInfo(_filePath) : new FileInfo(_filePath);
            head.Add(("Name", info.Name));
            head.Add(("Location", Path.GetDirectoryName(_filePath) ?? "/"));
            if (info.LinkTarget != null)
            {
                head.Add(("Link target", info.LinkTarget));
            }

            dates.Add(("Created", stat?.Birth?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown"));
            dates.Add(("Modified", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")));
            dates.Add(("Accessed", info.LastAccessTime.ToString("yyyy-MM-dd HH:mm:ss")));

            if (!isDirectory)
            {
                string mime = MimeService.GetMimeType(_filePath);
                head.Insert(1, ("Type", $"{GetFileTypeLabel()} ({mime})"));
                head.Add(("Size", FormatSize(((FileInfo) info).Length)));
                if (stat != null)
                {
                    head.Add(("Size on disk", FormatSize(stat.SizeOnDisk)));
                }

                string? defaultApp = MimeService.GetDefaultApplication(mime);
                tail.Add(("Opens with", defaultApp == null ? "No default application" : MimeService.GetDesktopAppName(defaultApp)));
                SetSection("General", head.Concat(dates).Concat(tail).ToList());
                return;
            }

            head.Insert(1, ("Type", "Folder"));
        }
        catch (Exception ex)
        {
            SetSection("General", [("Error", ex.Message)]);
            return;
        }

        void ShowProgress(FolderScanProgress progress)
        {
            List<(string Key, string Value)> rows = head.ToList();
            rows.Add(("Size", progress.Completed ? FormatSize(progress.Bytes) : $"{FormatSize(progress.Bytes)} …"));
            rows.Add(("Contents", $"{Plural(progress.Folders, "folder")}, {Plural(progress.Files, "file")}"));
            if (progress.Completed && progress.Unreadable > 0)
            {
                rows.Add(("Unreadable", $"{Plural(progress.Unreadable, "folder")} (size may be larger)"));
            }

            SetSection("General", rows.Concat(dates).ToList());
        }

        ShowProgress(new(0, 0, 0, 0, false));
        FolderScanner.ScanAsync(_filePath, progress =>
        {
            if (!token.IsCancellationRequested)
            {
                ShowProgress(progress);
            }
        }, token).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private void LoadPermissionsSection()
    {
        List<(string Key, string Value)> rows = new();
        bool isDirectory = Directory.Exists(_filePath);
        UnixStat? stat = OperatingSystem.IsLinux() ? UnixStat.Load(_filePath) : null;

        if (stat == null)
        {
            SetSection("Permissions", [("Unavailable", "")]);
            return;
        }

        rows.Add(("Owner", stat.Uid == UnixStat.CurrentUid ? $"{stat.Owner} (you)" : stat.Owner));
        rows.Add(("Group", stat.Group));
        rows.Add(("Owner access", DescribeAccess(stat.Mode >> 6, isDirectory)));
        rows.Add(("Group access", DescribeAccess(stat.Mode >> 3, isDirectory)));
        rows.Add(("Others access", DescribeAccess(stat.Mode, isDirectory)));
        rows.Add(("Mode", $"{Convert.ToString(stat.Mode, 8).PadLeft(4, '0')}  {UnixStat.FormatMode(stat.Mode, isDirectory)}"));

        List<string> access = new();
        if (FileUtils.HasAccess(_filePath, true, false, false))
        {
            access.Add(isDirectory ? "list" : "read");
        }

        if (FileUtils.HasAccess(_filePath, false, true, false))
        {
            access.Add(isDirectory ? "create/delete" : "write");
        }

        if (FileUtils.HasAccess(_filePath, false, false, true))
        {
            access.Add(isDirectory ? "open" : "execute");
        }

        rows.Add(("Your access", access.Count == 0 ? "None" : string.Join(", ", access)));
        if (!isDirectory)
        {
            rows.Add(("Executable", (stat.Mode & 0x49) != 0 ? "Yes" : "No"));
        }

        List<string> special = new();
        if ((stat.Mode & 0x800) != 0)
        {
            special.Add("setuid");
        }

        if ((stat.Mode & 0x400) != 0)
        {
            special.Add("setgid");
        }

        if ((stat.Mode & 0x200) != 0)
        {
            special.Add("sticky");
        }

        if (special.Count > 0)
        {
            rows.Add(("Special bits", string.Join(", ", special)));
        }

        SetSection("Permissions", rows);
    }

    private void LoadVolumeSection()
    {
        UnixStat? stat = OperatingSystem.IsLinux() ? UnixStat.Load(_filePath) : null;
        if (stat == null || string.IsNullOrEmpty(stat.MountPoint))
        {
            SetSection("Volume", []);
            return;
        }

        List<(string Key, string Value)> rows =
        [
            ("Mount point", stat.MountPoint),
            ("Filesystem", stat.FileSystemType),
        ];

        try
        {
            DriveInfo drive = new(stat.MountPoint);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                long used = drive.TotalSize - drive.AvailableFreeSpace;
                rows.Add(("Free", $"{FileUtils.FormatSize(drive.AvailableFreeSpace)} of {FileUtils.FormatSize(drive.TotalSize)} ({used * 100 / drive.TotalSize}% used)"));
            }
        }
        catch
        {
        }

        SetSection("Volume", rows);
    }

    private static string DescribeAccess(int bits, bool isDirectory)
    {
        bool read = (bits & 4) != 0;
        bool write = (bits & 2) != 0;
        bool execute = (bits & 1) != 0;

        if (isDirectory)
        {
            return (read, write, execute) switch
            {
                (false, false, false) => "None",
                (true, false, false) => "List files only",
                (true, false, true) => "Access files",
                (true, true, true) => "Create and delete files",
                _ => $"Custom ({(read ? 'r' : '-')}{(write ? 'w' : '-')}{(execute ? 'x' : '-')})",
            };
        }

        string basic = (read, write) switch
        {
            (false, false) => "None",
            (true, false) => "Read-only",
            (true, true) => "Read and write",
            _ => "Write-only",
        };

        return execute ? $"{basic}, execute" : basic;
    }

    private static string Plural(long count, string noun)
    {
        return $"{count:N0} {noun}{(count == 1 ? "" : "s")}";
    }

    private void LoadTypeSpecificSection()
    {
        string ext = Path.GetExtension(_filePath).ToLowerInvariant();
        List<(string Key, string Value)> rows = new();

        try
        {
            if (FileUtils.IsFileExecutable(_filePath))
            {
                rows.Add(("Needs terminal", FileExecutionService.RequiresTerminal(_filePath) ? "Yes" : "No"));

                if (OperatingSystem.IsLinux())
                {
                    string lddOutput = RunProcess("ldd", _filePath);
                    int libCount = lddOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
                    rows.Add(("Linked libs", libCount.ToString()));

                    bool isGui = lddOutput.Contains("libX11")
                        || lddOutput.Contains("libwayland")
                        || lddOutput.Contains("libQt")
                        || lddOutput.Contains("libgtk");

                    rows.Add(("GUI binary", isGui ? "Yes" : "No"));
                }
            }
            else if (IsImageExtension(ext))
            {
                LoadImageMetadata(rows);
            }
            else if (IsMediaExtension(ext))
            {
                LoadMediaMetadata(rows);
            }
            else if (IsTextExtension(ext))
            {
                LoadTextMetadata(rows);
            }
            else if (IsArchiveExtension(ext))
            {
                LoadArchiveMetadata(rows);
            }
        }
        catch (Exception ex)
        {
            rows.Add(("Error", ex.Message));
        }

        if (rows.Count == 0) return;

        SetSection("Details", rows);
    }

    private void LoadImageMetadata(List<(string Key, string Value)> rows)
    {
        string ext = Path.GetExtension(_filePath).ToLowerInvariant();
        (int width, int height) = ReadImageDimensions(ext);

        if (width > 0)
        {
            rows.Add(("Resolution", $"{width} x {height}"));
        }

        string exif = RunProcess("exiftool", $"-Make -Model -DateTimeOriginal -GPSLatitude -GPSLongitude -s3 \"{_filePath}\"");
        if (!string.IsNullOrWhiteSpace(exif))
        {
            string[] lines = exif.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string[] labels = ["Camera make", "Camera model", "Taken", "GPS lat", "GPS lon"];

            for (int i = 0; i < Math.Min(lines.Length, labels.Length); i++)
            {
                rows.Add((labels[i], lines[i].Trim()));
            }
        }
    }

    private void LoadMediaMetadata(List<(string Key, string Value)> rows)
    {
        string duration = RunProcess("ffprobe", $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{_filePath}\"");

        if (!string.IsNullOrWhiteSpace(duration) && double.TryParse(duration.Trim(), out double secs))
        {
            rows.Add(("Duration", TimeSpan.FromSeconds(secs).ToString(@"hh\:mm\:ss")));
        }

        string info = RunProcess("ffprobe", $"-v error -select_streams v:0 -show_entries stream=codec_name,width,height,bit_rate -of default=noprint_wrappers=1 \"{_filePath}\"");

        foreach (string line in info.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('=');
            if (parts.Length != 2)
            {
                continue;
            }

            rows.Add((parts[0].Trim() switch
            {
                "codec_name" => "Codec",
                "width" => "Width",
                "height" => "Height",
                "bit_rate" => "Bitrate",
                _ => parts[0].Trim(),
            }, parts[1].Trim()));
        }
    }

    private void LoadTextMetadata(List<(string Key, string Value)> rows)
    {
        string[] lines = File.ReadAllLines(_filePath);
        int wordCount = lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        rows.Add(("Lines", lines.Length.ToString()));
        rows.Add(("Words", wordCount.ToString()));
        rows.Add(("Encoding", DetectEncoding(_filePath)));
    }

    private void LoadArchiveMetadata(List<(string Key, string Value)> rows)
    {
        string ext = Path.GetExtension(_filePath).ToLowerInvariant();
        string output = ext switch
        {
            ".zip" => RunProcess("unzip", $"-l \"{_filePath}\""),
            ".tar" => RunProcess("tar", $"-tf \"{_filePath}\""),
            ".gz" => RunProcess("tar", $"-tzf \"{_filePath}\""),
            ".bz2" => RunProcess("tar", $"-tjf \"{_filePath}\""),
            ".xz" => RunProcess("tar", $"-tJf \"{_filePath}\""),
            ".7z" => RunProcess("7z", $"l \"{_filePath}\""),
            _ => "",
        };

        if (!string.IsNullOrWhiteSpace(output))
        {
            int fileCount = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            rows.Add(("Files inside", fileCount.ToString()));
        }
    }

    private static string DetectEncoding(string path)
    {
        byte[] bom = new byte[4];
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read);
        int read = fs.Read(bom, 0, 4);

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return "UTF-8 BOM";
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return "UTF-16 LE";
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) return "UTF-16 BE";

        return "UTF-8";
    }

    private (int width, int height) ReadImageDimensions(string ext)
    {
        try
        {
            using FileStream fs = new(_filePath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new(fs);

            if (ext == ".png")
            {
                fs.Seek(16, SeekOrigin.Begin);
                byte[] width = reader.ReadBytes(4);
                byte[] height = reader.ReadBytes(4);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(width);
                    Array.Reverse(height);
                }

                return (BitConverter.ToInt32(width), BitConverter.ToInt32(height));
            }

            if (ext is ".jpg" or ".jpeg")
            {
                while (fs.Position < fs.Length - 9)
                {
                    if (reader.ReadByte() != 0xFF)
                    {
                        continue;
                    }

                    byte marker = reader.ReadByte();
                    if (marker is >= 0xC0 and <= 0xC3)
                    {
                        fs.Seek(3, SeekOrigin.Current);
                        byte[] height = reader.ReadBytes(2);
                        byte[] width = reader.ReadBytes(2);

                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(width);
                            Array.Reverse(height);
                        }

                        return (BitConverter.ToInt16(width), BitConverter.ToInt16(height));
                    }
                }
            }
        }
        catch
        {
        }

        return (0, 0);
    }

    private static string RunProcess(string fileName, string args)
    {
        try
        {
            Process proc = new()
            {
                StartInfo = new()
                {
                    FileName = fileName,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            proc.Start();
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output;
        }
        catch
        {
            return "";
        }
    }

    private string GetFileTypeLabel()
    {
        if (Directory.Exists(_filePath))
        {
            return "Folder";
        }

        if (FileUtils.IsFileExecutable(_filePath))
        {
            return "Executable";
        }

        string ext = Path.GetExtension(_filePath).ToLowerInvariant();
        return string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = bytes;
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]} ({bytes:N0} bytes)";
    }

    private string TruncateValue(string value)
    {
        int maxVal = _list.MaxWidth - 20;
        if (maxVal < 4) return value;

        return value.Length > maxVal ? value[..(maxVal - 3)] + "..." : value;
    }

    private static bool IsImageExtension(string ext) => ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tiff";
    private static bool IsMediaExtension(string ext) => ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".mp3" or ".flac" or ".wav" or ".ogg";
    private static bool IsTextExtension(string ext) => ext is ".txt" or ".md" or ".cs" or ".cpp" or ".h" or ".py" or ".js" or ".ts" or ".json" or ".xml" or ".yaml" or ".toml" or ".sh";
    private static bool IsArchiveExtension(string ext) => ext is ".zip" or ".tar" or ".gz" or ".bz2" or ".xz" or ".7z" or ".rar";
}
