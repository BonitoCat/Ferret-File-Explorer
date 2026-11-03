using System.Diagnostics;

namespace TuiLib.Helpers
{
    public static class TerminalHelper
    {
        private static bool _active;
        private static bool _previousControlC;
        public static void BeginSession()
        {
            if (_active || Console.IsOutputRedirected) return;
            _active = true;
            _previousControlC = Console.TreatControlCAsInput;
            Console.Write("\x1b[?1049h\x1b[?7l\x1b[?25l");
        }
        public static void EndSession()
        {
            if (!_active) return;
            _active = false;
            Console.TreatControlCAsInput = _previousControlC;
            Console.Write("\x1b[0m\x1b[?7h\x1b[?25h\x1b[?1049l");
        }
        public static void ShowCursor() { if (!Console.IsOutputRedirected) Console.Write("\x1b[?25h"); }
        public static void HideCursor() { if (!Console.IsOutputRedirected) Console.Write("\x1b[?25l"); }
    }
}

namespace TuiLib
{
    public sealed class WindowLaunchInfo
    {
        public string ExecutablePath { get; set; } = OperatingSystem.IsWindows() ? "cmd.exe" : "x-terminal-emulator";
    }
    public class Window
    {
        public string Title { get; set; } = "Ferret";
        public string? WorkingDirectory { get; set; }
        public WindowLaunchInfo LaunchInfo { get; } = new();
        public void Launch(string? arguments = null)
        {
            ProcessStartInfo info = new(LaunchInfo.ExecutablePath) { UseShellExecute = false };
            if (!string.IsNullOrEmpty(WorkingDirectory)) info.WorkingDirectory = WorkingDirectory;
            if (OperatingSystem.IsWindows()) info.Arguments = arguments ?? "";
            else if (!string.IsNullOrEmpty(arguments)) info.Arguments = "-e " + arguments;
            Process.Start(info);
        }
    }
    public sealed class WindowManager
    {
        private readonly List<Window> _windows = new();
        public static WindowManager Instance { get; } = new();
        public Window MainWindow { get; } = new();
        public void Add(Window window) { ArgumentNullException.ThrowIfNull(window); lock (_windows) _windows.Add(window); }
    }
}

