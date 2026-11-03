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
    Check(DirectoryHelper.GetAppDataDirPath(false) == data && !Directory.Exists(data), "Noncreating XDG path");
    Check(DirectoryHelper.GetAppDataDirPath() == data && Directory.Exists(data), "XDG path creation");
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
    Check(DirectoryHelper.GetAppConfigDirPath() == Path.Combine(root, "config"), "Config override");
    Environment.SetEnvironmentVariable("XDG_CACHE_HOME", Path.Combine(root, "cache"));
    Check(DirectoryHelper.GetCacheDirPath() == Path.Combine(root, "cache"), "Cache override");
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", "relative-path");
    Check(Path.IsPathFullyQualified(DirectoryHelper.GetAppDataDirPath(false)), "Reject relative XDG override");
    if (!OperatingSystem.IsWindows())
    {
        Check(DirectoryHelper.GetAppDataDirPath(false) == Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "Linux fallback");
    }
    string file = Path.Combine(root, "keep.txt");
    File.WriteAllText(file, "keep me");
    FileHelper.Create(file);
    Check(File.ReadAllText(file) == "keep me", "Create preserves existing file");
    FileHelper.Create(Path.Combine(root, "new.txt"));
    Check(File.Exists(Path.Combine(root, "new.txt")), "Create new file");

    Logger.LogDir = Path.Combine(root, "logs");
    Logger.KeepLogs = 2;
    Directory.CreateDirectory(Logger.LogDir);
    string unrelated = Path.Combine(Logger.LogDir, "ferret-important.log");
    File.WriteAllText(unrelated, "keep");
    Logger.CreateFile();
    await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => Logger.LogI("message-" + i))));
    string writtenLog = Directory.EnumerateFiles(Logger.LogDir).Single(p => p != unrelated);
    string[] lines = File.ReadAllLines(writtenLog);
    Check(lines.Length == 100 && Enumerable.Range(0, 100).All(i => lines.Count(l => l.EndsWith(" message-" + i)) == 1),
        "Concurrent writes preserve each message once");
    await Logger.CreateFileAsync();
    Logger.CreateFile();
    Check(Directory.EnumerateFiles(Logger.LogDir).Count() == 3, "Retention keeps two own logs");
    Check(File.ReadAllText(unrelated) == "keep", "Retention preserves unrelated logs");
    Logger.LogFirstChance = true;
    Logger.LogDebug = true;
    Logger.LogDir = file;
    Logger.CreateFile();
    await Logger.LogE("unwritable destination");
    Logger.LogFirstChance = false;
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
