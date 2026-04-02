using System.Net;
using System.Text.Json;
using RichardSzalay.MockHttp;

namespace GenesisDB.Client.Tests;

public class GenesisDbClientTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly GenesisDbClient _client;

    public GenesisDbClientTests()
    {
        _mockHttp = new MockHttpMessageHandler();
        _client = new GenesisDbClient(
            new ClientConfig
            {
                ApiUrl = "http://localhost:8080",
                ApiVersion = "v1",
                AuthToken = "test-token"
            },
            _mockHttp.ToHttpClient()
        );
    }

    public void Dispose()
    {
        _client.Dispose();
        _mockHttp.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithFullConfig_CreatesClient()
    {
        using var client = new GenesisDbClient(new ClientConfig
        {
            ApiUrl = "http://custom.api.com",
            ApiVersion = "v2",
            AuthToken = "custom-token"
        });
        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_WithMissingConfig_ThrowsException()
    {
        // Clear env vars to ensure they don't interfere
        var origUrl = Environment.GetEnvironmentVariable("GENESISDB_API_URL");
        var origVersion = Environment.GetEnvironmentVariable("GENESISDB_API_VERSION");
        var origToken = Environment.GetEnvironmentVariable("GENESISDB_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("GENESISDB_API_URL", null);
            Environment.SetEnvironmentVariable("GENESISDB_API_VERSION", null);
            Environment.SetEnvironmentVariable("GENESISDB_AUTH_TOKEN", null);

            var ex = Assert.Throws<InvalidOperationException>(() => new GenesisDbClient());
            Assert.Contains("GENESISDB_API_URL", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GENESISDB_API_URL", origUrl);
            Environment.SetEnvironmentVariable("GENESISDB_API_VERSION", origVersion);
            Environment.SetEnvironmentVariable("GENESISDB_AUTH_TOKEN", origToken);
        }
    }

    [Fact]
    public void Constructor_WithPartialConfig_ThrowsForMissing()
    {
        var origUrl = Environment.GetEnvironmentVariable("GENESISDB_API_URL");
        try
        {
            Environment.SetEnvironmentVariable("GENESISDB_API_URL", null);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new GenesisDbClient(new ClientConfig { ApiVersion = "v1", AuthToken = "token" }));
            Assert.Contains("GENESISDB_API_URL", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GENESISDB_API_URL", origUrl);
        }
    }

    #endregion

    #region StreamEvents Tests

    [Fact]
    public async Task StreamEventsAsync_ReturnsCloudEvents()
    {
        var mockEvent = new
        {
            id = "1",
            source = "test",
            subject = "/test",
            type = "test.event",
            data = new { message = "test" }
        };

        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/stream")
            .Respond("application/x-ndjson", JsonSerializer.Serialize(mockEvent) + "\n");

        var events = await _client.StreamEventsAsync("/test");

        Assert.Single(events);
        Assert.Equal("1", events[0].Id);
        Assert.Equal("test", events[0].Source);
        Assert.Equal("/test", events[0].Subject);
        Assert.Equal("test.event", events[0].Type);
    }

    [Fact]
    public async Task StreamEventsAsync_WithOptions_SendsCorrectBody()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/stream")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("subject").GetString() == "/test" &&
                       json.GetProperty("options").GetProperty("lowerBound").GetString() == "123" &&
                       json.GetProperty("options").GetProperty("includeLowerBoundEvent").GetBoolean() == true &&
                       json.GetProperty("options").GetProperty("latestByEventType").GetString() == "test.type";
            })
            .Respond("application/x-ndjson", "");

        await _client.StreamEventsAsync("/test", new StreamOptions
        {
            LowerBound = "123",
            IncludeLowerBoundEvent = true,
            LatestByEventType = "test.type"
        });
    }

    [Fact]
    public async Task StreamEventsAsync_WithUpperBound_SendsCorrectBody()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/stream")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("options").GetProperty("upperBound").GetString() == "456" &&
                       json.GetProperty("options").GetProperty("includeUpperBoundEvent").GetBoolean() == false;
            })
            .Respond("application/x-ndjson", "");

        await _client.StreamEventsAsync("/test", new StreamOptions
        {
            UpperBound = "456",
            IncludeUpperBoundEvent = false
        });
    }

    [Fact]
    public async Task StreamEventsAsync_EmptyResponse_ReturnsEmptyList()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/stream")
            .Respond("application/x-ndjson", "");

        var events = await _client.StreamEventsAsync("/test");
        Assert.Empty(events);
    }

    [Fact]
    public async Task StreamEventsAsync_ApiError_ThrowsException()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/stream")
            .Respond(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<GenesisDbException>(
            () => _client.StreamEventsAsync("/test"));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task StreamEventsAsync_MultipleNdjsonEvents_ParsesAll()
    {
        var event1 = new { id = "1", source = "test", subject = "/test1", type = "test.event", data = new { n = 1 } };
        var event2 = new { id = "2", source = "test", subject = "/test2", type = "test.event", data = new { n = 2 } };

        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/stream")
            .Respond("application/x-ndjson",
                $"{JsonSerializer.Serialize(event1)}\n{JsonSerializer.Serialize(event2)}\n");

        var events = await _client.StreamEventsAsync("/test");
        Assert.Equal(2, events.Count);
        Assert.Equal("1", events[0].Id);
        Assert.Equal("2", events[1].Id);
    }

    #endregion

    #region CommitEvents Tests

    [Fact]
    public async Task CommitEventsAsync_SendsCorrectRequest()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                var events = json.GetProperty("events");
                return events.GetArrayLength() == 1 &&
                       events[0].GetProperty("source").GetString() == "test.source" &&
                       events[0].GetProperty("subject").GetString() == "/test/subject" &&
                       events[0].GetProperty("type").GetString() == "test.event.created";
            })
            .Respond(HttpStatusCode.OK);

        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "test.source",
                Subject = "/test/subject",
                Type = "test.event.created",
                Data = new { name = "Test Event" }
            }
        });
    }

    [Fact]
    public async Task CommitEventsAsync_WithOptions_IncludesOptions()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("events")[0]
                    .GetProperty("options")
                    .GetProperty("storeDataAsReference")
                    .GetBoolean() == true;
            })
            .Respond(HttpStatusCode.OK);

        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "test.source",
                Subject = "/test/subject",
                Type = "test.event.created",
                Data = new { name = "Test Event" },
                Options = new CommitEventOptions { StoreDataAsReference = true }
            }
        });
    }

    [Fact]
    public async Task CommitEventsAsync_WithPreconditions_IncludesPreconditions()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                var preconditions = json.GetProperty("preconditions");
                return preconditions.GetArrayLength() == 1 &&
                       preconditions[0].GetProperty("type").GetString() == "isSubjectNew";
            })
            .Respond(HttpStatusCode.OK);

        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent
                {
                    Source = "test.source",
                    Subject = "/test/subject",
                    Type = "test.event.created",
                    Data = new { name = "Test Event" }
                }
            },
            new[] { Preconditions.IsSubjectNew("/test/subject") }
        );
    }

    [Fact]
    public async Task CommitEventsAsync_WithIsSubjectExisting_IncludesPrecondition()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("preconditions")[0]
                    .GetProperty("type").GetString() == "isSubjectExisting";
            })
            .Respond(HttpStatusCode.OK);

        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent { Source = "test", Subject = "/test/subject", Type = "test.updated", Data = new { v = 2 } }
            },
            new[] { Preconditions.IsSubjectExisting("/test/subject") }
        );
    }

    [Fact]
    public async Task CommitEventsAsync_WithIsQueryResultTrue_IncludesPrecondition()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("preconditions")[0]
                    .GetProperty("type").GetString() == "isQueryResultTrue";
            })
            .Respond(HttpStatusCode.OK);

        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent { Source = "test", Subject = "/test", Type = "test.created", Data = new { } }
            },
            new[] { Preconditions.IsQueryResultTrue("STREAM e FROM events WHERE e.subject == '/test' MAP COUNT() < 100") }
        );
    }

    [Fact]
    public async Task CommitEventsAsync_WithMultipleMixedPreconditions()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                var preconditions = json.GetProperty("preconditions");
                return preconditions.GetArrayLength() == 2 &&
                       preconditions[0].GetProperty("type").GetString() == "isSubjectExisting" &&
                       preconditions[1].GetProperty("type").GetString() == "isQueryResultTrue";
            })
            .Respond(HttpStatusCode.OK);

        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent { Source = "test", Subject = "/user/456", Type = "test.user-updated", Data = new { name = "Jane" } }
            },
            new[]
            {
                Preconditions.IsSubjectExisting("/user/456"),
                Preconditions.IsQueryResultTrue("STREAM e FROM events WHERE e.data.email == 'john.doe@example.com' MAP COUNT() == 0")
            }
        );
    }

    [Fact]
    public async Task CommitEventsAsync_WithGenericPrecondition()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("preconditions")[0]
                    .GetProperty("type").GetString() == "someCustomFuturePrecondition";
            })
            .Respond(HttpStatusCode.OK);

        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent { Source = "test", Subject = "/test", Type = "test.created", Data = new { } }
            },
            new[]
            {
                new Precondition
                {
                    Type = "someCustomFuturePrecondition",
                    Payload = new { foo = "bar", baz = 123 }
                }
            }
        );
    }

    [Fact]
    public async Task CommitEventsAsync_ApiError_ThrowsException()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/commit")
            .Respond(HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<GenesisDbException>(
            () => _client.CommitEventsAsync(new[]
            {
                new CommitEvent { Source = "test", Subject = "/test", Type = "test.event", Data = new { } }
            }));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    #endregion

    #region EraseData Tests

    [Fact]
    public async Task EraseDataAsync_SendsCorrectRequest()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/erase")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("subject").GetString() == "/test/subject";
            })
            .Respond(HttpStatusCode.OK);

        await _client.EraseDataAsync("/test/subject");
    }

    [Fact]
    public async Task EraseDataAsync_ApiError_ThrowsException()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/erase")
            .Respond(HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<GenesisDbException>(
            () => _client.EraseDataAsync("/test/subject"));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    #endregion

    #region Audit Tests

    [Fact]
    public async Task AuditAsync_ReturnsAuditString()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:8080/api/v1/status/audit")
            .Respond("text/plain", "Audit successful");

        var result = await _client.AuditAsync();
        Assert.Equal("Audit successful", result);
    }

    [Fact]
    public async Task AuditAsync_ApiError_ThrowsException()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:8080/api/v1/status/audit")
            .Respond(HttpStatusCode.InternalServerError);

        var ex = await Assert.ThrowsAsync<GenesisDbException>(() => _client.AuditAsync());
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    #endregion

    #region Ping Tests

    [Fact]
    public async Task PingAsync_ReturnsPong()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:8080/api/v1/status/ping")
            .Respond("text/plain", "pong");

        var result = await _client.PingAsync();
        Assert.Equal("pong", result);
    }

    [Fact]
    public async Task PingAsync_ApiError_ThrowsException()
    {
        _mockHttp.When(HttpMethod.Get, "http://localhost:8080/api/v1/status/ping")
            .Respond(HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<GenesisDbException>(() => _client.PingAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task QueryAsync_ReturnsResults()
    {
        var result1 = new { id = "1", name = "Result 1" };
        var result2 = new { id = "2", name = "Result 2" };

        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/q")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("query").GetString() == "STREAM e FROM events MAP e.data";
            })
            .Respond("application/x-ndjson",
                $"{JsonSerializer.Serialize(result1)}\n{JsonSerializer.Serialize(result2)}\n");

        var results = await _client.QueryAsync("STREAM e FROM events MAP e.data");

        Assert.Equal(2, results.Count);
        Assert.Equal("1", results[0].GetProperty("id").GetString());
        Assert.Equal("2", results[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task QueryAsync_EmptyResults_ReturnsEmptyList()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/q")
            .Respond("application/x-ndjson", "");

        var results = await _client.QueryAsync("STREAM e FROM events WHERE false");
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_ApiError_ThrowsException()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/q")
            .Respond(HttpStatusCode.BadRequest);

        var ex = await Assert.ThrowsAsync<GenesisDbException>(
            () => _client.QueryAsync("INVALID QUERY"));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task QueryEventsAsync_DelegatesToQueryAsync()
    {
        var result = new { id = "1", name = "Result" };

        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/q")
            .Respond("application/x-ndjson", JsonSerializer.Serialize(result) + "\n");

        var results = await _client.QueryEventsAsync("STREAM e FROM events");
        Assert.Single(results);
    }

    #endregion

    #region ObserveEvents Tests

    [Fact]
    public async Task ObserveEventsAsync_YieldsEvents()
    {
        var event1 = new { id = "1", source = "test", subject = "/test", type = "test.event", data = new { n = 1 } };
        var event2 = new { id = "2", source = "test", subject = "/test", type = "test.event", data = new { n = 2 } };

        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/observe")
            .Respond("application/x-ndjson",
                $"{JsonSerializer.Serialize(event1)}\n{JsonSerializer.Serialize(event2)}\n");

        var events = new List<CloudEvent>();
        await foreach (var e in _client.ObserveEventsAsync("/test"))
        {
            events.Add(e);
        }

        Assert.Equal(2, events.Count);
        Assert.Equal("1", events[0].Id);
        Assert.Equal("2", events[1].Id);
    }

    [Fact]
    public async Task ObserveEventsAsync_HandlesSseFormat()
    {
        var ev = new { id = "1", source = "test", subject = "/test", type = "test.event", data = new { n = 1 } };

        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/observe")
            .Respond("application/x-ndjson", $"data: {JsonSerializer.Serialize(ev)}\n");

        var events = new List<CloudEvent>();
        await foreach (var e in _client.ObserveEventsAsync("/test"))
        {
            events.Add(e);
        }

        Assert.Single(events);
        Assert.Equal("1", events[0].Id);
    }

    [Fact]
    public async Task ObserveEventsAsync_WithOptions_SendsCorrectBody()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/observe")
            .With(req =>
            {
                var body = req.Content!.ReadAsStringAsync().Result;
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                return json.GetProperty("options").GetProperty("lowerBound").GetString() == "123" &&
                       json.GetProperty("options").GetProperty("includeLowerBoundEvent").GetBoolean() == true;
            })
            .Respond("application/x-ndjson", "");

        await foreach (var _ in _client.ObserveEventsAsync("/test", new StreamOptions
        {
            LowerBound = "123",
            IncludeLowerBoundEvent = true
        }))
        {
            break;
        }
    }

    [Fact]
    public async Task ObserveEventsAsync_ApiError_ThrowsException()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/observe")
            .Respond(HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<GenesisDbException>(async () =>
        {
            await foreach (var _ in _client.ObserveEventsAsync("/test"))
            {
                break;
            }
        });
    }

    #endregion

    #region Authorization Header Tests

    [Fact]
    public async Task AllRequests_IncludeBearerToken()
    {
        _mockHttp.When(HttpMethod.Post, "http://localhost:8080/api/v1/stream")
            .With(req => req.Headers.Authorization?.Scheme == "Bearer" &&
                         req.Headers.Authorization?.Parameter == "test-token")
            .Respond("application/x-ndjson", "");

        await _client.StreamEventsAsync("/test");
    }

    #endregion
}
