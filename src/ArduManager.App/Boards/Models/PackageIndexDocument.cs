namespace ArduboardsManager.App.Models;

public sealed class PackageIndexDocument
{
    public required string SourceUrl { get; init; }
    public required PackageIndex Index { get; init; }
}
