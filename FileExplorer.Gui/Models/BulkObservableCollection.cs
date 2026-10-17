using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FileExplorer.Gui.Models;

public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

    public void AddRange(IList<T> collection)
    {
        if (collection.Count == 0) return;

        int startIndex = Items.Count;
        _suppressNotification = true;
        foreach (T item in collection)
        {
            Items.Add(item);
        }
        _suppressNotification = false;

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList) collection, startIndex));
    }

    public void ReplaceAll(IEnumerable<T> collection)
    {
        _suppressNotification = true;
        Items.Clear();
        foreach (T item in collection)
        {
            Items.Add(item);
        }
        _suppressNotification = false;

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
        {
            base.OnCollectionChanged(e);
        }
    }
}
