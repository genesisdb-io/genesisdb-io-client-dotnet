using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenesisDB.Client;

/// <summary>
/// Represents a CloudEvent returned from GenesisDB.
/// </summary>
public class CloudEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("specversion")]
    public string? SpecVersion { get; set; }

    [JsonPropertyName("datacontenttype")]
    public string? DataContentType { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    /// <summary>
    /// Gets a typed value from the data payload by property name.
    /// </summary>
    public T? GetDataValue<T>(string propertyName)
    {
        if (Data == null || Data.Value.ValueKind == JsonValueKind.Undefined)
            return default;

        if (Data.Value.TryGetProperty(propertyName, out var prop))
        {
            return JsonSerializer.Deserialize<T>(prop.GetRawText());
        }

        return default;
    }

    /// <summary>
    /// Gets the data payload deserialized as the specified type.
    /// </summary>
    public T? GetData<T>()
    {
        if (Data == null || Data.Value.ValueKind == JsonValueKind.Undefined)
            return default;

        return JsonSerializer.Deserialize<T>(Data.Value.GetRawText());
    }
}
