using System.Collections.Generic;

namespace FileExplorer.Core.States;

public class AppSession
{
    public List<NavigationState> Panes { get; } = new();
    public int ActivePaneIndex { get; set; }

    public NavigationState ActivePane => Panes.Count > 0 ? Panes[ActivePaneIndex] : null!;

    public AppSession(string initialPath)
    {
        Panes.Add(new NavigationState(initialPath));
    }

    public void ToggleSplitPane()
    {
        if (Panes.Count == 1)
        {
            Panes.Add(new NavigationState(ActivePane.CurrentPath));
        }
        else
        {
            int removeIndex = ActivePaneIndex == 0 ? 1 : 0;
            Panes.RemoveAt(removeIndex);
            ActivePaneIndex = 0;
        }
    }

    public void SwitchPane(int indexOffset)
    {
        if (Panes.Count == 0) return;
        
        ActivePaneIndex = (ActivePaneIndex + indexOffset % Panes.Count + Panes.Count) % Panes.Count;
    }
}
