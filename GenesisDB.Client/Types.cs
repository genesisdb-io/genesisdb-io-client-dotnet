using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenesisDB.Client;

/// <summary>An event as stored and returned by GenesisDB. The property names follow CloudEvents.</summary>
public sealed record Event
{
    [JsonPropertyName("source")] public string Source { get; init; } = "";
    [JsonPropertyName("subject")] public string Subject { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("specversion")] public string SpecVersion { get; init; } = "1.0";
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("time")] public DateTimeOffset Time { get; init; }
    [JsonPropertyName("datacontenttype")] public string DataContentType { get; init; } = "application/json";
    [JsonPropertyName("predecessorhash")] public string PredecessorHash { get; init; } = "";
    /// <summary>The payload as a JSON element.</summary>
    [JsonPropertyName("data")] public JsonElement Data { get; init; }
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";

    /// <summary>Decode the payload into a typed value.</summary>
    public T? DataAs<T>() => Data.Deserialize<T>(Json.Options);
}

/// <summary>An event to commit. ID, time, hashes and the remaining CloudEvents attributes are assigned by the server.</summary>
public sealed record NewEvent
{
    /// <param name="source">The producing system.</param>
    /// <param name="subject">Must start with "/". A single segment such as "/customer" gets a generated ID appended.</param>
    /// <param name="type">The event type.</param>
    /// <param name="data">A JSON object: an anonymous object, a dictionary, a JsonElement or any class System.Text.Json serialises to an object.</param>
    public NewEvent(string source, string subject, string type, object data)
    {
        Source = source;
        Subject = subject;
        Type = type;
        Data = data;
    }

    [JsonPropertyName("source")] public string Source { get; init; }
    [JsonPropertyName("subject")] public string Subject { get; init; }
    [JsonPropertyName("type")] public string Type { get; init; }
    [JsonPropertyName("data")] public object Data { get; init; }
    [JsonPropertyName("options")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CommitOptions? Options { get; init; }

    /// <summary>Store the payload as reference data, so it can be erased later without breaking the hash chain.</summary>
    public NewEvent AsReference() => this with { Options = new CommitOptions { StoreDataAsReference = true } };
}

/// <summary>Per-event options for a commit.</summary>
public sealed record CommitOptions
{
    [JsonPropertyName("storeDataAsReference")] public bool StoreDataAsReference { get; init; }
}

/// <summary>A precondition that is checked atomically with the commit. Build one with the static factories.</summary>
public sealed record Precondition
{
    private Precondition(string type, string key, string value)
    {
        Type = type;
        Payload = new Dictionary<string, string> { [key] = value };
    }

    [JsonPropertyName("type")] public string Type { get; }
    [JsonPropertyName("payload")] public IReadOnlyDictionary<string, string> Payload { get; }

    /// <summary>Holds when neither the subject nor a descendant has events.</summary>
    public static Precondition IsSubjectNew(string subject) => new("isSubjectNew", "subject", subject);

    /// <summary>Holds when the subject or a descendant has events.</summary>
    public static Precondition IsSubjectExisting(string subject) => new("isSubjectExisting", "subject", subject);

    /// <summary>Holds when the GDBQL query result is true.</summary>
    public static Precondition IsQueryResultTrue(string query) => new("isQueryResultTrue", "query", query);
}

/// <summary>Narrow a fetch or a subscription. Bounds are event IDs.</summary>
public sealed record FetchOptions
{
    /// <summary>Skip every event before this ID. An ID that does not occur below the subject yields an empty result.</summary>
    [JsonPropertyName("lowerBound")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LowerBound { get; init; }
    [JsonPropertyName("includeLowerBoundEvent")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? IncludeLowerBoundEvent { get; init; }
    /// <summary>Stop at this ID.</summary>
    [JsonPropertyName("upperBound")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? UpperBound { get; init; }
    [JsonPropertyName("includeUpperBoundEvent")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? IncludeUpperBoundEvent { get; init; }
    /// <summary>Return only the newest event of this type below the subject. Ignored by subscriptions.</summary>
    [JsonPropertyName("latestByEventType")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LatestByEventType { get; init; }

    internal bool IsEmpty => LowerBound is null && IncludeLowerBoundEvent is null && UpperBound is null && IncludeUpperBoundEvent is null && LatestByEventType is null;
}

/// <summary>Configure a subscription.</summary>
public sealed record SubscribeOptions
{
    public FetchOptions Fetch { get; init; } = new();
    /// <summary>Poll interval. Null uses the server default of one second. Clamped by the server to 100 ms .. 24 h.</summary>
    public TimeSpan? Interval { get; init; }
}

/// <summary>One entry of the schema list.</summary>
public sealed record RegisteredSchema
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("schema")] public JsonElement Schema { get; init; }
}

/// <summary>The body of GET /api/v1/status.</summary>
public sealed record Status
{
    [JsonPropertyName("engine")] public EngineStatus Engine { get; init; } = new();
    [JsonPropertyName("events")] public EventsStatus Events { get; init; } = new();
    [JsonPropertyName("storage")] public StorageStatus Storage { get; init; } = new();
    /// <summary>Operating system, CPU and memory figures.</summary>
    [JsonPropertyName("system")] public JsonElement System { get; init; }

    public sealed record EngineStatus
    {
        [JsonPropertyName("version")] public string Version { get; init; } = "";
        [JsonPropertyName("channel")] public string Channel { get; init; } = "";
    }

    public sealed record EventsStatus
    {
        [JsonPropertyName("count")] public long Count { get; init; }
        [JsonPropertyName("subjects")] public long Subjects { get; init; }
        [JsonPropertyName("types")] public long Types { get; init; }
        [JsonPropertyName("storageSize")] public long StorageSize { get; init; }
    }

    public sealed record StorageStatus
    {
        [JsonPropertyName("storageFormat")] public string Format { get; init; } = "";
        [JsonPropertyName("ready")] public bool Ready { get; init; }
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
