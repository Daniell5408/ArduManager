namespace ArduboardsManager.App.Services;

public sealed class ArduinoPathService
{
    public string DataDirectory { get; }

    public ArduinoPathService()
    {
        var overrideDir = Environment.GetEnvironmentVariable("ARDUINO_DATA_DIR");
        DataDirectory = !string.IsNullOrWhiteSpace(overrideDir)
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arduino15");
    }

    public string GetPackageDirectory(string packager)
    {
        return Path.Combine(DataDirectory, "packages", packager);
    }

    public string GetPackageHardwareDirectory(string packager)
    {
        return Path.Combine(GetPackageDirectory(packager), "hardware");
    }

    public string GetPackageToolsDirectory(string packager)
    {
        return Path.Combine(GetPackageDirectory(packager), "tools");
    }

    public string GetPlatformVersionsDirectory(string packager, string architecture)
    {
        return Path.Combine(GetPackageHardwareDirectory(packager), architecture);
    }

    public string GetPlatformVersionDirectory(string packager, string architecture, string version)
    {
        return Path.Combine(GetPlatformVersionsDirectory(packager, architecture), version);
    }

    public string GetToolFamilyDirectory(string packager, string toolName)
    {
        return Path.Combine(GetPackageToolsDirectory(packager), toolName);
    }

    public string GetToolVersionDirectory(string packager, string toolName, string version)
    {
        return Path.Combine(GetToolFamilyDirectory(packager, toolName), version);
    }

    public bool HasAnyPlatformFiles(string packager, string architecture)
    {
        var dir = GetPlatformVersionsDirectory(packager, architecture);
        return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any();
    }

    public bool HasAnyLocalFilesForDescriptor(Models.PlatformDescriptor descriptor)
    {
        if (HasAnyPlatformFiles(descriptor.PackageName, descriptor.Architecture))
            return true;

        foreach (var dependency in GetToolDependencies(descriptor))
        {
            var dir = GetToolVersionDirectory(dependency.Packager, dependency.Name, dependency.Version);
            if (Directory.Exists(dir))
                return true;
        }

        var packageDir = GetPackageDirectory(descriptor.PackageName);
        return Directory.Exists(packageDir) && Directory.EnumerateFileSystemEntries(packageDir).Any();
    }

    public static IReadOnlyList<Models.ToolDependency> GetToolDependencies(Models.PlatformDescriptor descriptor)
    {
        return descriptor.PlatformsByVersion.Values
            .SelectMany(platform => platform.ToolsDependencies ?? new List<Models.ToolDependency>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Packager)
                        && !string.IsNullOrWhiteSpace(x.Name)
                        && !string.IsNullOrWhiteSpace(x.Version))
            .GroupBy(x => $"{x.Packager}\0{x.Name}\0{x.Version}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    public IReadOnlyList<string> GetInstalledPlatformVersions(string packager, string architecture)
    {
        var dir = GetPlatformVersionsDirectory(packager, architecture);
        if (!Directory.Exists(dir))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(dir)
            .Select(Path.GetFileName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Where(x => !x.Contains(".installing-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x, VersionTextComparer.Instance)
            .ToList();
    }

    public string? GetInstalledPlatformVersion(string packager, string architecture)
    {
        return GetInstalledPlatformVersions(packager, architecture).FirstOrDefault();
    }
}
