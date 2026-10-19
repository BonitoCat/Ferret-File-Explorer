namespace FileExplorer.Core.Models;

public sealed record AppChoice(string Id, string Name);

public sealed record AppAssociations(string MimeType, string? DefaultApp, string? DefaultAppName, List<AppChoice> Apps);
