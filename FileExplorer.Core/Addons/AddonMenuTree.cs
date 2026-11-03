namespace FileExplorer.Core.Addons;

public sealed class AddonMenuTree
{
    private const int MaxDepth = 8;

    private readonly List<AddonAction> _actions;
    private readonly Dictionary<string, AddonAction> _groups;
    private readonly AddonContext? _context;
    private readonly Dictionary<AddonAction, bool> _visible = new();

    public AddonMenuTree(IEnumerable<AddonAction> actions, AddonContext? context)
    {
        _actions = actions.ToList();
        _groups = _actions
            .Where(a => a.IsGroup)
            .GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _context = context;
    }

    public string? GetParent(AddonAction action)
    {
        if (action.Parent == null)
        {
            return null;
        }

        if (AddonAction.BuiltInParents.Contains(action.Parent))
        {
            return action.Parent;
        }

        return _groups.ContainsKey(action.Parent) ? action.Parent : null;
    }

    public List<AddonAction> GetChildren(string? parent)
    {
        return _actions
            .Where(a => string.Equals(GetParent(a), parent, StringComparison.OrdinalIgnoreCase) && IsVisible(a))
            .ToList();
    }

    public IEnumerable<string> GetBuiltInParents()
    {
        return _actions
            .Select(GetParent)
            .OfType<string>()
            .Where(AddonAction.BuiltInParents.Contains)
            .Distinct();
    }

    public bool IsVisible(AddonAction action)
    {
        return IsVisible(action, 0);
    }

    private bool IsVisible(AddonAction action, int depth)
    {
        if (_visible.TryGetValue(action, out bool known))
        {
            return known;
        }

        if (depth > MaxDepth || (_context != null && !action.Matches(_context)))
        {
            return false;
        }

        bool visible = !action.IsGroup || _actions.Any(a => string.Equals(GetParent(a), action.Key, StringComparison.OrdinalIgnoreCase) && IsVisible(a, depth + 1));
        _visible[action] = visible;
        return visible;
    }
}
