using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenesisDB.Client.Tests;

/// <summary>Unit tests against a stubbed HttpMessageHandler. See IntegrationTests for the live tests.</summary>
public sealed class GenesisDbClientTests : IDisposable
{
    private const string Stored = "{\"source\":\"app\",\"subject\":\"/customer/1\",\"type\":\"customer.created\",\"specversion\":\"1.0\",\"id\":\"0199c0e0-0000-7000-8000-000000000001\",\"time\":\"2026-10-24T10:00:00.123456789Z\",\"datacontenttype\":\"application/json\",\"predecessorhash\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"data\":{\"name\":\"Bruce\"},\"hash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}";

    private readonly StubHandler _stub = new();
    private readonly GenesisDbClient _db;

    public GenesisDbClientTests()
    {
        _db = new GenesisDbClient("http://db.test/", "secret", http: new HttpClient(_stub));
    }

    public void Dispose() => _db.Dispose();

    private static NewEvent Event(string subject = "/customer/1") => new("app", subject, "customer.created", new { name = "Bruce" });

    private JsonNode LastBody() => JsonNode.Parse(_stub.Requests[^1].Body)!;

    [Fact]
    public void ConfigRequiresUrlAndToken()
    {
        if (Environment.GetEnvironmentVariable("GENESISDB_URL") is not null && Environment.GetEnvironmentVariable("GENESISDB_AUTH_TOKEN") is not null) return;
        Assert.Throws<ArgumentException>(() => new GenesisDbClient(token: "t"));
        Assert.Throws<ArgumentException>(() => new GenesisDbClient(url: "http://x"));
    }

    [Fact]
    public async Task PingSendsHeaders()
    {
        _stub.Respond("/ping", 200, "Pong");
        Assert.Equal("Pong", await _db.PingAsync());
        var request = _stub.Requests[^1];
        Assert.Equal("http://db.test/ping", request.Url);
        Assert.Equal("Bearer secret", request.Headers["Authorization"]);
        Assert.Equal("genesisdb-dotnet/2", request.Headers["User-Agent"]);
    }

    [Fact]
    public async Task CommitSendsBodyAndParsesNdjson()
    {
        _stub.Respond("/api/v1/commit", 200, Stored + "\n");
        var events = await _db.CommitAsync(new[] { Event() }, new[] { Precondition.IsSubjectNew("/customer/1") });
        var body = LastBody();
        Assert.Equal("/customer/1", (string?)body["events"]![0]!["subject"]);
        Assert.Equal("Bruce", (string?)body["events"]![0]!["data"]!["name"]);
        Assert.Null(body["events"]![0]!["options"]);
        Assert.Equal("isSubjectNew", (string?)body["preconditions"]![0]!["type"]);
        Assert.Equal("/customer/1", (string?)body["preconditions"]![0]!["payload"]!["subject"]);
        Assert.Single(events);
        Assert.Equal("0199c0e0-0000-7000-8000-000000000001", events[0].Id);
        Assert.Equal("Bruce", events[0].Data.GetProperty("name").GetString());
        Assert.Equal(new string('a', 64), events[0].Hash);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567), events[0].Time);
        Assert.Equal("Bruce", events[0].DataAs<Dictionary<string, string>>()!["name"]);
    }

    [Fact]
    public async Task CommitOmitsEmptyPreconditionsAndSerialisesReferenceOption()
    {
        _stub.Respond("/api/v1/commit", 200, Stored + "\n");
        await _db.CommitAsync(new[] { Event().AsReference() });
        var body = LastBody();
        Assert.Null(body["preconditions"]);
        Assert.True((bool?)body["events"]![0]!["options"]!["storeDataAsReference"]);
    }

    [Fact]
    public async Task CommitRejectsEmptyList()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _db.CommitAsync(Array.Empty<NewEvent>()));
    }

    [Fact]
    public void PreconditionHelpers()
    {
        var existing = JsonSerializer.Serialize(Precondition.IsSubjectExisting("/a"));
        Assert.Equal("{\"type\":\"isSubjectExisting\",\"payload\":{\"subject\":\"/a\"}}", existing);
        var query = JsonSerializer.Serialize(Precondition.IsQueryResultTrue("STREAM e FROM events"));
        Assert.Equal("{\"type\":\"isQueryResultTrue\",\"payload\":{\"query\":\"STREAM e FROM events\"}}", query);
    }

    [Fact]
    public async Task PreconditionFailedIsParsed()
    {
        _stub.Respond("/api/v1/commit", 412, "{\"type\":\"isSubjectNew\",\"message\":\"subject /customer/1 already exists\"}");
        var e = await Assert.ThrowsAsync<PreconditionFailedException>(() => _db.CommitAsync(new[] { Event() }));
        Assert.Equal(412, e.Status);
        Assert.Equal("commit", e.Operation);
        Assert.Equal("isSubjectNew", e.PreconditionType);
        Assert.Equal("subject /customer/1 already exists", e.Reason);
    }

    [Fact]
    public async Task FetchWithAndWithoutOptions()
    {
        _stub.Respond("/api/v1/fetch", 200, Stored + "\n" + Stored + "\n");
        Assert.Equal(2, (await _db.FetchAsync("/customer", new FetchOptions { LowerBound = "x", IncludeLowerBoundEvent = true })).Count);
        Assert.Equal("{\"subject\":\"/customer\",\"options\":{\"lowerBound\":\"x\",\"includeLowerBoundEvent\":true}}", _stub.Requests[^1].Body);
        _stub.Respond("/api/v1/fetch", 200, "");
        Assert.Empty(await _db.FetchAsync("/customer"));
        Assert.Equal("{\"subject\":\"/customer\"}", _stub.Requests[^1].Body);
        Assert.Empty(await _db.FetchAsync("/customer", new FetchOptions()));
        Assert.Equal("{\"subject\":\"/customer\"}", _stub.Requests[^1].Body);
    }

    private sealed record Row(string Name);

    [Fact]
    public async Task QuerySubjectsTypes()
    {
        _stub.Respond("/api/v1/query", 200, "{\"name\":\"Bruce\"}\n{\"name\":\"Selina\"}\n");
        _stub.Respond("/api/v1/subjects", 200, "{\"subject\":\"/customer\"}\n{\"subject\":\"/order\"}\n");
        _stub.Respond("/api/v1/types", 200, "{\"type\":\"customer.created\"}\n");
        var rows = await _db.QueryAsync("STREAM e FROM events MAP { name: e.data.name }");
        Assert.Equal("Bruce", rows[0].GetProperty("name").GetString());
        var typed = await _db.QueryAsync<Row>("x");
        Assert.Equal("Selina", typed[1].Name);
        Assert.Equal(new[] { "/customer", "/order" }, await _db.SubjectsAsync());
        Assert.Equal(new[] { "customer.created" }, await _db.TypesAsync());
    }

    [Fact]
    public async Task StatusIsMapped()
    {
        _stub.Respond("/api/v1/status", 200, "{\"engine\":{\"version\":\"2.0.0\",\"channel\":\"stable\"},\"system\":{\"os\":\"linux\"},\"events\":{\"count\":3,\"subjects\":1,\"types\":2,\"storageSize\":100},\"storage\":{\"storageFormat\":\"v2\",\"ready\":true}}");
        var status = await _db.StatusAsync();
        Assert.Equal("2.0.0", status.Engine.Version);
        Assert.Equal(3, status.Events.Count);
        Assert.True(status.Storage.Ready);
        Assert.Equal("linux", status.System.GetProperty("os").GetString());
    }

    [Fact]
    public async Task SchemasAndAdmin()
    {
        _stub.Respond("/api/v1/schemas", 201, "", "POST");
        _stub.Respond("/api/v1/schemas", 200, "{\"type\":\"customer.created\",\"schema\":{\"type\":\"object\"}}\n", "GET");
        _stub.Respond("/api/v1/status/audit", 200, "Audit passed");
        _stub.Respond("/api/v1/erase", 200, "Erased");

        await _db.RegisterSchemaAsync("customer.created", new { type = "object" });
        Assert.Equal("{\"type\":\"customer.created\",\"schema\":{\"type\":\"object\"}}", _stub.Requests[^1].Body);
        var schemas = await _db.SchemasAsync();
        Assert.Equal("customer.created", schemas[0].Type);
        Assert.Equal("object", schemas[0].Schema.GetProperty("type").GetString());
        Assert.Equal("Audit passed", await _db.AuditAsync());
        Assert.Equal("Erased", await _db.EraseAsync("/customer/1"));
        Assert.Equal("{\"subject\":\"/customer/1\"}", _stub.Requests[^1].Body);
    }

    [Theory]
    [InlineData(401, typeof(AuthenticationException))]
    [InlineData(403, typeof(AuthenticationException))]
    [InlineData(404, typeof(NotFoundException))]
    [InlineData(409, typeof(ConflictException))]
    [InlineData(422, typeof(UnprocessableException))]
    [InlineData(503, typeof(NotReadyException))]
    [InlineData(500, typeof(GenesisDbException))]
    public async Task ErrorClasses(int status, Type expected)
    {
        _stub.Respond("/api/v1/fetch", status, "nope");
        var e = await Assert.ThrowsAnyAsync<GenesisDbException>(() => _db.FetchAsync("/x"));
        Assert.IsType(expected, e);
        Assert.Equal(status, e.Status);
        Assert.Equal("nope", e.Body);
        Assert.Equal("fetch", e.Operation);
        Assert.Contains("nope", e.Message);
    }

    [Fact]
    public async Task SubscribeSkipsHeartbeatAndStopsOnError()
    {
        var stream = "data: " + Stored + "\n\ndata: {\"payload\":\"\"}\n\ndata: " + Stored + "\n\nevent: error\ndata: {\"error\":\"store closed\"}\n\n";
        _stub.Respond("/api/v1/subscribe", 200, stream);
        var received = new List<Event>();
        var e = await Assert.ThrowsAsync<GenesisDbException>(async () =>
        {
            await foreach (var ev in _db.SubscribeAsync("/customer", new SubscribeOptions { Interval = TimeSpan.FromMilliseconds(500) }))
            {
                received.Add(ev);
            }
        });
        Assert.Contains("store closed", e.Message);
        Assert.Equal(2, received.Count);
        Assert.Equal("{\"subject\":\"/customer\",\"interval\":0.5}", _stub.Requests[^1].Body);
    }

    [Fact]
    public async Task SubscribeThrowsOnHttpError()
    {
        _stub.Respond("/api/v1/subscribe", 401, "denied");
        await Assert.ThrowsAsync<AuthenticationException>(async () =>
        {
            await foreach (var _ in _db.SubscribeAsync("/customer")) Assert.Fail("no event expected");
        });
    }

    /// <summary>Records requests and answers from a table keyed by method and path.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public sealed record Recorded(string Method, string Url, Dictionary<string, string> Headers, string Body);

        private readonly Dictionary<string, (int Status, string Body)> _responses = new();
        public List<Recorded> Requests { get; } = new();

        public void Respond(string path, int status, string body, string? method = null) => _responses[(method ?? "*") + " " + path] = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value));
            Requests.Add(new Recorded(request.Method.Method, request.RequestUri!.ToString(), headers, body));
            var path = request.RequestUri!.AbsolutePath;
            if (!_responses.TryGetValue(request.Method.Method + " " + path, out var response) && !_responses.TryGetValue("* " + path, out response))
            {
                throw new HttpRequestException("no stub for " + path);
            }
            return new HttpResponseMessage((HttpStatusCode)response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "text/plain") };
        }
    }
}
