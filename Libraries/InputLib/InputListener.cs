using System.Diagnostics;
using System.Runtime.ExceptionServices;
using InputLib.EventArgs;
using InputLib.PlatformListener;

namespace InputLib;

/// <summary>Reads only the foreground console. Terminals supply strokes, not key releases.</summary>
public class InputListener : IDisposable
{
    private static readonly object ConsoleGate = new();
    private static InputListener? _active;
    private static string? _terminalState;
    private readonly object _gate = new();
    private readonly HashSet<Key> _consumeDownKeys = [];
    private Key[] _heldKeys = [];
    private CancellationTokenSource? _cancellation;
    private Task? _reader;
    private int _readerThread;
    private bool _paused;
    private bool _disposed;
    private bool _originalControlC;
    private ExceptionDispatchInfo? _failure;
    private Key _lastKey;
    private ConsoleModifiers _lastModifiers;
    private long _lastStroke;
    private long _lastDispatch;

    static InputListener()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => EnableEcho();
        Console.CancelKeyPress += (_, _) => EnableEcho();
    }

    public event EventHandler<KeyDownEventArgs>? OnKeyDown;
    public int RepeatIntervalMs { get; set; } = 30;

    /// <summary>Call from the application's main loop to surface reader or callback failures.</summary>
    public void ThrowIfFaulted() => Volatile.Read(ref _failure)?.Throw();

    // The reader never holds this lock while waiting for a key, so setting true
    // guarantees Console.ReadKey is no longer running before a dialog takes over.
    public bool PauseListening
    {
        get { lock (_gate) return _paused; }
        set
        {
            lock (_gate)
            {
                _paused = value;
                if (_reader is { IsCompleted: false })
                    Console.TreatControlCAsInput = value ? _originalControlC : true;
                ClearKeyState();
            }
        }
    }

    public static InputListener New() => new TtyInputListener();

    public void StartListening()
    {
        lock (ConsoleGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_reader is { IsCompleted: false }) return;
            if (_active != null && _active != this)
                throw new InvalidOperationException("Another input listener owns the console.");
            if (Console.IsInputRedirected)
                throw new InvalidOperationException("Interactive input requires a terminal.");
            _originalControlC = Console.TreatControlCAsInput;
            _failure = null;
            if (!_paused) Console.TreatControlCAsInput = true;
            _active = this;
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _reader = Task.Factory.StartNew(() => ReadLoop(_cancellation.Token), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void ReadLoop(CancellationToken cancellation)
    {
        _readerThread = Environment.CurrentManagedThreadId;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                lock (_gate)
                {
                    if (!_paused && Console.KeyAvailable)
                        Dispatch(Console.ReadKey(intercept: true));
                }
                cancellation.WaitHandle.WaitOne(5);
            }
        }
        catch (Exception error)
        {
            Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(error));
            throw;
        }
        finally
        {
            ClearKeyState();
            Console.TreatControlCAsInput = _originalControlC;
            lock (ConsoleGate)
            {
                if (_active == this) _active = null;
            }
            EnableEcho();
        }
    }

    internal void Dispatch(ConsoleKeyInfo stroke, long? timestamp = null)
    {
        lock (_gate)
        {
            if (_paused) return;
            Key key = Key.FromConsoleKey(stroke);
            long now = timestamp ?? Environment.TickCount64;
            // Only navigation uses repeat semantics. Terminal streams cannot
            // distinguish a held character from intentional fast repeated letters.
            bool navigation = key == Key.ArrowUp || key == Key.ArrowDown || key == Key.ArrowLeft
                || key == Key.ArrowRight || key == Key.PageUp || key == Key.PageDown
                || key == Key.Home || key == Key.End;
            bool continuous = navigation && key == _lastKey && stroke.Modifiers == _lastModifiers
                && now - _lastStroke < 250 && _lastStroke != 0;
            _lastKey = key;
            _lastModifiers = stroke.Modifiers;
            _lastStroke = now;
            if (_consumeDownKeys.Remove(key)) return;
            if (continuous && now - _lastDispatch < Math.Max(0, RepeatIntervalMs)) return;
            _lastDispatch = now;
            List<Key> held = [key];
            if (stroke.Modifiers.HasFlag(ConsoleModifiers.Control)) held.Add(Key.Ctrl);
            if (stroke.Modifiers.HasFlag(ConsoleModifiers.Shift)) held.Add(Key.Shift);
            if (stroke.Modifiers.HasFlag(ConsoleModifiers.Alt)) held.Add(Key.Alt);
            _heldKeys = held.ToArray();
            try { OnKeyDown?.Invoke(this, new KeyDownEventArgs(key, continuous)); }
            catch (Exception error)
            {
                Volatile.Write(ref _failure, ExceptionDispatchInfo.Capture(error));
                throw;
            }
            finally { _heldKeys = []; }
        }
    }

    public IEnumerable<Key> GetHeldKeys()
    {
        lock (_gate) return _heldKeys.ToArray();
    }

    public void ClearKeyState()
    {
        lock (_gate)
        {
            _heldKeys = [];
            _lastStroke = 0;
            _lastDispatch = 0;
        }
    }

    public void ConsumeNextKeyDown(Key key)
    {
        lock (_gate) _consumeDownKeys.Add(key);
    }

    public void ClearConsumedKeys()
    {
        lock (_gate) _consumeDownKeys.Clear();
    }

    public void StopListening()
    {
        _cancellation?.Cancel();
        if (_reader != null && Environment.CurrentManagedThreadId != _readerThread)
            _reader.GetAwaiter().GetResult();
    }

    public static void Flush()
    {
        InputListener? listener;
        lock (ConsoleGate) listener = _active;
        if (listener != null)
        {
            lock (listener._gate) Drain();
        }
        else Drain();
    }

    private static void Drain()
    {
        if (Console.IsInputRedirected) return;
        while (Console.KeyAvailable) Console.ReadKey(intercept: true);
    }

    public static void DisableEcho()
    {
        if (!OperatingSystem.IsLinux() || Console.IsInputRedirected) return;
        lock (ConsoleGate)
        {
            if (_terminalState != null) return;
            _terminalState = RunStty("-g");
            if (_terminalState != null) RunStty("-echo");
        }
    }

    public static void EnableEcho()
    {
        lock (ConsoleGate)
        {
            if (_terminalState == null) return;
            RunStty(_terminalState);
            _terminalState = null;
        }
    }

    private static string? RunStty(string argument)
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo("stty")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            if (!process.WaitForExit(1000))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (IOException) { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { StopListening(); }
        finally
        {
            _cancellation?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
