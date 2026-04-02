using System.Text.Json.Serialization;

namespace GenesisDB.Client;

/// <summary>
/// Options for streaming or observing events.
/// </summary>
public class StreamOptions
{
    /// <summary>
    /// Resume streaming from this event ID.
    /// </summary>
    [JsonPropertyName("lowerBound")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LowerBound { get; set; }

    /// <summary>
    /// Whether to include the lower-bound event itself in the results.
    /// </summary>
    [JsonPropertyName("includeLowerBoundEvent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeLowerBoundEvent { get; set; }

    /// <summary>
    /// Stop streaming at this event ID.
    /// </summary>
    [JsonPropertyName("upperBound")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpperBound { get; set; }

    /// <summary>
    /// Whether to include the upper-bound event itself in the results.
    /// </summary>
    [JsonPropertyName("includeUpperBoundEvent")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeUpperBoundEvent { get; set; }

    /// <summary>
    /// Stream only the latest event of the given type per subject.
    /// </summary>
    [JsonPropertyName("latestByEventType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LatestByEventType { get; set; }
}
