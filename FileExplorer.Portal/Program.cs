using Tmds.DBus.Protocol;

namespace FileExplorer.Portal;

internal static class Program
{
    private const string BusName = "org.freedesktop.impl.portal.desktop.ferret";

    public static async Task<int> Main(string[] args)
    {
        string? address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address))
        {
            await Console.Error.WriteLineAsync("No D-Bus session bus available.");
            return 1;
        }

        string? picker = PickerLauncher.FindPicker();
        if (picker == null)
        {
            await Console.Error.WriteLineAsync("Could not find the ferret executable.");
            return 1;
        }

        using DBusConnection connection = new(address);
        await connection.ConnectAsync();

        connection.AddMethodHandler(new FileChooserPortal(connection, new(picker)));
        await connection.RequestNameAsync(BusName, RequestNameOptions.Default);

        await connection.DisconnectedAsync();
        return 0;
    }
}
