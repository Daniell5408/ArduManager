using System.Text.Json.Serialization;

namespace ArduboardsManager.App.Models;

public sealed class FlexibleNullableStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var longValue)
                ? longValue.ToString(CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => JsonDocument.ParseValue(ref reader).RootElement.GetRawText()
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

public sealed class PackageIndex
{
    [JsonPropertyName("packages")]
    public List<ArduinoPackage> Packages { get; set; } = new();
}

public sealed class ArduinoPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("maintainer")]
    public string? Maintainer { get; set; }

    [JsonPropertyName("websiteURL")]
    public string? WebsiteUrl { get; set; }

    [JsonPropertyName("platforms")]
    public List<ArduinoPlatform> Platforms { get; set; } = new();

    [JsonPropertyName("tools")]
    public List<ArduinoTool> Tools { get; set; } = new();
}

public sealed class ArduinoPlatform
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("archiveFileName")]
    public string ArchiveFileName { get; set; } = string.Empty;

    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    [JsonPropertyName("size")]
    [JsonConverter(typeof(FlexibleNullableStringConverter))]
    public string? Size { get; set; }

    [JsonPropertyName("boards")]
    public List<ArduinoBoard> Boards { get; set; } = new();

    [JsonPropertyName("toolsDependencies")]
    public List<ToolDependency> ToolsDependencies { get; set; } = new();
}

public sealed class ArduinoBoard
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class ToolDependency
{
    [JsonPropertyName("packager")]
    public string Packager { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public sealed class ArduinoTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("systems")]
    public List<ToolSystem> Systems { get; set; } = new();
}

public sealed class ToolSystem
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("archiveFileName")]
    public string ArchiveFileName { get; set; } = string.Empty;

    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    [JsonPropertyName("size")]
    [JsonConverter(typeof(FlexibleNullableStringConverter))]
    public string? Size { get; set; }
}
