using System.Text.Json.Serialization;

namespace GenesisDB.Client;

/// <summary>
/// A precondition that must hold true before a commit is accepted.
/// </summary>
public class Precondition
{
    /// <summary>
    /// The precondition type (e.g., "isSubjectNew", "isSubjectExisting", "isQueryResultTrue").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The precondition payload. Structure depends on the precondition type.
    /// </summary>
    [JsonPropertyName("payload")]
    public object Payload { get; set; } = new { };
}

/// <summary>
/// Factory methods for creating strongly-typed preconditions.
/// </summary>
public static class Preconditions
{
    /// <summary>
    /// Creates a precondition that ensures no events exist yet for the given subject.
    /// </summary>
    public static Precondition IsSubjectNew(string subject) => new()
    {
        Type = "isSubjectNew",
        Payload = new { subject }
    };

    /// <summary>
    /// Creates a precondition that ensures at least one event exists for the given subject.
    /// </summary>
    public static Precondition IsSubjectExisting(string subject) => new()
    {
        Type = "isSubjectExisting",
        Payload = new { subject }
    };

    /// <summary>
    /// Creates a precondition that evaluates a GDBQL query and ensures the result is truthy.
    /// </summary>
    public static Precondition IsQueryResultTrue(string query) => new()
    {
        Type = "isQueryResultTrue",
        Payload = new { query }
    };
}
