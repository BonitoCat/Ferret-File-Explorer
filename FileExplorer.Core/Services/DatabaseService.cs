using Microsoft.Data.Sqlite;

namespace FileExplorer.Core.Services;

public static class DatabaseService
{
    private const string DbFile = "database.sqlite";
    private const string ConnectionString =
        "Data Source=database.sqlite;Foreign Keys=True;";

    public static void Initialize()
    {
        bool exists = File.Exists(DbFile);

        using SqliteConnection con = new(ConnectionString);
        con.Open();
        
        FileSystemWatcher watcher = new(DbFile);
    }
}
