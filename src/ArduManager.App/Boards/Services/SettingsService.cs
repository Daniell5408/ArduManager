using System.Text.Json;
using ArduboardsManager.App.Models;

namespace ArduboardsManager.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<SettingsData> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.AppDataDirectory);

        if (!File.Exists(AppPaths.SettingsFile))
            return new SettingsData();

        await using var stream = File.OpenRead(AppPaths.SettingsFile);
        var settings = await JsonSerializer.DeserializeAsync<SettingsData>(stream, JsonOptions, cancellationToken);
        return settings ?? new SettingsData();
    }

    public async Task SaveAsync(SettingsData settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.AppDataDirectory);
        await using var stream = File.Create(AppPaths.SettingsFile);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
    }

    public async Task AddUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        if (!settings.PackageUrls.Any(x => string.Equals(x, url, StringComparison.OrdinalIgnoreCase)))
        {
            settings.PackageUrls.Add(url);
            await SaveAsync(settings, cancellationToken);
        }
    }

    public async Task RemoveUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(cancellationToken);
        settings.PackageUrls = settings.PackageUrls
            .Where(x => !string.Equals(x, url, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await SaveAsync(settings, cancellationToken);
    }
}
