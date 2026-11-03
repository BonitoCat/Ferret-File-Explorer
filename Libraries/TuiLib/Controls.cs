using TuiLib.Components;

namespace TuiLib.Components
{
    public enum AnchorPoint { TopLeft, Center }
    public class RenderPart
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int ZIndex { get; set; }
        public string Text { get; set; } = "";
        public Color Foreground { get; set; } = Color.Reset;
        public Color Background { get; set; } = Color.Reset;
    }
    public class TuiStyle
    {
        public Color Foreground { get; set; } = Color.Reset;
        public Color Background { get; set; } = Color.Reset;
    }
    public interface ITuiControl
    {
        int X { get; set; }
        int Y { get; set; }
        int MaxWidth { get; set; }
        int MaxHeight { get; set; }
        AnchorPoint AnchorPoint { get; set; }
        IEnumerable<RenderPart> Render();
    }
}

namespace TuiLib.Controls
{
    public class TuiLabel
    {
        public string Text { get; set; }
        public TuiStyle Style { get; } = new();
        public TuiLabel(string text, Color? foreground = null) { Text = text; Style.Foreground = foreground ?? Color.Reset; }
        public override string ToString() => Text;
    }
    public class TuiListBoxItem<T>
    {
        public T? Value { get; set; }
        public TuiListBoxItem(T value) { Value = value; }
    }
    public class TuiListBox<T> : ITuiControl
    {
        private int _selectedIndex;
        private int _viewIndex;
        public object Lock { get; } = new();
        public List<TuiListBoxItem<T>> Items { get; } = new();
        public int X { get; set; }
        public int Y { get; set; }
        public int MaxWidth { get; set; } = 80;
        public int MaxHeight { get; set; } = 24;
        public int Height { get; set; } = 24;
        public int ViewRange { get; set; } = 24;
        public int ScrollOvershoot { get; set; }
        public bool UseDefaultMarker { get; set; } = true;
        public Color ScrollBarColor { get; set; } = Color.Gray;
        public AnchorPoint AnchorPoint { get; set; }
        public Func<T, IEnumerable<RenderPart>>? ItemRenderer { get; set; }
        public int SelectedIndex { get => Items.Count == 0 ? -1 : Math.Clamp(_selectedIndex, 0, Items.Count - 1); set => _selectedIndex = Items.Count == 0 ? 0 : Math.Clamp(value, 0, Items.Count - 1); }
        public int ViewIndex { get => Math.Clamp(_viewIndex, 0, Math.Max(Items.Count - VisibleRows, 0)); set => _viewIndex = Math.Clamp(value, 0, Math.Max(Items.Count - VisibleRows, 0)); }
        private int VisibleRows => Math.Max(0, Math.Min(MaxHeight, ViewRange));
        public TuiListBoxItem<T>? SelectedItem => SelectedIndex < 0 ? null : Items[SelectedIndex];
        public void AddItem(T item) { lock (Lock) Items.Add(new(item)); }
        public void AddItemRange(IEnumerable<T> items) { lock (Lock) Items.AddRange(items.Select(item => new TuiListBoxItem<T>(item))); }
        public void ClearItems() { lock (Lock) { Items.Clear(); _selectedIndex = _viewIndex = 0; } }
        public int GetItemCount() => Items.Count;
        public int IndexOf(T item) => Items.FindIndex(i => EqualityComparer<T>.Default.Equals(i.Value, item));
        public List<TuiListBoxItem<T>> GetViewItems() { lock (Lock) return Items.Skip(ViewIndex).Take(VisibleRows).ToList(); }
        public bool Scroll(int amount) { lock (Lock) { int old = ViewIndex; ViewIndex += amount; return old != ViewIndex; } }
        public void MoveSelected(int amount)
        {
            lock (Lock)
            {
                SelectedIndex += amount;
                EnsureVisible();
            }
        }
        public bool TryScrollTo(TuiListBoxItem<T> item)
        {
            lock (Lock) { int index = Items.IndexOf(item); if (index < 0) return false; SelectedIndex = index; EnsureVisible(); return true; }
        }
        private void EnsureVisible()
        {
            int margin = Math.Clamp(ScrollOvershoot, 0, Math.Max((VisibleRows - 1) / 2, 0));
            if (SelectedIndex < ViewIndex + margin) ViewIndex = SelectedIndex - margin;
            else if (SelectedIndex >= ViewIndex + VisibleRows - margin) ViewIndex = SelectedIndex - VisibleRows + margin + 1;
        }
        public IEnumerable<RenderPart> Render()
        {
            lock (Lock)
            {
                int width = Math.Max(MaxWidth, 0), height = VisibleRows;
                int x = X - (AnchorPoint == AnchorPoint.Center ? width / 2 : 0);
                int y = Y - (AnchorPoint == AnchorPoint.Center ? Math.Max(MaxHeight, 0) / 2 : 0);
                List<RenderPart> parts = new();
                var visible = GetViewItems();
                for (int row = 0; row < height; row++)
                {
                    parts.Add(new() { X = x, Y = y + row, Text = new string(' ', width), ZIndex = AnchorPoint == AnchorPoint.Center ? 9 : -1 });
                    if (row >= visible.Count || visible[row].Value is not T value) continue;
                    var rendered = ItemRenderer?.Invoke(value) ?? new[] { new RenderPart { Text = (UseDefaultMarker ? (ViewIndex + row == SelectedIndex ? "> " : "  ") : "") + value } };
                    foreach (var part in rendered)
                        parts.Add(new() { X = x + part.X, Y = y + row + part.Y, Text = Rendering.TerminalText.Clip(part.Text, 0, Math.Max(width - part.X, 0)), Foreground = part.Foreground, Background = part.Background, ZIndex = Math.Max(AnchorPoint == AnchorPoint.Center ? 10 : 0, part.ZIndex) });
                }
                if (height > 0 && width > 0 && Items.Count > height)
                {
                    int thumbHeight = Math.Max(1, height * height / Items.Count);
                    int thumbTop = (height - thumbHeight) * ViewIndex / Math.Max(Items.Count - height, 1);
                    for (int row = 0; row < height; row++) parts.Add(new() { X = x + width - 1, Y = y + row, Text = row >= thumbTop && row < thumbTop + thumbHeight ? "┃" : "│", Foreground = ScrollBarColor, ZIndex = AnchorPoint == AnchorPoint.Center ? 11 : 0 });
                }
                return parts;
            }
        }
    }
    public class TuiBorder
    {
        public TuiLabel? Title { get; set; }
        public Color Color { get; set; } = Color.White;
        public ITuiControl? Child { get; set; }
        public IEnumerable<RenderPart> Render()
        {
            if (Child == null) return Array.Empty<RenderPart>();
            int w = Math.Max(Child.MaxWidth, 0), h = Math.Max(Child.MaxHeight, 0);
            int x = Child.X - (Child.AnchorPoint == AnchorPoint.Center ? w / 2 : 0);
            int y = Child.Y - (Child.AnchorPoint == AnchorPoint.Center ? h / 2 : 0);
            List<RenderPart> parts = Child.Render().ToList();
            parts.Add(new() { X = x - 1, Y = y - 1, Text = "┌" + new string('─', w) + "┐", Foreground = Color, ZIndex = 12 });
            parts.Add(new() { X = x - 1, Y = y + h, Text = "└" + new string('─', w) + "┘", Foreground = Color, ZIndex = 12 });
            for (int row = 0; row < h; row++)
            {
                parts.Add(new() { X = x - 1, Y = y + row, Text = "│", Foreground = Color, ZIndex = 12 });
                parts.Add(new() { X = x + w, Y = y + row, Text = "│", Foreground = Color, ZIndex = 12 });
            }
            if (Title != null) parts.Add(new() { X = x, Y = y - 1, Text = Rendering.TerminalText.Clip(Title.Text, 0, w), Foreground = Title.Style.Foreground, ZIndex = 13 });
            return parts;
        }
    }
}
