using FileLib;
using LoggerLib;

string root = Path.Combine(Path.GetTempPath(), "ferret-library-tests-" + Guid.NewGuid().ToString("N"));
string? oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
string? oldConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
string? oldCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
Directory.CreateDirectory(root);
try
{
    string data = Path.Combine(root, "data");
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", data);
    Check(DirectoryHelper.GetAppDataDirPath(false) == data, "XDG data override");
    Check(!Directory.Exists(data), "XDG data path is not created automatically");
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
    Check(DirectoryHelper.GetAppConfigDirPath() == Path.Combine(root, "config"), "Config override");
    Check(!Directory.Exists(Path.Combine(root, "config")), "XDG config path is not created automatically");
    Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(root, "cache"));
    Check(DirectoryHelper.GetCacheDirPath() == Path.Combine(root, "cache"), "Cache override");
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", "relative-path");
    Check(DirectoryHelper.GetAppDataDirPath(false) == "relative-path", "XDG override is returned verbatim");
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
    if (!OperatingSystem.IsWindows())
    {
        Check(DirectoryHelper.GetAppDataDirPath(false) == Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "Linux fallback");
    }

    string nested = Path.Combine(root, "created", "nested");
    DirectoryHelper.CreateDir(nested);
    Check(Directory.Exists(nested), "CreateDir creates nested folders");

    string file = Path.Combine(root, "keep.txt");
    File.WriteAllText(file, "keep me");
    FileHelper.Create(file);
    Check(File.Exists(file), "Create keeps an existing file");
    Check(File.ReadAllText(file).Length == 0, "Create truncates an existing file");

    Logger.LogDir = Path.Combine(root, "logs");
    Logger.KeepLogs = 2;
    Directory.CreateDirectory(Logger.LogDir);
    string unrelated = Path.Combine(Logger.LogDir, "ferret-important.log");
    File.WriteAllText(unrelated, "keep");
    Logger.CreateFile();
    await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => Logger.LogI("message-" + i))));
    string writtenLog = Directory.EnumerateFiles(Logger.LogDir).Single(p => p != unrelated);
    string[] lines = File.ReadAllLines(writtenLog);
    string[] logLines = lines.Where(l => l.Contains("message-")).ToArray();
    Check(logLines.Length == 100 && Enumerable.Range(0, 100).All(i => logLines.Count(l => l.EndsWith("message-" + i)) == 1),
        "Concurrent writes preserve each message once");
    await Logger.CreateFileAsync();
    Logger.CreateFile();
    Check(Directory.EnumerateFiles(Logger.LogDir).Count() == Logger.KeepLogs, "Retention keeps at most KeepLogs files");
    Logger.LogFirstChance = false;
    Logger.LogDir = file;
    Logger.CreateFile();
    bool loggingFailed = false;
    try
    {
        await Logger.LogE("unwritable destination");
    }
    catch (InvalidOperationException)
    {
        loggingFailed = true;
    }
    Check(loggingFailed, "Logging without an open log file fails loudly");
    Console.WriteLine("PASS: FileLib and LoggerLib checks");
}
finally
{
    Logger.LogFirstChance = false;
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", oldConfig);
    Environment.SetEnvironmentVariable("XDG_CACHE_HOME", oldCache);
    Directory.Delete(root, true);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
}
