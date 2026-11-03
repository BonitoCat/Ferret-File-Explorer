namespace InputLib.EventArgs;

public sealed class KeyDownEventArgs : System.EventArgs
{
    public Key Key { get; }
    public bool Continuous { get; }

    public KeyDownEventArgs(Key key, bool continuous = false)
    {
        Key = key;
        Continuous = continuous;
    }
}
