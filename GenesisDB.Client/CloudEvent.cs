using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenesisDB.Client;

/// <summary>
/// Represents a CloudEvent returned from GenesisDB.
/// </summary>
public class CloudEvent
{
    /// <summary>Unique event identifier.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>CloudEvents source identifier.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>Hierarchical subject path.</summary>
    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    /// <summary>Event type identifier.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Event timestamp.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    /// <summary>CloudEvents specification version.</summary>
    [JsonPropertyName("specversion")]
    public string? SpecVersion { get; set; }

    /// <summary>Content type of the data payload.</summary>
    [JsonPropertyName("datacontenttype")]
    public string? DataContentType { get; set; }

    /// <summary>Event payload data as a JSON element.</summary>
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
