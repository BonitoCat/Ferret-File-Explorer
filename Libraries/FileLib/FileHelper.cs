namespace FileLib;

public static class FileHelper
{
    public static void Create(string path)
    {
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
    }
}
