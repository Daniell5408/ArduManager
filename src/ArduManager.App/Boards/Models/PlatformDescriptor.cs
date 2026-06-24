namespace ArduboardsManager.App.Models;

public sealed class PlatformDescriptor
{
    public required string SourceUrl { get; init; }
    public required string PackageName { get; init; }
    public required string Architecture { get; init; }
    public required string DisplayName { get; init; }
    public string? GitHubUrl { get; init; }
    public required IReadOnlyDictionary<string, ArduinoPlatform> PlatformsByVersion { get; init; }

    public string Key => $"{PackageName}:{Architecture}";

    public ArduinoPlatform GetPlatform(string version) => PlatformsByVersion[version];
}
