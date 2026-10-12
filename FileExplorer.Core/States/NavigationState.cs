namespace FileExplorer.Core.States;

public class NavigationState
{
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    public string CurrentPath { get; private set; }

    public NavigationState(string initialPath)
    {
        CurrentPath = Path.GetFullPath(initialPath);
        _history.Add(CurrentPath);
        _historyIndex = 0;
    }

    public void NavigateTo(string path)
    {
        path = Path.GetFullPath(path);

        try
        {
            DirectoryInfo dir = new(path);
            if (dir.Exists && dir.LinkTarget != null)
            {
                FileSystemInfo? target = dir.ResolveLinkTarget(true);
                if (target != null)
                {
                    path = target.FullName;
                }
            }
        }
        catch { }
        
        if (CurrentPath == path) return;
        
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
        }

        _history.Add(path);
        _historyIndex++;
        CurrentPath = path;
    }

    public void NavigateUp()
    {
        string? parent = Path.GetDirectoryName(CurrentPath);
        if (parent != null)
        {
            NavigateTo(parent);
        }
    }

    public void GoBack()
    {
        if (_historyIndex > 0)
        {
            _historyIndex--;
            CurrentPath = _history[_historyIndex];
        }
    }

    public void GoForward()
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            CurrentPath = _history[_historyIndex];
        }
    }

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex < _history.Count - 1;
}
