using System.Runtime.ExceptionServices;

namespace LoggerLib;

public static class Logger
{
    private static readonly object Gate = new();
    private static string? _filePath;
    private static bool _logFirstChance;
    [ThreadStatic] private static bool _writing;

    public static string LogDir { get; set; } = Path.Combine(Path.GetTempPath(), "fe", "logs");
    public static int KeepLogs { get; set; } = 20;
    public static bool LogDebug { get; set; }
    public static bool LogToTerminal { get; set; }
    public static bool LogFirstChance
    {
        get => _logFirstChance;
        set
        {
            lock (Gate)
            {
                if (_logFirstChance == value) return;
                _logFirstChance = value;
                if (value) AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
                else AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            }
        }
    }

    public static void CreateFile()
    {
        lock (Gate)
        {
            if (_writing) return;
            _writing = true;
            try
            {
                Directory.CreateDirectory(LogDir);
                string path = Path.Combine(LogDir, $"ferret-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.log");
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
                _filePath = path;
                var ownLogs = Directory.EnumerateFiles(LogDir, "ferret-*.log")
                    .Where(IsOwnLog).OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToArray();
                foreach (string old in ownLogs.Skip(Math.Max(1, KeepLogs)))
                {
                    if (old == _filePath) continue;
                    try { File.Delete(old); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception) { _filePath = null; }
            finally { _writing = false; }
        }
    }

    public static Task CreateFileAsync()
    {
        CreateFile();
        return Task.CompletedTask;
    }

    public static Task LogS(string message) => Write("START", message);
    public static Task LogI(string message) => Write("INFO", message);
    public static Task LogW(string message) => Write("WARN", message);
    public static Task LogE(string message) => Write("ERROR", message);

    private static Task Write(string level, string message)
    {
        lock (Gate)
        {
            if (_writing) return Task.CompletedTask;
            _writing = true;
            try
            {
                string line = $"{DateTimeOffset.Now:O} [{level}] {message}";
                if (_filePath != null)
                {
                    try { File.AppendAllText(_filePath, line + Environment.NewLine); }
                    catch (Exception) { }
                }
                if (LogToTerminal)
                {
                    try { Console.Error.WriteLine(line); }
                    catch (Exception) { }
                }
            }
            finally { _writing = false; }
        }
        return Task.CompletedTask;
    }

    private static bool IsOwnLog(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name.Length == 63 && name.StartsWith("ferret-", StringComparison.Ordinal)
            && DateTime.TryParseExact(name.Substring(7, 23), "yyyyMMddTHHmmssfffffffZ",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)
            && name[30] == '-' && Guid.TryParseExact(name.Substring(31), "N", out _);
    }

    private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
    {
        if (!_writing && LogDebug) _ = Write("DEBUG", e.Exception.ToString());
    }
}
