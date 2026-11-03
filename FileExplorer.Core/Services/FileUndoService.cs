using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileExplorer.Core.Models;
using LoggerLib;

namespace FileExplorer.Core.Services;

public enum FileUndoKind
{
    Rename,
    Move,
    Copy,
    Create,
    Trash,
    Link,
    Custom,
}

public sealed record FileUndoStep(string Path, string Other);

public sealed class FileUndoEntry
{
    public FileUndoKind Kind { get; }
    public IReadOnlyList<FileUndoStep> Steps { get; }
    public string? Title { get; init; }
    public Func<bool, Task<bool>>? Handler { get; init; }

    public FileUndoEntry(FileUndoKind kind, IReadOnlyList<FileUndoStep> steps)
    {
        Kind = kind;
        Steps = steps;
    }

    public string FirstName => Steps.Count == 0 ? "" : Path.GetFileName(Steps[0].Path);
}

public sealed record FileUndoResult(bool Success, IReadOnlyList<string> Paths, string? Error, bool IsConflict = false);

public sealed class FileUndoService
{
    private const int MaxEntries = 100;

    private readonly FileOperationService _operations;
    private readonly List<FileUndoEntry> _undo = new();
    private readonly List<FileUndoEntry> _redo = new();

    public event Action? Changed;

    public FileUndoService(FileOperationService operations)
    {
        _operations = operations;
    }

    public FileUndoEntry? NextUndo => _undo.Count == 0 ? null : _undo[^1];
    public FileUndoEntry? NextRedo => _redo.Count == 0 ? null : _redo[^1];
    public bool IsBusy { get; private set; }

    public void Record(FileUndoKind kind, IEnumerable<FileUndoStep> steps)
    {
        List<FileUndoStep> list = steps.ToList();
        if (list.Count == 0 || IsBusy)
        {
            return;
        }

        Push(new(kind, list));
    }

    public void RecordCustom(string title, Func<bool, Task<bool>> handler)
    {
        if (IsBusy)
        {
            return;
        }

        Push(new(FileUndoKind.Custom, []) { Title = title, Handler = handler });
    }

    private void Push(FileUndoEntry entry)
    {
        _undo.Add(entry);
        if (_undo.Count > MaxEntries)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        Changed?.Invoke();
    }

    public void Record(FileUndoKind kind, IEnumerable<(string Path, string Other)> steps)
    {
        Record(kind, steps.Select(step => new FileUndoStep(step.Path, step.Other)));
    }

    public Task<FileUndoResult> UndoAsync()
    {
        return ApplyAsync(_undo, _redo, true);
    }

    public Task<FileUndoResult> RedoAsync()
    {
        return ApplyAsync(_redo, _undo, false);
    }

    private async Task<FileUndoResult> ApplyAsync(List<FileUndoEntry> from, List<FileUndoEntry> to, bool isUndo)
    {
        if (from.Count == 0 || IsBusy)
        {
            return new(false, [], null);
        }

        FileUndoEntry entry = from[^1];
        from.RemoveAt(from.Count - 1);
        IsBusy = true;
        Changed?.Invoke();
        try
        {
            if (entry.Handler != null)
            {
                bool handled = await entry.Handler(isUndo);
                if (handled)
                {
                    to.Add(entry);
                }

                return new(handled, [], null);
            }

            string? problem = FindProblem(entry, isUndo);
            if (problem != null)
            {
                return new(false, [], problem, true);
            }

            (FileUndoEntry next, List<string> paths) = await Task.Run(() => isUndo ? Undo(entry) : Redo(entry));
            to.Add(next);
            _ = Logger.LogI($"{(isUndo ? "Undid" : "Redid")} {entry.Kind} of {entry.Steps.Count} items");
            return new(true, paths, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = Logger.LogW($"{(isUndo ? "Undo" : "Redo")} of {entry.Kind} failed: {ex.Message}");
            return new(false, [], ex.Message);
        }
        finally
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }

    private static string? FindProblem(FileUndoEntry entry, bool isUndo)
    {
        HashSet<string> moving = entry.Kind is FileUndoKind.Rename or FileUndoKind.Move
            ? entry.Steps.Select(step => isUndo ? step.Other : step.Path).ToHashSet(StringComparer.Ordinal)
            : new();
        foreach (FileUndoStep step in entry.Steps)
        {
            (string mustExist, string? mustBeFree) = (entry.Kind, isUndo) switch
            {
                (FileUndoKind.Rename or FileUndoKind.Move, true) => (step.Other, step.Path),
                (FileUndoKind.Rename or FileUndoKind.Move, false) => (step.Path, step.Other),
                (FileUndoKind.Copy or FileUndoKind.Create, true) => (step.Path, null),
                (FileUndoKind.Copy or FileUndoKind.Create, false) => (step.Other, step.Path),
                (FileUndoKind.Trash, true) => (step.Other, step.Path),
                (FileUndoKind.Trash, false) => (step.Path, null),
                (FileUndoKind.Link, true) => (step.Path, null),
                _ => ("", step.Path),
            };

            if (mustExist.Length > 0 && !Exists(mustExist))
            {
                return mustExist;
            }

            if (mustBeFree != null && !moving.Contains(mustBeFree) && Exists(mustBeFree))
            {
                return mustBeFree;
            }
        }

        return null;
    }

    private (FileUndoEntry Next, List<string> Paths) Undo(FileUndoEntry entry)
    {
        switch (entry.Kind)
        {
            case FileUndoKind.Rename:
            case FileUndoKind.Move:
                MoveAll(entry.Steps.Select(step => (step.Other, step.Path)).ToList());
                return (entry, entry.Steps.Select(step => step.Path).ToList());
            case FileUndoKind.Copy:
            case FileUndoKind.Create:
                List<TrashedItem> trashed = Trash(entry.Steps.Select(step => step.Path));
                return (new(entry.Kind, trashed.Select(item => new FileUndoStep(item.OriginalPath, item.TrashedPath)).ToList()), []);
            case FileUndoKind.Trash:
                Restore(entry.Steps.Select(step => step.Other));
                return (entry, entry.Steps.Select(step => step.Path).ToList());
            default:
                foreach (FileUndoStep step in entry.Steps)
                {
                    File.Delete(step.Path);
                }

                return (entry, []);
        }
    }

    private (FileUndoEntry Next, List<string> Paths) Redo(FileUndoEntry entry)
    {
        switch (entry.Kind)
        {
            case FileUndoKind.Rename:
            case FileUndoKind.Move:
                MoveAll(entry.Steps.Select(step => (step.Path, step.Other)).ToList());
                return (entry, entry.Steps.Select(step => step.Other).ToList());
            case FileUndoKind.Copy:
            case FileUndoKind.Create:
                Restore(entry.Steps.Select(step => step.Other));
                return (entry, entry.Steps.Select(step => step.Path).ToList());
            case FileUndoKind.Trash:
                List<TrashedItem> trashed = Trash(entry.Steps.Select(step => step.Path));
                return (new(entry.Kind, trashed.Select(item => new FileUndoStep(item.OriginalPath, item.TrashedPath)).ToList()), []);
            default:
                foreach (FileUndoStep step in entry.Steps)
                {
                    if (Directory.Exists(step.Other))
                    {
                        Directory.CreateSymbolicLink(step.Path, step.Other);
                    }
                    else
                    {
                        File.CreateSymbolicLink(step.Path, step.Other);
                    }
                }

                return (entry, entry.Steps.Select(step => step.Path).ToList());
        }
    }

    private List<TrashedItem> Trash(IEnumerable<string> paths)
    {
        List<TrashedItem> trashed = new();
        List<string> failed = _operations.TrashForUndo(paths, trashed);
        if (failed.Count > 0 && trashed.Count == 0)
        {
            throw new IOException(failed[0]);
        }

        return trashed;
    }

    private void Restore(IEnumerable<string> trashedPaths)
    {
        List<string> failed = _operations.RestoreFromTrash(trashedPaths);
        if (failed.Count > 0)
        {
            throw new IOException(failed[0]);
        }
    }

    private void MoveAll(List<(string From, string To)> moves)
    {
        HashSet<string> sources = new(moves.Select(move => move.From), StringComparer.Ordinal);
        List<(string Temp, string To)> staged = new();
        foreach ((string from, string to) in moves)
        {
            if (!sources.Contains(to))
            {
                MoveEntry(from, to);
                continue;
            }

            string temp = Path.Combine(Path.GetDirectoryName(from) ?? "", $".fe-undo-{Guid.NewGuid():N}");
            MoveEntry(from, temp);
            staged.Add((temp, to));
        }

        foreach ((string temp, string to) in staged)
        {
            MoveEntry(temp, to);
        }
    }

    private void MoveEntry(string from, string to)
    {
        string? parent = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        bool isDirectory = Directory.Exists(from) && new FileInfo(from).LinkTarget == null;
        if (!isDirectory)
        {
            File.Move(from, to);
            return;
        }

        try
        {
            Directory.Move(from, to);
        }
        catch (IOException) when (Directory.Exists(from) && !Exists(to))
        {
            MoveAcrossVolumes(from, to);
        }

        FolderColorStore.Move(from, to);
    }

    private void MoveAcrossVolumes(string from, string to)
    {
        string targetDir = Path.GetDirectoryName(to) ?? "";
        List<string> targets = _operations.TransferAsync(targetDir, ClipboardMode.Cut, [from]).GetAwaiter().GetResult();
        string? moved = targets.FirstOrDefault();
        if (moved != null && moved != to)
        {
            Directory.Move(moved, to);
        }
    }

    private static bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget != null;
    }
}
