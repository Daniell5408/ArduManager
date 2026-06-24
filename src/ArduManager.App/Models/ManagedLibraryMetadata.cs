namespace ArduManager.Models;

public sealed class ManagedLibraryMetadata
{
    public const string FileName = ".ardulibs.json";

    public string? RepositoryFullName { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? InstalledRef { get; set; }
    public DateTime InstalledAtUtc { get; set; }
}
