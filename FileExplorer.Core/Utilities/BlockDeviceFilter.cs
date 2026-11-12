using System.Text.Json;

namespace FileExplorer.Core.Utilities;

public static class BlockDeviceFilter
{
    public const string LsblkColumns = "NAME,PATH,LABEL,PARTLABEL,PARTTYPE,MOUNTPOINT,FSTYPE,SIZE,MODEL,RM,HOTPLUG,TYPE";

    private static readonly HashSet<string> SystemPartitionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "c12a7328-f81f-11d2-ba4b-00a0c93ec93b",
        "e3c9e316-0b5c-4db8-817d-f92df00215ae",
        "de94bba4-06d1-4d40-a16a-bfd50179d6ac",
        "21686148-6449-6e6f-744e-656564454649",
        "0xef",
        "0x27",
    };

    public static bool ShouldList(JsonElement device)
    {
        if (device.TryGetProperty("parttype", out JsonElement partType) && partType.GetString() is { } type && SystemPartitionTypes.Contains(type))
        {
            return false;
        }

        bool isMounted = device.TryGetProperty("mountpoint", out JsonElement mountPoint) && !string.IsNullOrEmpty(mountPoint.GetString());
        return isMounted || IsTrue(device, "rm") || IsTrue(device, "hotplug");
    }

    private static bool IsTrue(JsonElement device, string property)
    {
        if (!device.TryGetProperty(property, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => value.TryGetInt32(out int number) && number != 0,
            JsonValueKind.String => value.GetString() is "1" or "true",
            _ => false,
        };
    }
}
