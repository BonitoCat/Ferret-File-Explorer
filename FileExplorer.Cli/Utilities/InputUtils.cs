using FileExplorer.Cli.Context;

namespace FileExplorer.Cli.Utilities;

using System;
using Cli.Context;
using TuiLib.Helpers;

public static class InputUtils
{
    private static bool TryPositionCursor(int x, int y)
    {
        try
        {
            int width = Console.WindowWidth;
            int height = Console.WindowHeight;
            if (width <= 0 || height <= 0) return false;
            Console.SetCursorPosition(Math.Clamp(x, 0, width - 1), Math.Clamp(y, 0, height - 1));
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; } // Resized after measuring.
        catch (IOException) { return false; }
    }

    private static void DrainPendingInput()
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(300);
        DateTime quietUntil = DateTime.UtcNow.AddMilliseconds(60);
        while (DateTime.UtcNow < quietUntil && DateTime.UtcNow < deadline)
        {
            if (!Console.KeyAvailable)
            {
                Thread.Sleep(5);
                continue;
            }

            Console.ReadKey(true);
            quietUntil = DateTime.UtcNow.AddMilliseconds(60);
        }
    }

    public static string? Input(this EntryContext context, string prompt, string defaultText = "", bool mask = false)
    {
        return Input(context, prompt, defaultText, mask, null);
    }

    public static string? Input(this EntryContext context, string prompt, string defaultText, bool mask, int? selectLength)
    {
        if (!prompt.StartsWith(' '))
        {
            prompt = ' ' + prompt;
        }

        bool wasRenderingPaused = Program.PauseRendering;
        bool wasListeningPaused = Program.Listener.PauseListening;
        Program.PauseRendering = true;
        try
        {
            Program.Listener.PauseListening = true;
            Program.Listener.ClearKeyState();

            DrainPendingInput();

            TerminalHelper.ShowCursor();

            System.Text.StringBuilder builder = new(defaultText);
            string? result = null;

            int cursor = selectLength.HasValue ? Math.Clamp(selectLength.Value, 0, builder.Length) : builder.Length;
            int? selectionStart = selectLength.HasValue ? 0 : null;

            string label = prompt.Trim().TrimEnd(':').TrimEnd();

            void Redraw()
            {
                TerminalHelper.HideCursor();
                int width = Math.Max(Console.WindowWidth - 1, 0);
                int height = Console.WindowHeight;
                if (width == 0 || height <= 0) return;
                string head = $" {label} › ";
                if (head.Length > width) head = head[..width];
                int available = Math.Max(width - head.Length - 1, 0);
                string text = mask ? new string('*', builder.Length) : builder.ToString();
                int offset = Math.Clamp(cursor - available + 1, 0, text.Length);
                string visible = text.Substring(offset, Math.Min(available, text.Length - offset));

                System.Text.StringBuilder line = new();
                line.Append("\x1b[48;2;38;38;44m\x1b[1;38;2;120;180;255m");
                line.Append(head);
                line.Append("\x1b[22;38;2;235;235;235m");

                if (selectionStart.HasValue && selectionStart.Value != cursor)
                {
                    int selMin = Math.Clamp(Math.Min(cursor, selectionStart.Value) - offset, 0, visible.Length);
                    int selMax = Math.Clamp(Math.Max(cursor, selectionStart.Value) - offset, 0, visible.Length);
                    line.Append(visible[..selMin]);
                    line.Append("\x1b[7m").Append(visible[selMin..selMax]).Append("\x1b[27m");
                    line.Append(visible[selMax..]);
                }
                else
                {
                    line.Append(visible);
                }

                line.Append(new string(' ', Math.Max(width - head.Length - visible.Length, 0)));
                line.Append("\x1b[0m");

                if (!TryPositionCursor(0, Math.Max(height - 2, 0))) return;
                Console.Write("\x1b[2K" + line);
                if (height > 1 && TryPositionCursor(0, height - 1))
                {
                    string hint = "  Enter confirm   Esc cancel";
                    Console.Write("\x1b[2K\x1b[38;2;110;110;120m" + hint[..Math.Min(hint.Length, width)] + "\x1b[0m");
                }

                TryPositionCursor(head.Length + cursor - offset, Math.Max(height - 2, 0));
                if (!selectionStart.HasValue || selectionStart.Value == cursor)
                {
                    TerminalHelper.ShowCursor();
                }
            }

            void DeleteSelection()
            {
                if (selectionStart.HasValue && selectionStart.Value != cursor)
                {
                    int selMin = Math.Min(cursor, selectionStart.Value);
                    int selMax = Math.Max(cursor, selectionStart.Value);
                    builder.Remove(selMin, selMax - selMin);
                    cursor = selMin;
                }
                selectionStart = null;
            }

            Redraw();

            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                bool isShift = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0;
                bool isCtrl = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0;

                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    if (Console.KeyAvailable)
                    {
                        System.Threading.Thread.Sleep(15);
                        string seq = "";
                        while (Console.KeyAvailable)
                        {
                            seq += Console.ReadKey(true).KeyChar;
                        }

                        ConsoleKey mappedKey = ConsoleKey.None;
                        if (seq.EndsWith("~"))
                        {
                            if (seq.StartsWith("[3")) mappedKey = ConsoleKey.Delete;
                            else if (seq.StartsWith("[1") || seq.StartsWith("[7")) mappedKey = ConsoleKey.Home;
                            else if (seq.StartsWith("[4") || seq.StartsWith("[8")) mappedKey = ConsoleKey.End;
                        }
                        else if (seq.EndsWith("D")) mappedKey = ConsoleKey.LeftArrow;
                        else if (seq.EndsWith("C")) mappedKey = ConsoleKey.RightArrow;
                        else if (seq.EndsWith("H")) mappedKey = ConsoleKey.Home;
                        else if (seq.EndsWith("F")) mappedKey = ConsoleKey.End;

                        if (mappedKey != ConsoleKey.None)
                        {
                            isShift = seq.Contains(";2") || seq.Contains(";4") || seq.Contains(";6") || seq.Contains(";8");
                            isCtrl = seq.Contains(";5") || seq.Contains(";6") || seq.Contains(";7") || seq.Contains(";8");
                            bool alt = seq.Contains(";3") || seq.Contains(";4") || seq.Contains(";7") || seq.Contains(";8");

                            keyInfo = new ConsoleKeyInfo('\0', mappedKey, isShift, alt, isCtrl);
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        result = null;
                        break;
                    }
                }

                void HandleMovement(Action moveAction)
                {
                    if (isShift)
                    {
                        if (selectionStart == null) selectionStart = cursor;
                    }
                    else
                    {
                        selectionStart = null;
                    }
                    moveAction();
                }

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    result = builder.ToString();
                    break;
                }

                bool hasSelection = selectionStart.HasValue && selectionStart.Value != cursor;
                if (hasSelection && !isShift && !isCtrl && keyInfo.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
                {
                    cursor = keyInfo.Key == ConsoleKey.LeftArrow ? Math.Min(cursor, selectionStart!.Value) : Math.Max(cursor, selectionStart!.Value);
                    selectionStart = null;
                    Redraw();
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.LeftArrow)
                {
                    HandleMovement(() =>
                    {
                        if (isCtrl)
                        {
                            while (cursor > 0 && builder[cursor - 1] == ' ') cursor--;
                            while (cursor > 0 && builder[cursor - 1] != ' ') cursor--;
                        }
                        else if (cursor > 0)
                        {
                            cursor--;
                        }
                    });
                    Redraw();
                }
                else if (keyInfo.Key == ConsoleKey.RightArrow)
                {
                    HandleMovement(() =>
                    {
                        if (isCtrl)
                        {
                            while (cursor < builder.Length && builder[cursor] == ' ') cursor++;
                            while (cursor < builder.Length && builder[cursor] != ' ') cursor++;
                        }
                        else if (cursor < builder.Length)
                        {
                            cursor++;
                        }
                    });
                    Redraw();
                }
                else if (keyInfo.Key == ConsoleKey.Home)
                {
                    HandleMovement(() => cursor = 0);
                    Redraw();
                }
                else if (keyInfo.Key == ConsoleKey.End)
                {
                    HandleMovement(() => cursor = builder.Length);
                    Redraw();
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (selectionStart.HasValue && selectionStart.Value != cursor)
                    {
                        DeleteSelection();
                    }
                    else if (cursor > 0)
                    {
                        if (isCtrl)
                        {
                            int start = cursor;
                            while (cursor > 0 && builder[cursor - 1] == ' ') cursor--;
                            while (cursor > 0 && builder[cursor - 1] != ' ') cursor--;
                            builder.Remove(cursor, start - cursor);
                        }
                        else
                        {
                            builder.Remove(cursor - 1, 1);
                            cursor--;
                        }
                    }
                    selectionStart = null;
                    Redraw();
                }
                else if (keyInfo.Key == ConsoleKey.Delete)
                {
                    if (selectionStart.HasValue && selectionStart.Value != cursor)
                    {
                        DeleteSelection();
                    }
                    else if (cursor < builder.Length)
                    {
                        if (isCtrl)
                        {
                            int end = cursor;
                            while (end < builder.Length && builder[end] == ' ') end++;
                            while (end < builder.Length && builder[end] != ' ') end++;
                            builder.Remove(cursor, end - cursor);
                        }
                        else
                        {
                            builder.Remove(cursor, 1);
                        }
                    }
                    selectionStart = null;
                    Redraw();
                }
                else if (keyInfo.KeyChar != '\0' && !char.IsControl(keyInfo.KeyChar))
                {
                    DeleteSelection();
                    builder.Insert(cursor, keyInfo.KeyChar);
                    cursor++;
                    Redraw();
                }
            }

            return result;
        }
        finally
        {
            try
            {
                TerminalHelper.HideCursor();
                Program.Renderer.ClearScreen();
            }
            finally
            {
                Program.Listener.ClearKeyState();
                Program.Listener.ClearConsumedKeys();
                try { Program.Listener.PauseListening = wasListeningPaused; }
                finally { Program.PauseRendering = wasRenderingPaused; }
            }
        }
    }
}
