namespace InputLib;

/// <summary>A logical terminal key. Character casing does not change key identity.</summary>
public readonly struct Key : IEquatable<Key>
{
    private readonly string? _name;
    public char Char { get; }

    private Key(string name, char character = '\0')
    {
        _name = name;
        Char = character;
    }

    public static Key FromConsoleKey(ConsoleKeyInfo key)
    {
        string name = key.Key switch
        {
            ConsoleKey.UpArrow => "ArrowUp",
            ConsoleKey.DownArrow => "ArrowDown",
            ConsoleKey.LeftArrow => "ArrowLeft",
            ConsoleKey.RightArrow => "ArrowRight",
            ConsoleKey.Spacebar => "Space",
            ConsoleKey.None => char.ToUpperInvariant(key.KeyChar).ToString(),
            _ => key.Key.ToString()
        };
        return new Key(name, char.IsControl(key.KeyChar) ? '\0' : key.KeyChar);
    }

    public bool Equals(Key other) => _name == other._name;
    public override bool Equals(object? obj) => obj is Key other && Equals(other);
    public override int GetHashCode() => _name?.GetHashCode(StringComparison.Ordinal) ?? 0;
    public static bool operator ==(Key left, Key right) => left.Equals(right);
    public static bool operator !=(Key left, Key right) => !left.Equals(right);
    public override string ToString() => _name ?? "None";

    public static readonly Key None = default;
    public static readonly Key Ctrl = new("Ctrl"), LeftCtrl = new("LeftCtrl"), RightCtrl = new("RightCtrl");
    public static readonly Key Shift = new("Shift"), LeftShift = new("LeftShift"), RightShift = new("RightShift");
    public static readonly Key Alt = new("Alt"), AltGr = new("AltGr");
    public static readonly Key ArrowUp = new("ArrowUp"), ArrowDown = new("ArrowDown"), ArrowLeft = new("ArrowLeft"), ArrowRight = new("ArrowRight");
    public static readonly Key Enter = new("Enter"), Escape = new("Escape"), Space = new("Space", ' '), Tab = new("Tab");
    public static readonly Key Home = new("Home"), End = new("End"), PageUp = new("PageUp"), PageDown = new("PageDown"), Delete = new("Delete"), Backspace = new("Backspace");
    public static readonly Key F1 = new("F1"), F2 = new("F2"), F3 = new("F3"), F4 = new("F4"), F5 = new("F5"), F6 = new("F6"), F7 = new("F7"), F8 = new("F8"), F9 = new("F9"), F10 = new("F10"), F11 = new("F11"), F12 = new("F12");
    public static readonly Key A = new("A", 'a'), B = new("B", 'b'), C = new("C", 'c'), D = new("D", 'd'), E = new("E", 'e'), F = new("F", 'f'), G = new("G", 'g'), H = new("H", 'h'), I = new("I", 'i'), J = new("J", 'j'), K = new("K", 'k'), L = new("L", 'l'), M = new("M", 'm'), N = new("N", 'n'), O = new("O", 'o'), P = new("P", 'p'), Q = new("Q", 'q'), R = new("R", 'r'), S = new("S", 's'), T = new("T", 't'), U = new("U", 'u'), V = new("V", 'v'), W = new("W", 'w'), X = new("X", 'x'), Y = new("Y", 'y'), Z = new("Z", 'z');
}
