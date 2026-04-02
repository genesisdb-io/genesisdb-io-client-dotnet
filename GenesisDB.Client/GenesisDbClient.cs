using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace GenesisDB.Client;

/// <summary>
/// Client for interacting with GenesisDB.
/// </summary>
public class GenesisDbClient : IDisposable
{
    private readonly string _apiUrl;
    private readonly string _apiVersion;
    private readonly string _authToken;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a new GenesisDB client using environment variables for configuration.
    /// Required environment variables: GENESISDB_API_URL, GENESISDB_API_VERSION, GENESISDB_AUTH_TOKEN
    /// </summary>
    public GenesisDbClient() : this(null, null) { }

    /// <summary>
    /// Creates a new GenesisDB client with the specified configuration.
    /// Missing config values fall back to environment variables.
    /// </summary>
    public GenesisDbClient(ClientConfig config) : this(config, null) { }

    /// <summary>
    /// Creates a new GenesisDB client with a custom HttpClient (useful for testing).
    /// </summary>
    public GenesisDbClient(ClientConfig? config, HttpClient? httpClient)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        var envApiUrl = Environment.GetEnvironmentVariable("GENESISDB_API_URL");
        var envApiVersion = Environment.GetEnvironmentVariable("GENESISDB_API_VERSION");
        var envAuthToken = Environment.GetEnvironmentVariable("GENESISDB_AUTH_TOKEN");

        _apiUrl = config?.ApiUrl ?? envApiUrl ?? string.Empty;
        _apiVersion = config?.ApiVersion ?? envApiVersion ?? string.Empty;
        _authToken = config?.AuthToken ?? envAuthToken ?? string.Empty;

        if (string.IsNullOrEmpty(_apiUrl) || string.IsNullOrEmpty(_apiVersion) || string.IsNullOrEmpty(_authToken))
        {
            var missing = new List<string>();
            if (string.IsNullOrEmpty(_apiUrl)) missing.Add("GENESISDB_API_URL");
            if (string.IsNullOrEmpty(_apiVersion)) missing.Add("GENESISDB_API_VERSION");
            if (string.IsNullOrEmpty(_authToken)) missing.Add("GENESISDB_AUTH_TOKEN");
            throw new InvalidOperationException(
                $"Missing required environment variables: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// Streams events for a subject from GenesisDB.
    /// </summary>
    /// <param name="subject">The subject path to stream events for.</param>
    /// <param name="options">Optional streaming options (bounds, filters).</param>
    /// <returns>A list of CloudEvents.</returns>
    public async Task<List<CloudEvent>> StreamEventsAsync(string subject, StreamOptions? options = null)
    {
        var url = $"{_apiUrl}/api/{_apiVersion}/stream";

        var requestBody = new Dictionary<string, object> { ["subject"] = subject };
        if (options != null)
        {
            requestBody["options"] = options;
        }

        var request = CreateRequest(HttpMethod.Post, url, requestBody);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response);

        var rawText = await response.Content.ReadAsStringAsync();
        return ParseNdjsonCloudEvents(rawText);
    }

    /// <summary>
    /// Commits events to GenesisDB.
    /// </summary>
    /// <param name="events">Array of events to commit.</param>
    /// <param name="preconditions">Optional preconditions to check before committing.</param>
    public async Task CommitEventsAsync(IEnumerable<CommitEvent> events, IEnumerable<Precondition>? preconditions = null)
    {
        var url = $"{_apiUrl}/api/{_apiVersion}/commit";

        var requestBody = new Dictionary<string, object>
        {
            ["events"] = events
        };

        var preconditionsList = preconditions?.ToList();
        if (preconditionsList != null && preconditionsList.Count > 0)
        {
            requestBody["preconditions"] = preconditionsList;
        }

        var request = CreateRequest(HttpMethod.Post, url, requestBody);
        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    /// <summary>
    /// Erases data for a subject (GDPR compliance).
    /// </summary>
    /// <param name="subject">The subject to erase data for.</param>
    public async Task EraseDataAsync(string subject)
    {
        var url = $"{_apiUrl}/api/{_apiVersion}/erase";

        var requestBody = new { subject };
        var request = CreateRequest(HttpMethod.Post, url, requestBody);
        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    /// <summary>
    /// Gets audit information from GenesisDB.
    /// </summary>
    /// <returns>Audit information as a string.</returns>
    public async Task<string> AuditAsync()
    {
        var url = $"{_apiUrl}/api/{_apiVersion}/status/audit";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
        request.Headers.Add("User-Agent", "genesisdb-sdk-dotnet");
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain");

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Pings the GenesisDB server.
    /// </summary>
    /// <returns>The ping response (typically "pong").</returns>
    public async Task<string> PingAsync()
    {
        var url = $"{_apiUrl}/api/{_apiVersion}/status/ping";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
        request.Headers.Add("User-Agent", "genesisdb-sdk-dotnet");
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain");

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Executes a GDBQL query against GenesisDB.
    /// </summary>
    /// <param name="query">The GDBQL query string.</param>
    /// <returns>A list of JSON results.</returns>
    public async Task<List<JsonElement>> QueryAsync(string query)
    {
        var url = $"{_apiUrl}/api/{_apiVersion}/q";

        var requestBody = new { query };
        var request = CreateRequest(HttpMethod.Post, url, requestBody);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        var response = await _httpClient.SendAsync(request);
        await EnsureSuccessAsync(response);

        var rawText = await response.Content.ReadAsStringAsync();
        return ParseNdjsonResults(rawText);
    }

    /// <summary>
    /// Alias for QueryAsync. Executes a GDBQL query against GenesisDB.
    /// </summary>
    public Task<List<JsonElement>> QueryEventsAsync(string query) => QueryAsync(query);

    /// <summary>
    /// Observes events for a subject as an async stream.
    /// The stream stays open and yields new events as they arrive.
    /// </summary>
    /// <param name="subject">The subject path to observe.</param>
    /// <param name="options">Optional streaming options.</param>
    /// <param name="cancellationToken">Cancellation token to stop observing.</param>
    /// <returns>An async enumerable of CloudEvents.</returns>
    public async IAsyncEnumerable<CloudEvent> ObserveEventsAsync(
        string subject,
        StreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = $"{_apiUrl}/api/{_apiVersion}/observe";

        var requestBody = new Dictionary<string, object> { ["subject"] = subject };
        if (options != null)
        {
            requestBody["options"] = options;
        }

        var request = CreateRequest(HttpMethod.Post, url, requestBody);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response);

        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break; // stream ended

            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Handle SSE format with "data: " prefix
            var jsonStr = trimmed.StartsWith("data: ") ? trimmed[6..] : trimmed;

            var cloudEvent = TryParseObservedEvent(jsonStr);
            if (cloudEvent != null)
            {
                yield return cloudEvent;
            }
        }
    }

    private static CloudEvent? TryParseObservedEvent(string jsonStr)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(jsonStr);

            // Skip heartbeat messages
            if (json.ValueKind == JsonValueKind.Object &&
                json.TryGetProperty("payload", out var payload) &&
                payload.ValueKind == JsonValueKind.String &&
                payload.GetString() == "" &&
                json.EnumerateObject().Count() == 1)
            {
                return null;
            }

            return JsonSerializer.Deserialize<CloudEvent>(jsonStr);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
        request.Headers.Add("User-Agent", "genesisdb-sdk-dotnet");

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new GenesisDbException(response.StatusCode, response.ReasonPhrase);
        }
        return Task.CompletedTask;
    }

    private static List<CloudEvent> ParseNdjsonCloudEvents(string rawText)
    {
        var events = new List<CloudEvent>();
        if (string.IsNullOrWhiteSpace(rawText)) return events;

        foreach (var line in rawText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            try
            {
                var cloudEvent = JsonSerializer.Deserialize<CloudEvent>(trimmed);
                if (cloudEvent != null) events.Add(cloudEvent);
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return events;
    }

    private static List<JsonElement> ParseNdjsonResults(string rawText)
    {
        var results = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(rawText)) return results;

        foreach (var line in rawText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(trimmed);
                results.Add(json);
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return results;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
