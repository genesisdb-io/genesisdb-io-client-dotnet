using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenesisDB.Client;

/// <summary>
/// Client for GenesisDB.
/// <code>
/// var db = new GenesisDbClient("http://localhost:8080", Environment.GetEnvironmentVariable("GENESISDB_AUTH_TOKEN"));
/// await db.CommitAsync(new[] { new NewEvent("app", "/customer/1", "customer.created", new { name = "Bruce" }) });
/// var events = await db.FetchAsync("/customer");
/// </code>
/// <c>url</c> and <c>token</c> fall back to GENESISDB_URL and GENESISDB_AUTH_TOKEN. Every method throws
/// <see cref="GenesisDbException"/> (or a subclass) on a non-2xx answer. Thread-safe, dispose when done.
/// </summary>
public sealed class GenesisDbClient : IDisposable
{
    public const string UserAgent = "genesisdb-dotnet/2";
    private const string Heartbeat = "{\"payload\":\"\"}";

    private readonly string _url;
    private readonly string _token;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <param name="url">Base URL, for example http://localhost:8080. Falls back to GENESISDB_URL.</param>
    /// <param name="token">Bearer token. Falls back to GENESISDB_AUTH_TOKEN.</param>
    /// <param name="timeout">Timeout for non-streaming calls. Default 30 s. Subscriptions never time out.</param>
    /// <param name="http">Bring your own HttpClient (proxies, TLS, handlers). Its timeout is ignored, pass <paramref name="timeout"/> instead.</param>
    public GenesisDbClient(string? url = null, string? token = null, TimeSpan? timeout = null, HttpClient? http = null)
    {
        url = FirstNonEmpty(url, Environment.GetEnvironmentVariable("GENESISDB_URL"))
            ?? throw new ArgumentException("GenesisDB URL missing: pass url or set GENESISDB_URL", nameof(url));
        _token = FirstNonEmpty(token, Environment.GetEnvironmentVariable("GENESISDB_AUTH_TOKEN"))
            ?? throw new ArgumentException("GenesisDB token missing: pass token or set GENESISDB_AUTH_TOKEN", nameof(token));
        _url = url.TrimEnd('/');
        Timeout = timeout ?? TimeSpan.FromSeconds(30);
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        // Per-request timeouts are enforced with a CancellationTokenSource, so the HttpClient must not cut streams.
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }

    /// <summary>Timeout for non-streaming calls.</summary>
    public TimeSpan Timeout { get; }

    private static string? FirstNonEmpty(string? a, string? b) => !string.IsNullOrEmpty(a) ? a : !string.IsNullOrEmpty(b) ? b : null;

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // Health and status

    /// <summary>Liveness check. Needs no token on the server. Returns "Pong".</summary>
    public Task<string> PingAsync(CancellationToken ct = default) => TextAsync("ping", HttpMethod.Get, "/api/v2/ping", null, ct);

    /// <summary>Engine version, host, event counts, storage format and readiness.</summary>
    public Task<Status> StatusAsync(CancellationToken ct = default) => JsonAsync<Status>("status", HttpMethod.Get, "/api/v2/status", null, ct);

    /// <summary>Verify the hash chain of the whole store. Administrator token. Returns the audit text.</summary>
    public Task<string> AuditAsync(CancellationToken ct = default) => TextAsync("audit", HttpMethod.Get, "/api/v2/admin/audit", null, ct);

    // Events

    /// <summary>
    /// Commit events atomically after checking the preconditions. Returns the stored events.
    /// A failed precondition throws <see cref="PreconditionFailedException"/> and writes nothing.
    /// </summary>
    public Task<IReadOnlyList<Event>> CommitAsync(IEnumerable<NewEvent> events, IEnumerable<Precondition>? preconditions = null, CancellationToken ct = default)
    {
        var list = events.ToList();
        if (list.Count == 0) throw new ArgumentException("commit requires at least one event", nameof(events));
        var body = new JsonObject { ["events"] = JsonSerializer.SerializeToNode(list, Json.Options) };
        var conditions = preconditions?.ToList();
        if (conditions is { Count: > 0 }) body["preconditions"] = JsonSerializer.SerializeToNode(conditions, Json.Options);
        return NdjsonAsync<Event>("commit", HttpMethod.Post, "/api/v2/commit", body, ct);
    }

    /// <summary>Fetch the events of a subject and all of its descendants, in commit order.</summary>
    public Task<IReadOnlyList<Event>> FetchAsync(string subject, FetchOptions? options = null, CancellationToken ct = default) =>
        NdjsonAsync<Event>("fetch", HttpMethod.Post, "/api/v2/fetch", FetchBody(subject, options), ct);

    /// <summary>
    /// Subscribe to a subject and its descendants. Yields the history first, then new events as they are committed.
    /// Runs until the token is cancelled or the server ends the stream. A stream the server ends with an error
    /// throws <see cref="GenesisDbException"/>.
    /// </summary>
    public async IAsyncEnumerable<Event> SubscribeAsync(string subject, SubscribeOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= new SubscribeOptions();
        var body = FetchBody(subject, options.Fetch);
        if (options.Interval is { } interval) body["interval"] = interval.TotalSeconds;

        using var request = Request(HttpMethod.Post, "/api/v2/subscribe", body);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new GenesisDbException("subscribe", e.Message, e);
        }
        using (response)
        {
            if ((int)response.StatusCode != 200)
            {
                throw GenesisDbException.ForResponse("subscribe", (int)response.StatusCode, await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            }
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var eventName = "";
            while (true)
            {
                string? line;
                try
                {
#if NET7_0_OR_GREATER
                    line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
#else
                    line = await reader.ReadLineAsync().WaitAsync(ct).ConfigureAwait(false);
#endif
                }
                catch (IOException) when (ct.IsCancellationRequested)
                {
                    yield break;
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    yield break;
                }
                if (line is null) yield break;
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    eventName = line[7..].Trim();
                    continue;
                }
                if (!line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    eventName = "";
                    continue;
                }
                var payload = line[6..];
                if (eventName == "error")
                {
                    var message = payload;
                    try
                    {
                        using var parsed = JsonDocument.Parse(payload);
                        if (parsed.RootElement.TryGetProperty("error", out var error)) message = error.GetString() ?? payload;
                    }
                    catch (JsonException)
                    {
                        // keep the raw payload
                    }
                    throw new GenesisDbException("subscribe ended", 200, message);
                }
                eventName = "";
                if (payload.Trim() == Heartbeat) continue;
                yield return Decode<Event>("subscribe", payload);
            }
        }
    }

    /// <summary>Run a GDBQL query. Returns the result rows, or a single-element list for a bare aggregate.</summary>
    public Task<IReadOnlyList<JsonElement>> QueryAsync(string query, CancellationToken ct = default) =>
        NdjsonAsync<JsonElement>("query", HttpMethod.Post, "/api/v2/query", new JsonObject { ["query"] = query }, ct);

    /// <summary>Run a GDBQL query and decode every row into <typeparamref name="T"/>.</summary>
    public Task<IReadOnlyList<T>> QueryAsync<T>(string query, CancellationToken ct = default) =>
        NdjsonAsync<T>("query", HttpMethod.Post, "/api/v2/query", new JsonObject { ["query"] = query }, ct);

    /// <summary>Distinct top level subjects, sorted.</summary>
    public Task<IReadOnlyList<string>> SubjectsAsync(CancellationToken ct = default) => CatalogAsync("subjects", "/api/v2/subjects", "subject", ct);

    /// <summary>Distinct event types, sorted.</summary>
    public Task<IReadOnlyList<string>> TypesAsync(CancellationToken ct = default) => CatalogAsync("types", "/api/v2/types", "type", ct);

    private async Task<IReadOnlyList<string>> CatalogAsync(string operation, string path, string field, CancellationToken ct)
    {
        var rows = await NdjsonAsync<JsonElement>(operation, HttpMethod.Get, path, null, ct).ConfigureAwait(false);
        return rows.Select(row => row.GetProperty(field).GetString() ?? "").ToList();
    }

    // Schemas

    /// <summary>Register a schema for an event type. Throws <see cref="ConflictException"/> when the type already has one. Administrator token.</summary>
    public async Task RegisterSchemaAsync(string eventType, object schema, CancellationToken ct = default)
    {
        var body = new JsonObject { ["type"] = eventType, ["schema"] = JsonSerializer.SerializeToNode(schema, Json.Options) };
        using var response = await SendAsync("register_schema", HttpMethod.Post, "/api/v2/schemas", body, ct).ConfigureAwait(false);
    }

    /// <summary>Every registered schema.</summary>
    public Task<IReadOnlyList<RegisteredSchema>> SchemasAsync(CancellationToken ct = default) =>
        NdjsonAsync<RegisteredSchema>("schemas", HttpMethod.Get, "/api/v2/schemas", null, ct);

    // Administration

    /// <summary>
    /// Erase every reference payload below a subject. Inline event data is immutable and stays. Throws
    /// <see cref="NotFoundException"/> when the subject has no events, <see cref="UnprocessableException"/> when it
    /// has no reference payloads. Administrator token. Returns the server's confirmation text.
    /// </summary>
    public Task<string> EraseAsync(string subject, CancellationToken ct = default) =>
        TextAsync("erase", HttpMethod.Post, "/api/v2/admin/erase", new JsonObject { ["subject"] = subject }, ct);

    // Transport

    private static JsonObject FetchBody(string subject, FetchOptions? options)
    {
        var body = new JsonObject { ["subject"] = subject };
        if (options is { IsEmpty: false }) body["options"] = JsonSerializer.SerializeToNode(options, Json.Options);
        return body;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, JsonNode? body)
    {
        var request = new HttpRequestMessage(method, _url + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(Json.Options), Encoding.UTF8, "application/json");
        }
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(string operation, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        using var request = Request(method, path, body);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new GenesisDbException(operation, e.Message, e);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new GenesisDbException(operation, $"timed out after {Timeout.TotalSeconds:0.#} s", e);
        }
        var status = (int)response.StatusCode;
        if (status < 200 || status > 299)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            response.Dispose();
            throw GenesisDbException.ForResponse(operation, status, text);
        }
        return response;
    }

    private async Task<string> TextAsync(string operation, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        using var response = await SendAsync(operation, method, path, body, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> JsonAsync<T>(string operation, HttpMethod method, string path, JsonNode? body, CancellationToken ct) =>
        Decode<T>(operation, await TextAsync(operation, method, path, body, ct).ConfigureAwait(false));

    private async Task<IReadOnlyList<T>> NdjsonAsync<T>(string operation, HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        var text = await TextAsync(operation, method, path, body, ct).ConfigureAwait(false);
        var rows = new List<T>();
        foreach (var line in text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(Decode<T>(operation, line));
        }
        return rows;
    }

    private static T Decode<T>(string operation, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json.Options) ?? throw new GenesisDbException(operation, 0, "empty JSON value in response");
        }
        catch (JsonException e)
        {
            throw new GenesisDbException(operation, "invalid JSON in response", e);
        }
    }
}
