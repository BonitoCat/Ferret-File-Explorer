# InputLib

Foreground terminal input for Ferret on .NET 8. `New()` and `TtyInputListener`
use the same console reader; no device access or global keyboard hooks are used.
Only one listener can own the console. `PauseListening = true` waits for the
current read/dispatch to finish, so dialogs can then use `Console.ReadKey`.
Callbacks can pause the listener themselves. Stop/dispose the listener on exit.
Call `ThrowIfFaulted()` each iteration of the main render loop so a failed reader
or key callback exits through the application's normal error/cleanup path.
While listening, Ctrl+C is a key stroke for Ferret's Copy action, rather than a
termination signal. Pausing or stopping restores the previous Ctrl+C behavior.

`GetHeldKeys()` describes the stroke currently being dispatched, including the
modifiers reported by .NET. Terminals do not transmit reliable key releases,
left/right modifier identity, or every modified combination. Ctrl+Enter often
arrives as Enter and Shift+Space as Space. Ferret provides Shift+Enter for
metadata, Ctrl+J for size, and function keys for several other actions; terminal
configuration can still intercept a shortcut before the application sees it.

.NET decodes terminal escape sequences, navigation/function keys and modifier
sequences. Normal Unicode text retains its UTF-16 character in `Key.Char`.
Supplementary characters may arrive as separate UTF-16 surrogate strokes.
`Continuous` is inferred only for successive navigation strokes within 250 ms;
`RepeatIntervalMs` throttles those strokes. It cannot identify physical key holds.
Repeated text characters are never suppressed by this heuristic.

On Linux, echo settings are captured with `stty -g`, disabled, and restored on
stop, process exit and Ctrl+C. The console reader itself uses intercepted reads.
`stty` absence is tolerated because intercepted .NET reads do not echo. Forced
termination such as SIGKILL cannot run terminal cleanup.

Run dependency-free checks with:

```
dotnet run --project Libraries/InputLib/Tests/InputLib.Tests.csproj
```

These checks inject decoded console strokes; real Linux terminal parsing,
resize integration and terminal-state restoration require a terminal smoke test.
