using System;
using System.Collections.Generic;

namespace FileExplorer.Core.Models;

public class FilePickerResult
{
    public bool IsSuccess { get; }

    public IReadOnlyList<string> SelectedPaths { get; }

    public string? SelectedPath => SelectedPaths.Count > 0 ? SelectedPaths[0] : null;

    public FilePickerResult(bool isSuccess, IReadOnlyList<string>? selectedPaths = null)
    {
        IsSuccess = isSuccess;
        SelectedPaths = selectedPaths ?? Array.Empty<string>();
    }

    public static FilePickerResult Cancelled() => new(false);

    public static FilePickerResult Selected(string path) => new(true, new[] { path });

    public static FilePickerResult Selected(IReadOnlyList<string> paths) => new(true, paths);
}
