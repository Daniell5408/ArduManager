using ArdulibsManager.Models;

namespace ArdulibsManager.Services;

public sealed class LibraryScannerService
{
    public async Task<IReadOnlyList<InstalledLibrary>> ScanAsync(string librariesPath, CancellationToken ct = default)
    {
        var list = new List<InstalledLibrary>();
        if (!Directory.Exists(librariesPath)) return list;

        foreach (var dir in Directory.EnumerateDirectories(librariesPath))
        {
            ct.ThrowIfCancellationRequested();
            var folder = Path.GetFileName(dir) ?? string.Empty;
            if (folder.StartsWith(".") || folder.Contains(".backup", StringComparison.OrdinalIgnoreCase) || folder.Equals("backups", StringComparison.OrdinalIgnoreCase))
                continue;

            var propsPath = Path.Combine(dir, "library.properties");
            if (!File.Exists(propsPath)) continue;
            try
            {
                var text = await File.ReadAllTextAsync(propsPath, ct);
                var props = LibraryProperties.Parse(text);
                var metadata = await ReadMetadataAsync(dir, ct);
                var displayVersion = props.Version;
                var status = metadata?.RepositoryFullName is null
                    ? "Не проверено"
                    : $"Управляется: {metadata.RepositoryFullName}";

                if (!string.IsNullOrWhiteSpace(metadata?.InstalledRef)
                    && !string.IsNullOrWhiteSpace(props.Version)
                    && !VersionService.IsSameVersion(metadata.InstalledRef, props.Version))
                {
                    status += $"; library.properties version={props.Version}";
                }

                list.Add(new InstalledLibrary
                {
                    Name = props.Name ?? Path.GetFileName(dir),
                    Version = displayVersion,
                    PropertiesVersion = props.Version,
                    InstalledRef = metadata?.InstalledRef,
                    Maintainer = props.Maintainer,
                    Url = props.Url,
                    LocalPath = dir,
                    RepositoryFullName = metadata?.RepositoryFullName,
                    Status = status
                });
            }
            catch (Exception ex)
            {
                list.Add(new InstalledLibrary
                {
                    Name = Path.GetFileName(dir),
                    LocalPath = dir,
                    Status = "Ошибка чтения library.properties: " + ex.Message
                });
            }
        }

        return list.OrderBy(x => x.Name).ToList();
    }
    private static async Task<ManagedLibraryMetadata?> ReadMetadataAsync(string libraryPath, CancellationToken ct)
    {
        var metadataPath = Path.Combine(libraryPath, ManagedLibraryMetadata.FileName);
        if (!File.Exists(metadataPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, ct);
            return JsonSerializer.Deserialize<ManagedLibraryMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

}
