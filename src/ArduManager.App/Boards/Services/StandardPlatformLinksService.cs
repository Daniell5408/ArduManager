using ArduboardsManager.App.Models;

namespace ArduboardsManager.App.Services;

public sealed class StandardPlatformLinksService
{
    private const string FileName = "standard-platforms.json";

    public IReadOnlyList<StandardPlatformLink> Load()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, FileName);

        // При dotnet run файл копируется в bin. На случай запуска из другой структуры оставляем fallback.
        if (!File.Exists(filePath))
        {
            var sourceTreeFallback = Path.Combine(Environment.CurrentDirectory, "src", "ArduManager.App", FileName);
            if (File.Exists(sourceTreeFallback))
                filePath = sourceTreeFallback;
        }

        if (!File.Exists(filePath))
            return Array.Empty<StandardPlatformLink>();

        var json = File.ReadAllText(filePath);
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (map is null || map.Count == 0)
            return Array.Empty<StandardPlatformLink>();

        return map
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new StandardPlatformLink
            {
                Name = x.Key.Trim(),
                Url = x.Value.Trim()
            })
            .ToList();
    }
}
