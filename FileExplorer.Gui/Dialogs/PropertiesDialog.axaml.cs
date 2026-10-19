using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using FileExplorer.Core.Entries;
using FileExplorer.Core.Models;
using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;
using FileExplorer.Gui.Services;
using FileExplorer.Gui.ViewModels;
using SkiaSharp;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Dialogs;

public partial class PropertiesDialog : Window
{
    private static readonly string[] FileAccessNames = ["AccessNone", "AccessReadOnly", "AccessReadWrite"];
    private static readonly int[] FileAccessBits = [0, 4, 6];
    private static readonly string[] FolderAccessNames = ["AccessNone", "AccessListFiles", "AccessFiles", "AccessCreateDelete"];
    private static readonly int[] FolderAccessBits = [0, 4, 5, 7];

    private readonly CancellationTokenSource _cts = new();
    private string _path = "";
    private string _originalName = "";
    private bool _isDirectory;
    private int _mode = -1;
    private bool _updatingUi;
    private bool _canEditPermissions;
    private UnixStat? _stat;
    private string? _mimeType;
    private string? _defaultApp;
    private string _typeLabelText = "";
    private string? _typeIconKey;
    private FileItemViewModel? _item;

    public PropertiesDialog()
    {
        InitializeComponent();
        Chooser.ProgramActivated += LaunchSelectedApp;

        foreach (Grid grid in this.GetLogicalDescendants().OfType<Grid>().Where(g => g.Classes.Contains("rows")))
        {
            double spacing = -grid.Margin.Bottom;
            foreach (Control child in grid.Children)
            {
                child.Margin = new(child.Margin.Left, child.Margin.Top, child.Margin.Right, spacing);
            }
        }

        Opened += (_, _) =>
        {
            CloseButton.Focus();
        };

        Closing += (_, _) =>
        {
            TryRename();
            _cts.Cancel();
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            Close();
            e.Handled = true;
        }, Tunnel);
    }

    public PropertiesDialog(FileItemViewModel item) : this()
    {
        _path = item.FullPath;
        _originalName = item.Name;
        _isDirectory = item.IsDirectory;

        NameBox.Text = item.Name;
        _typeLabelText = item.TypeString;
        TypeLabel.Text = _typeLabelText;
        _item = item;
        ItemIcon.Source = item.Icon;
        SetupTypeIconMenu();
        LocationValue.Text = Path.GetDirectoryName(item.FullPath) ?? "/";
        OpenWithTab.IsVisible = !_isDirectory;
        ChecksumCard.IsVisible = !_isDirectory;
        ExecutableCheckBox.IsVisible = !_isDirectory;
        ApplyRecursiveButton.IsVisible = _isDirectory;

        LoadGeneral(item);
        FillAccessCombos();
        _ = LoadStatAsync();

        if (_isDirectory)
        {
            StartFolderScan();
        }
        else
        {
            _ = LoadMimeInfoAsync();
        }

        if (item.FileType == FileType.Image)
        {
            _ = LoadDimensionsAsync();
        }
    }

    private void LoadGeneral(FileItemViewModel item)
    {
        SetRowVisible(ContentsLabel, ContentsValue, _isDirectory);
        SetRowVisible(DimensionsLabel, DimensionsValue, false);
        SetRowVisible(OpensWithLabel, OpensWithValue, !_isDirectory);

        try
        {
            FileSystemInfo info = _isDirectory ? new DirectoryInfo(_path) : new FileInfo(_path);
            ModifiedValue.Text = info.LastWriteTime.ToString("F");
            AccessedValue.Text = info.LastAccessTime.ToString("F");

            string? linkTarget = info.LinkTarget;
            SetRowVisible(LinkLabel, LinkValue, linkTarget != null);
            LinkValue.Text = linkTarget ?? "";

            if (info is FileInfo file)
            {
                SizeValue.Text = LocalizationService.Format("SizeWithBytes", FileUtils.FormatSize(file.Length), file.Length);
            }
        }
        catch
        {
            ModifiedValue.Text = item.ModifiedString;
            AccessedValue.Text = "-";
            SetRowVisible(LinkLabel, LinkValue, false);
            SizeValue.Text = string.IsNullOrEmpty(item.SizeString) ? "-" : item.SizeString;
        }

        CreatedValue.Text = "…";
        OpensWithValue.Text = "…";
    }

    private static void SetRowVisible(Control label, Control value, bool visible)
    {
        label.IsVisible = visible;
        value.IsVisible = visible;
    }

    private void StartFolderScan()
    {
        SizeValue.Text = LocalizationService.Get("Calculating");
        ContentsValue.Text = "…";
        SizeStatusText.Text = LocalizationService.Get("Calculating");
        SizeStatusText.IsVisible = true;
        CancellationToken token = _cts.Token;

        _ = FolderScanner.ScanAsync(_path, progress =>
        {
            Dispatcher.UIThread.Post(() => ShowScanProgress(progress));
        }, token).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private void ShowScanProgress(FolderScanProgress progress)
    {
        SizeValue.Text = LocalizationService.Format("SizeWithBytes", FileUtils.FormatSize(progress.Bytes), progress.Bytes);
        ContentsValue.Text = LocalizationService.Format("ContentsSummary", LocalizationService.Plural("FolderCount", progress.Folders), LocalizationService.Plural("FileCount", progress.Files));

        if (!progress.Completed)
        {
            SizeStatusText.Text = LocalizationService.Get("Calculating");
            return;
        }

        SizeStatusText.IsVisible = progress.Unreadable > 0;
        SizeStatusText.Text = LocalizationService.Format("UnreadableFolders", LocalizationService.Plural("FolderCount", progress.Unreadable));
    }

    private async Task LoadStatAsync()
    {
        string path = _path;
        UnixStat? stat = await Task.Run(() => UnixStat.Load(path));
        List<string> groups = await Task.Run(UnixStat.GetCurrentUserGroups);
        DriveInfo? drive = null;
        if (stat != null && !string.IsNullOrEmpty(stat.MountPoint))
        {
            drive = await Task.Run(() => TryGetDrive(stat.MountPoint));
        }

        _stat = stat;
        if (stat == null)
        {
            CreatedValue.Text = LocalizationService.Get("Unknown");
            ShowPermissionMessage(LocalizationService.Get("CouldNotReadInfo"));
            return;
        }

        CreatedValue.Text = stat.Birth?.ToString("F") ?? LocalizationService.Get("Unknown");
        DiskSizeValue.Text = LocalizationService.Format("SizeWithBytes", FileUtils.FormatSize(stat.SizeOnDisk), stat.SizeOnDisk);
        InodeValue.Text = stat.Inode.ToString();
        LinksValue.Text = stat.HardLinks.ToString();
        MountValue.Text = stat.MountPoint;
        FileSystemValue.Text = stat.FileSystemType;
        if (_isDirectory)
        {
            MimeValue.Text = "inode/directory";
        }

        ShowVolume(stat, drive);
        ShowOwnership(stat, groups);

        _mode = stat.Mode;
        RefreshPermissionUi();
    }

    private static DriveInfo? TryGetDrive(string mountPoint)
    {
        try
        {
            DriveInfo drive = new(mountPoint);
            return drive.IsReady ? drive : null;
        }
        catch
        {
            return null;
        }
    }

    private void ShowVolume(UnixStat stat, DriveInfo? drive)
    {
        VolumeValue.Text = string.IsNullOrEmpty(stat.FileSystemType) ? stat.MountPoint : $"{stat.MountPoint} ({stat.FileSystemType})";
        if (drive == null || drive.TotalSize <= 0)
        {
            VolumeBar.IsVisible = false;
            VolumeFreeText.Text = "";
            return;
        }

        long used = drive.TotalSize - drive.AvailableFreeSpace;
        VolumeBar.Value = used * 100.0 / drive.TotalSize;
        VolumeFreeText.Text = LocalizationService.Format("VolumeFreeOf", FileUtils.FormatSize(drive.AvailableFreeSpace), FileUtils.FormatSize(drive.TotalSize));
    }

    private void ShowOwnership(UnixStat stat, List<string> groups)
    {
        uint currentUid = UnixStat.CurrentUid;
        _canEditPermissions = currentUid == 0 || currentUid == stat.Uid;
        OwnerValue.Text = currentUid == stat.Uid ? LocalizationService.Format("OwnerYou", stat.Owner) : stat.Owner;

        List<string> options = groups.ToList();
        if (!options.Contains(stat.Group))
        {
            options.Insert(0, stat.Group);
        }

        _updatingUi = true;
        GroupCombo.ItemsSource = options;
        GroupCombo.SelectedItem = stat.Group;
        GroupCombo.IsEnabled = _canEditPermissions;
        _updatingUi = false;

        List<string> access = new();
        if (FileUtils.HasAccess(_path, true, false, false))
        {
            access.Add(LocalizationService.Get(_isDirectory ? "CanListContents" : "CanRead"));
        }

        if (FileUtils.HasAccess(_path, false, true, false))
        {
            access.Add(LocalizationService.Get(_isDirectory ? "CanCreateDelete" : "CanWrite"));
        }

        if (FileUtils.HasAccess(_path, false, false, true))
        {
            access.Add(LocalizationService.Get(_isDirectory ? "CanOpen" : "CanExecute"));
        }

        YourAccessValue.Text = access.Count == 0 ? LocalizationService.Get("NoAccess") : LocalizationService.Format("YouCan", string.Join(", ", access));

        if (!_canEditPermissions)
        {
            ShowPermissionMessage(stat.Owner == "root"
                ? LocalizationService.Get("OnlyRootCanChange")
                : LocalizationService.Format("OnlyOwnerCanChange", stat.Owner));
        }
    }

    private void FillAccessCombos()
    {
        string[] names = (_isDirectory ? FolderAccessNames : FileAccessNames)
            .Select(LocalizationService.Get)
            .ToArray();
        foreach (ComboBox combo in new[] { OwnerAccessCombo, GroupAccessCombo, OthersAccessCombo })
        {
            combo.ItemsSource = names;
        }
    }

    private void RefreshPermissionUi()
    {
        if (_mode < 0)
        {
            return;
        }

        _updatingUi = true;

        SetAccessCombo(OwnerAccessCombo, 6);
        SetAccessCombo(GroupAccessCombo, 3);
        SetAccessCombo(OthersAccessCombo, 0);

        ExecutableCheckBox.IsChecked = (_mode & 0x49) != 0;
        SetUidCheckBox.IsChecked = (_mode & 0x800) != 0;
        SetGidCheckBox.IsChecked = (_mode & 0x400) != 0;
        StickyCheckBox.IsChecked = (_mode & 0x200) != 0;
        OctalBox.Text = Convert.ToString(_mode, 8).PadLeft(4, '0');
        SymbolicText.Text = UnixStat.FormatMode(_mode, _isDirectory);

        foreach (Control control in new Control[] { OwnerAccessCombo, GroupAccessCombo, OthersAccessCombo, ExecutableCheckBox, OctalBox, SetUidCheckBox, SetGidCheckBox, StickyCheckBox, ApplyRecursiveButton })
        {
            control.IsEnabled = _canEditPermissions;
        }

        _updatingUi = false;
    }

    private void SetAccessCombo(ComboBox combo, int shift)
    {
        int[] bits = _isDirectory ? FolderAccessBits : FileAccessBits;
        int mask = _isDirectory ? 7 : 6;
        int value = (_mode >> shift) & mask;
        combo.SelectedIndex = Array.IndexOf(bits, value);
    }

    private int ApplyAccessCombo(int mode, ComboBox combo, int shift)
    {
        if (combo.SelectedIndex < 0)
        {
            return mode;
        }

        int[] bits = _isDirectory ? FolderAccessBits : FileAccessBits;
        int mask = (_isDirectory ? 7 : 6) << shift;

        return (mode & ~mask) | (bits[combo.SelectedIndex] << shift);
    }

    private void OnAccessComboChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _mode < 0)
        {
            return;
        }

        int mode = _mode;
        mode = ApplyAccessCombo(mode, OwnerAccessCombo, 6);
        mode = ApplyAccessCombo(mode, GroupAccessCombo, 3);
        mode = ApplyAccessCombo(mode, OthersAccessCombo, 0);
        ApplyMode(mode);
    }

    private void OnExecutableChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingUi || _mode < 0)
        {
            return;
        }

        if (ExecutableCheckBox.IsChecked != true)
        {
            ApplyMode(_mode & ~0x49);
            return;
        }

        int mode = _mode;
        if ((mode & 0x100) != 0 || (mode & 0x124) == 0)
        {
            mode |= 0x40;
        }

        if ((mode & 0x20) != 0)
        {
            mode |= 0x8;
        }

        if ((mode & 0x4) != 0)
        {
            mode |= 0x1;
        }

        ApplyMode(mode);
    }

    private void OnSpecialBitChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingUi || _mode < 0)
        {
            return;
        }

        int mode = _mode & ~0xE00;
        mode |= SetUidCheckBox.IsChecked == true ? 0x800 : 0;
        mode |= SetGidCheckBox.IsChecked == true ? 0x400 : 0;
        mode |= StickyCheckBox.IsChecked == true ? 0x200 : 0;
        ApplyMode(mode);
    }

    private void OnOctalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        ApplyOctalText();
        e.Handled = true;
    }

    private void OnOctalLostFocus(object? sender, RoutedEventArgs e)
    {
        ApplyOctalText();
    }

    private void ApplyOctalText()
    {
        if (_updatingUi || _mode < 0 || !_canEditPermissions)
        {
            return;
        }

        int mode = UnixStat.ParseOctal(OctalBox.Text ?? "");
        if (mode < 0 || mode > 0xFFF)
        {
            ShowPermissionMessage(LocalizationService.Get("InvalidOctalMode"));
            RefreshPermissionUi();
            return;
        }

        if (mode != _mode)
        {
            ApplyMode(mode);
        }
    }

    private void ApplyMode(int mode)
    {
        try
        {
            File.SetUnixFileMode(_path, (UnixFileMode) mode);
            _mode = mode;
            PermissionsErrorText.IsVisible = false;
        }
        catch (Exception ex)
        {
            ShowPermissionMessage(LocalizationService.Format("ChangePermissionsFailed", ex.Message));
        }

        RefreshPermissionUi();
    }

    private void ShowPermissionMessage(string message)
    {
        PermissionsErrorText.Text = message;
        PermissionsErrorText.IsVisible = true;
    }

    private async void OnGroupChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || _stat == null || GroupCombo.SelectedItem is not string group || group == _stat.Group)
        {
            return;
        }

        string path = _path;
        (bool success, string error) = await Task.Run(() =>
        {
            bool ok = UnixStat.ChangeGroup(path, group, false, out string message);
            return (ok, message);
        });

        if (!success)
        {
            ShowPermissionMessage(LocalizationService.Format("ChangeGroupFailed", error));
        }

        await LoadStatAsync();
    }

    private async void OnApplyRecursiveClick(object? sender, RoutedEventArgs e)
    {
        if (_mode < 0)
        {
            return;
        }

        ConfirmDialog confirm = new(
            LocalizationService.Get("ApplyRecursiveTitle"),
            LocalizationService.Format("ApplyRecursiveMessage", Path.GetFileName(_path), UnixStat.FormatMode(_mode, true)[1..]),
            LocalizationService.Get("Apply"),
            true,
            false);
        bool confirmed = await confirm.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        int folderMode = _mode;
        string root = _path;
        ApplyRecursiveButton.IsEnabled = false;
        ShowPermissionMessage(LocalizationService.Get("Applying"));

        (int changed, int failed) = await Task.Run(() => ApplyRecursive(root, folderMode, _cts.Token));

        ApplyRecursiveButton.IsEnabled = true;
        ShowPermissionMessage(failed == 0 ? LocalizationService.Format("UpdatedItems", changed) : LocalizationService.Format("UpdatedItemsWithFailures", changed, failed));
    }

    private static (int Changed, int Failed) ApplyRecursive(string root, int folderMode, CancellationToken token)
    {
        int fileReadWrite = folderMode & 0x1B6;
        int changed = 0;
        int failed = 0;
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
        };

        Stack<string> pending = new();
        pending.Push(root);

        while (pending.Count > 0 && !token.IsCancellationRequested)
        {
            string current = pending.Pop();
            try
            {
                foreach (FileSystemInfo info in new DirectoryInfo(current).EnumerateFileSystemInfos("*", options))
                {
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    try
                    {
                        if (info is DirectoryInfo)
                        {
                            File.SetUnixFileMode(info.FullName, (UnixFileMode) (folderMode & 0xFFF));
                            pending.Push(info.FullName);
                        }
                        else
                        {
                            int existing = (int) File.GetUnixFileMode(info.FullName);
                            int mode = (existing & ~0x1B6) | fileReadWrite;
                            File.SetUnixFileMode(info.FullName, (UnixFileMode) mode);
                        }

                        changed++;
                    }
                    catch
                    {
                        failed++;
                    }
                }
            }
            catch
            {
                failed++;
            }
        }

        return (changed, failed);
    }

    private void SetupTypeIconMenu()
    {
        _typeIconKey = _isDirectory ? null : FileTypeIconService.GetKey(_path);
        if (_typeIconKey == null)
        {
            return;
        }

        ItemIconButton.IsHitTestVisible = true;
        ItemIconButton.Cursor = new(StandardCursorType.Hand);
        ToolTip.SetTip(ItemIconButton, LocalizationService.Get("ChangeTypeIconTip"));
        ChangeTypeIconMenuItem.Header = LocalizationService.Format("ChangeTypeIcon", FileTypeIconService.GetKeyDisplayName(_typeIconKey));
        ResetTypeIconMenuItem.IsEnabled = FileTypeIconService.HasCustomIcon(_typeIconKey);
    }

    private async void OnChangeTypeIconClick(object? sender, RoutedEventArgs e)
    {
        if (_typeIconKey == null)
        {
            return;
        }

        string initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!Directory.Exists(initialPath))
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        FilePickerOptions options = new()
        {
            Mode = PickerMode.File,
            InitialPath = initialPath,
            Title = LocalizationService.Get("SelectIcon"),
            ActionButtonText = LocalizationService.Get("Select")
        };

        MainWindow picker = new(initialPath, options);
        FilePickerResult result = await picker.ShowPickerAsync(this);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.SelectedPath))
        {
            return;
        }

        if (IconService.LoadFile(result.SelectedPath) == null)
        {
            ConfirmDialog error = new(LocalizationService.Get("Error"), LocalizationService.Get("InvalidIconFile"));
            await error.ShowDialog(this);
            return;
        }

        try
        {
            FileTypeIconService.SetCustomIcon(_typeIconKey, result.SelectedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ConfirmDialog error = new(LocalizationService.Get("Error"), ex.Message);
            await error.ShowDialog(this);
            return;
        }

        RefreshItemIcon();
    }

    private void OnResetTypeIconClick(object? sender, RoutedEventArgs e)
    {
        if (_typeIconKey == null)
        {
            return;
        }

        FileTypeIconService.ResetCustomIcon(_typeIconKey);
        RefreshItemIcon();
    }

    private void RefreshItemIcon()
    {
        if (_item == null || _typeIconKey == null)
        {
            return;
        }

        ItemIcon.Source = IconService.GetForEntry(false, _item.FileType, _path);
        ResetTypeIconMenuItem.IsEnabled = FileTypeIconService.HasCustomIcon(_typeIconKey);
    }

    private async Task LoadMimeInfoAsync()
    {
        await Chooser.LoadAsync(_path);
        AppAssociations? associations = Chooser.Associations;
        if (associations == null)
        {
            return;
        }

        _mimeType = associations.MimeType;
        _defaultApp = associations.DefaultApp;
        MimeValue.Text = associations.MimeType;
        TypeLabel.Text = $"{_typeLabelText} ({associations.MimeType})";
        OpensWithValue.Text = associations.DefaultAppName ?? LocalizationService.Get("NoDefaultApplication");
        OpenWithHeader.Text = LocalizationService.Format("OpenWithHeader", associations.MimeType);
    }

    private async void OnSetDefaultAppClick(object? sender, RoutedEventArgs e)
    {
        string? program = Chooser.SelectedProgram;
        if (string.IsNullOrEmpty(program))
        {
            return;
        }

        string path = _path;
        await Task.Run(() => MimeService.SetDefaultApplication(path, program));
        await LoadMimeInfoAsync();
    }

    private void OnOpenWithSelectedClick(object? sender, RoutedEventArgs e)
    {
        LaunchSelectedApp();
    }

    private void LaunchSelectedApp()
    {
        string? program = Chooser.SelectedProgram;
        if (string.IsNullOrEmpty(program))
        {
            return;
        }

        try
        {
            FileExecutionService.OpenWith(program, _path);
        }
        catch
        {
        }
    }

    private async Task LoadDimensionsAsync()
    {
        string path = _path;
        string? dimensions = await Task.Run(() =>
        {
            try
            {
                using SKCodec? codec = SKCodec.Create(path);
                return codec == null ? null : LocalizationService.Format("DimensionsPixels", codec.Info.Width, codec.Info.Height);
            }
            catch
            {
                return null;
            }
        });

        if (dimensions == null)
        {
            return;
        }

        DimensionsValue.Text = dimensions;
        SetRowVisible(DimensionsLabel, DimensionsValue, true);
    }

    private async void OnCalculateChecksumsClick(object? sender, RoutedEventArgs e)
    {
        ChecksumButton.IsEnabled = false;
        Md5Value.Text = LocalizationService.Get("Calculating");
        Sha1Value.Text = LocalizationService.Get("Calculating");
        Sha256Value.Text = LocalizationService.Get("Calculating");

        string path = _path;
        CancellationToken token = _cts.Token;
        try
        {
            (string md5, string sha1, string sha256) = await Task.Run(() => ComputeChecksums(path, token), token);
            Md5Value.Text = md5;
            Sha1Value.Text = sha1;
            Sha256Value.Text = sha256;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Md5Value.Text = LocalizationService.Format("FailedWithError", ex.Message);
            Sha1Value.Text = "-";
            Sha256Value.Text = "-";
        }

        ChecksumButton.IsEnabled = true;
    }

    private static (string Md5, string Sha1, string Sha256) ComputeChecksums(string path, CancellationToken token)
    {
        using IncrementalHash md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using IncrementalHash sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);

        byte[] buffer = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> chunk = buffer.AsSpan(0, read);
            md5.AppendData(chunk);
            sha1.AppendData(chunk);
            sha256.AppendData(chunk);
        }

        return (
            Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
            Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant());
    }

    private async void OnCopyChecksumClick(object? sender, RoutedEventArgs e)
    {
        string? text = (sender as Button)?.Tag switch
        {
            "md5" => Md5Value.Text,
            "sha1" => Sha1Value.Text,
            "sha256" => Sha256Value.Text,
            _ => null,
        };

        if (string.IsNullOrEmpty(text) || text == "-" || Clipboard == null)
        {
            return;
        }

        await Clipboard.SetTextAsync(text);
    }

    private async void OnCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard == null)
        {
            return;
        }

        await Clipboard.SetTextAsync(_path);
    }

    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        TryRename();
        CloseButton.Focus();
        e.Handled = true;
    }

    private void TryRename()
    {
        string newName = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(newName) || newName == _originalName)
        {
            return;
        }

        if (newName.Contains('/') || newName is "." or "..")
        {
            ShowRenameError(LocalizationService.Get("NameCannotContainSlash"));
            return;
        }

        string target = Path.Combine(Path.GetDirectoryName(_path) ?? "/", newName);
        if (File.Exists(target) || Directory.Exists(target))
        {
            ShowRenameError(LocalizationService.Format("NameAlreadyExists", newName));
            return;
        }

        try
        {
            if (_isDirectory)
            {
                Directory.Move(_path, target);
                FolderColorService.Move(_path, target);
            }
            else
            {
                File.Move(_path, target);
            }

            _path = target;
            _originalName = newName;
            RenameErrorText.IsVisible = false;
        }
        catch (Exception ex)
        {
            ShowRenameError(LocalizationService.Format("RenameFailed", ex.Message));
        }
    }

    private void ShowRenameError(string message)
    {
        RenameErrorText.Text = message;
        RenameErrorText.IsVisible = true;
        NameBox.Text = _originalName;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
