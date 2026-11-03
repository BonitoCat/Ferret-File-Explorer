using TuiLib;
using TuiLib.Components;
using TuiLib.Controls;
using TuiLib.Rendering;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}
var list = new TuiListBox<string> { MaxHeight = 3, ViewRange = 3, MaxWidth = 8, ScrollOvershoot = 1, UseDefaultMarker = false };
list.MoveSelected(1);
Check(list.SelectedItem == null && list.SelectedIndex == -1, "empty selection");
list.AddItemRange(Enumerable.Range(0, 10).Select(i => i.ToString()));
list.MoveSelected(5);
Check(list.SelectedItem?.Value == "5" && list.ViewIndex == 4, "selection scroll margin");
list.MoveSelected(99);
Check(list.SelectedIndex == 9 && list.ViewIndex == 7, "selection end clamp");
Check(!list.Scroll(1) && list.Scroll(-99) && list.ViewIndex == 0, "scroll clamp");
list.TryScrollTo(list.Items[8]);
Check(list.GetViewItems().Any(i => i.Value == "8"), "scroll to item");
list.ClearItems();
Check(list.GetViewItems().Count == 0 && list.SelectedItem == null, "clear resets selection");
list.MaxHeight = -2;
Check(!list.Render().Any(), "negative dimensions");
Check(Color.TrimAnsi(TerminalText.Clip("\x1b[31mA界B", 0, 3)) == "A界", "wide clipping");
Check(Color.TrimAnsi(TerminalText.Clip("e\u0301😀x", 0, 3)) == "e\u0301😀", "grapheme clipping");
Check(TerminalText.Clip("a\x1b[2J\x1b]0;bad\aB", 0, 8) == "aB", "discard terminal injection");
string[] frame = TuiRenderer.Compose(new[] {
    new RenderPart { X = -1, Text = "abc" },
    new RenderPart { X = 1, Text = "X", ZIndex = 2 },
    new RenderPart { X = 0, Y = 1, Text = "界ab" },
    new RenderPart { X = 1, Y = 1, Text = "!", ZIndex = 1 },
    new RenderPart { X = 0, Y = -1, Text = "hidden" }
}, 3, 2);
Check(Color.TrimAnsi(frame[0]) == "bX ", "negative clip and stacking");
Check(Color.TrimAnsi(frame[1]) == " !a", "wide glyph overwrite");
Check(TuiRenderer.Compose(Array.Empty<RenderPart>(), 0, 0).Length == 0, "zero terminal");
var popup = new TuiListBox<string> { X = 5, Y = 4, MaxWidth = 4, MaxHeight = 2, ViewRange = 2, AnchorPoint = AnchorPoint.Center };
popup.AddItem("row");
var border = new TuiBorder { Child = popup, Title = new(" long title ") };
Check(border.Render().Any(p => p.X == 2 && p.Y == 2 && p.Text.StartsWith('┌')), "center border coordinates");
Check(Color.TrimAnsi(TuiRenderer.Compose(new[] { new RenderPart { X = 1, Text = "界" }, new RenderPart { Text = "語", ZIndex = 1 } }, 3, 1)[0]) == "語 ", "overlapping wide glyphs");
Check(!TerminalText.Clip("x\u009b2J\u009d52;data\u009cy", 0, 100).Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Control), "C1 terminal controls filtered");
Check(Color.TrimAnsi(TerminalText.Clip("❤️🇦🇹1️⃣x", 0, 6)) == "❤️🇦🇹1️⃣", "emoji presentation widths");
Check(TuiRenderer.Compose(new[] { new RenderPart { Text = "\x1b[4munderlined" } }, 10, 1)[0].Contains("\x1b[4m"), "underline retained");
var popupFrame = TuiRenderer.Compose(new[] { new RenderPart { Y = 3, Text = "0123456789" }, new RenderPart { Y = 4, Text = "0123456789" } }.Concat(border.Render()), 10, 7);
Check(Color.TrimAnsi(popupFrame[4]) == "01│    │89", "popup blanks cover base frame");
if (OperatingSystem.IsLinux())
{
    string directory = Path.Combine(Path.GetTempPath(), "ferret-terminal-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    string terminal = Path.Combine(directory, "fake terminal"), result = Path.Combine(directory, "arguments");
    string? previous = Environment.GetEnvironmentVariable("FERRET_TERMINAL_TEST_RESULT");
    try
    {
        File.WriteAllText(terminal, "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$FERRET_TERMINAL_TEST_RESULT\"\n");
        File.SetUnixFileMode(terminal, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("FERRET_TERMINAL_TEST_RESULT", result);
        var window = new Window { WorkingDirectory = directory };
        window.LaunchInfo.ExecutablePath = terminal;
        window.Launch("\"/tmp/a directory/my executable\"");
        Check(SpinWait.SpinUntil(() => File.Exists(result) && File.ReadAllText(result).EndsWith('\n'), 5000), "fake terminal completion");
        Check(File.ReadAllLines(result).SequenceEqual(new[] { "-e", "/tmp/a directory/my executable" }), "quoted executable remains one argument");
        File.Delete(result);
        window.Launch("bash -c \"sudo '/tmp/a directory/my executable'\"");
        Check(SpinWait.SpinUntil(() => File.Exists(result) && File.ReadAllText(result).EndsWith('\n'), 5000), "fake shell terminal completion");
        Check(File.ReadAllLines(result).SequenceEqual(new[] { "-e", "bash", "-c", "sudo '/tmp/a directory/my executable'" }), "shell command argument preserved");
    }
    finally
    {
        Environment.SetEnvironmentVariable("FERRET_TERMINAL_TEST_RESULT", previous);
        Directory.Delete(directory, true);
    }
}
Console.WriteLine("TuiLib checks passed (Linux additionally verifies terminal arguments with a fake executable).");
