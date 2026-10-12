using System.Text.Json.Serialization;

namespace FileExplorer.Core.Models;

public class NetworkBookmark
{
    public required string Name { get; set; }
    public required string Uri { get; set; }

    [JsonIgnore]
    public string? Password { get; set; }
}
