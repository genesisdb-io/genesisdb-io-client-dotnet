using System.Text.Json.Serialization;

namespace GenesisDB.Client;

/// <summary>
/// Options for storing an event.
/// </summary>
public class CommitEventOptions
{
    /// <summary>
    /// When true, event data is stored as a separate reference that can be erased for GDPR compliance.
    /// </summary>
    [JsonPropertyName("storeDataAsReference")]
    public bool? StoreDataAsReference { get; set; }
}

/// <summary>
/// An event to commit to GenesisDB.
/// </summary>
public class CommitEvent
{
    /// <summary>
    /// CloudEvents source identifier.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Hierarchical subject path.
    /// </summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Event type identifier.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Event payload data.
    /// </summary>
    [JsonPropertyName("data")]
    public object? Data { get; set; }

    /// <summary>
    /// Optional commit options.
    /// </summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CommitEventOptions? Options { get; set; }
}
