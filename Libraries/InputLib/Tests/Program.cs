using InputLib;
using InputLib.EventArgs;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

using InputListener listener = new();
List<KeyDownEventArgs> events = [];
Key[] held = [];
listener.OnKeyDown += (_, e) =>
{
    events.Add(e);
    held = listener.GetHeldKeys().ToArray();
};

listener.Dispatch(new('A', ConsoleKey.A, true, true, true), 1000);
Check(events.Single().Key == Key.A && events[0].Key.Char == 'A', "Casing preserves identity and character");
Check(held.Contains(Key.Ctrl) && held.Contains(Key.Shift) && held.Contains(Key.Alt), "Modifiers accompany stroke");
Check(!listener.GetHeldKeys().Any(), "No phantom held keys between terminal strokes");
listener.Dispatch(new('é', ConsoleKey.None, false, false, false), 1010);
Check(events[^1].Key.Char == 'é', "Unicode character preserved");

listener.ConsumeNextKeyDown(Key.Enter);
int before = events.Count;
listener.Dispatch(new('\r', ConsoleKey.Enter, false, false, false), 1020);
Check(events.Count == before, "One consumed event suppressed");
listener.Dispatch(new('\r', ConsoleKey.Enter, false, false, false), 1030);
Check(events.Count == before + 1, "Consumption expires after one stroke");
listener.ConsumeNextKeyDown(Key.Escape);
listener.ClearConsumedKeys();
listener.Dispatch(new('\u001b', ConsoleKey.Escape, false, false, false), 1040);
Check(events[^1].Key == Key.Escape, "Explicit clear resets consumption");

listener.PauseListening = true;
before = events.Count;
listener.Dispatch(new('b', ConsoleKey.B, false, false, false), 1050);
Check(events.Count == before, "Paused listener does not dispatch");
listener.PauseListening = false;
listener.Dispatch(new('b', ConsoleKey.B, false, false, false), 1060);
listener.Dispatch(new('b', ConsoleKey.B, false, false, false), 1070);
Check(events.Count == before + 2 && !events[^1].Continuous, "Repeated letters remain available for typeahead");

ConsoleKeyInfo down = new('\0', ConsoleKey.DownArrow, false, false, false);
listener.Dispatch(down, 2000);
listener.Dispatch(down, 2050);
Check(events[^1].Continuous, "Navigation repeat detected");
before = events.Count;
listener.Dispatch(down, 2055);
Check(events.Count == before, "Repeat interval throttles navigation");
listener.ClearKeyState();
listener.Dispatch(down, 2060);
Check(!events[^1].Continuous, "Reset ends navigation repeat");

// A handler can synchronously hand input to a dialog without self-deadlocking.
listener.OnKeyDown += (_, _) => listener.PauseListening = true;
listener.Dispatch(new('c', ConsoleKey.C, false, false, false), 3000);
Check(listener.PauseListening, "Pause inside callback is supported");

using InputListener failing = new();
Exception expected = new InvalidOperationException("callback test");
failing.OnKeyDown += (_, _) => throw expected;
try { failing.Dispatch(new('a', ConsoleKey.A, false, false, false)); }
catch (InvalidOperationException error) { Check(ReferenceEquals(error, expected), "Original callback failure retained"); }
Check(!failing.GetHeldKeys().Any(), "Fault releases held state");
bool observed = false;
try { failing.ThrowIfFaulted(); }
catch (InvalidOperationException error) { observed = ReferenceEquals(error, expected); }
Check(observed, "Main loop can observe callback failure");

using InputListener handoff = new();
using ManualResetEventSlim entered = new();
using ManualResetEventSlim release = new();
handoff.OnKeyDown += (_, _) => { entered.Set(); release.Wait(); };
Task dispatch = Task.Run(() => handoff.Dispatch(new('x', ConsoleKey.X, false, false, false)));
Check(entered.Wait(2000), "Dispatch entered");
Task pause = Task.Run(() => handoff.PauseListening = true);
Check(!pause.Wait(50), "Pause waits for active dispatch before returning ownership");
release.Set();
Check(Task.WaitAll([dispatch, pause], 2000), "Handoff finishes without deadlock");
Check(handoff.PauseListening, "Handoff leaves reader paused");
handoff.PauseListening = false;
handoff.ThrowIfFaulted();

Console.WriteLine("InputLib checks passed (mapping, modifiers, Unicode, consumption, pause, repeat, callback faults and synchronized handoff).");
