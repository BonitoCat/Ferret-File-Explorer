using System.Collections;
using System.Reflection;

using FileExplorer.Cli.Context;

namespace FileExplorer.Cli.Utilities;

using System;
using Cli.Context;

public static class InputUtils
{
    public static string? Input(this EntryContext context, string prompt, string defaultText = "")
    {
        if (!prompt.StartsWith(' '))
        {
            prompt = ' ' + prompt;
        }
        
        Program.PauseRendering = true;
        Program.Listener.PauseListening = true;
        Program.Listener.ClearKeyState();
        
        while (Console.KeyAvailable)
        {
            Console.ReadKey(true);
        }

        Console.CursorVisible = true;
        
        System.Text.StringBuilder builder = new(defaultText);
        string? result = null;

        int cursor = builder.Length;
        int? selectionStart = null;

        void Redraw()
        {
            Console.CursorVisible = false;
            Console.SetCursorPosition(0, Console.WindowHeight - 2);
            Console.Write("\x1b[2K");
            Console.Write(prompt);
            
            string text = builder.ToString();
            if (selectionStart.HasValue && selectionStart.Value != cursor)
            {
                int selMin = Math.Min(cursor, selectionStart.Value);
                int selMax = Math.Max(cursor, selectionStart.Value);

                Console.Write(text.Substring(0, selMin));
                Console.Write("\x1b[7m"); // Invert colors
                Console.Write(text.Substring(selMin, selMax - selMin));
                Console.Write("\x1b[27m"); // Reset invert
                Console.Write(text.Substring(selMax));
            }
            else
            {
                Console.Write(text);
            }
            
            Console.SetCursorPosition(prompt.Length + cursor, Console.WindowHeight - 2);
            Console.CursorVisible = true;
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

        Program.Listener.ConsumeNextKeyDown(InputLib.Key.Enter);
        Program.Listener.ConsumeNextKeyDown(InputLib.Key.Escape);
        Program.Listener.PauseListening = false;
        Program.PauseRendering = false;

        Console.CursorVisible = false;
        
        Console.SetCursorPosition(0, Console.WindowHeight - 2);
        Console.Write(new string(' ', Console.WindowWidth));
        
        Console.SetCursorPosition(0, Console.WindowHeight - 1);
        Console.Write(new string(' ', Console.WindowWidth));
        
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            try
            {
                FieldInfo? field = typeof(InputLib.InputListener).GetField("_consumeDownKeys", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    IList? list = field.GetValue(Program.Listener) as IList;
                    list?.Clear();
                }
            }
            catch { }
        });
        
        return result;
    }
}
