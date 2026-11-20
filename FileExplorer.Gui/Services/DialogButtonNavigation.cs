using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using static Avalonia.Input.InputElement;
using static Avalonia.Interactivity.RoutingStrategies;

namespace FileExplorer.Gui.Services;

public static class DialogButtonNavigation
{
    public static void Attach(Window window)
    {
        window.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None)
            {
                return;
            }

            int direction = e.Key switch
            {
                Key.Left or Key.Up => -1,
                Key.Right or Key.Down => 1,
                _ => 0,
            };
            if (direction == 0)
            {
                return;
            }

            if (window.FocusManager?.GetFocusedElement() is not Button focused || focused is ToggleButton)
            {
                return;
            }

            if (focused.Parent is not Panel panel)
            {
                return;
            }

            List<Button> buttons = panel.Children
                .OfType<Button>()
                .Where(button => button is not ToggleButton && button.IsVisible && button.IsEffectivelyEnabled && button.Focusable)
                .ToList();
            int index = buttons.IndexOf(focused);
            if (index < 0 || buttons.Count < 2)
            {
                return;
            }

            int next = (index + direction + buttons.Count) % buttons.Count;
            buttons[next].Focus(NavigationMethod.Directional);
            e.Handled = true;
        }, Tunnel);
    }
}
