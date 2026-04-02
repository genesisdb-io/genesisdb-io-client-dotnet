using System.Text.Json;

namespace GenesisDB.Client.Tests;

/// <summary>
/// Integration tests that run against a live GenesisDB instance on port 8080.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class GenesisDbClientIntegrationTests : IDisposable
{
    private readonly GenesisDbClient _client;

    public GenesisDbClientIntegrationTests()
    {
        _client = new GenesisDbClient(new ClientConfig
        {
            ApiUrl = "http://localhost:8080",
            ApiVersion = "v1",
            AuthToken = "secret"
        });
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task Ping_ReturnsResponse()
    {
        var response = await _client.PingAsync();
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task Audit_ReturnsResponse()
    {
        var response = await _client.AuditAsync();
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task CommitAndStream_CustomerEvents()
    {
        var uniqueId = $"integration-test-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/customer/{uniqueId}";

        // Commit customer event
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.customer-added",
                Data = new
                {
                    firstName = "Bruce",
                    lastName = "Wayne",
                    emailAddress = "bruce.wayne@enterprise.wayne"
                }
            }
        });

        // Stream events back
        var events = await _client.StreamEventsAsync(subject);
        Assert.NotEmpty(events);
        Assert.True(events.Count >= 1, "Should have at least one event");

        var customerEvent = events.Find(e => e.Type == "io.genesisdb.app.customer-added");
        Assert.NotNull(customerEvent);
        Assert.Equal("Bruce", customerEvent.GetDataValue<string>("firstName"));
        Assert.Equal("Wayne", customerEvent.GetDataValue<string>("lastName"));
        Assert.Equal("bruce.wayne@enterprise.wayne", customerEvent.GetDataValue<string>("emailAddress"));
    }

    [Fact]
    public async Task CommitMultipleEvents_InSingleCommit()
    {
        var uniqueId = $"batch-test-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = $"/customer/{uniqueId}",
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Bruce", lastName = "Wayne", emailAddress = "bruce.wayne@enterprise.wayne" }
            },
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = $"/customer/{uniqueId}",
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Alfred", lastName = "Pennyworth", emailAddress = "alfred.pennyworth@enterprise.wayne" }
            },
            new CommitEvent
            {
                Source = "io.genesisdb.store",
                Subject = $"/article/{uniqueId}",
                Type = "io.genesisdb.store.article-added",
                Data = new { name = "Tumbler", color = "black", price = 2990000.00 }
            }
        });

        // Stream customer events
        var customerEvents = await _client.StreamEventsAsync($"/customer/{uniqueId}");
        Assert.Equal(2, customerEvents.Count);

        // Stream article events
        var articleEvents = await _client.StreamEventsAsync($"/article/{uniqueId}");
        Assert.Single(articleEvents);
        Assert.Equal("Tumbler", articleEvents[0].GetDataValue<string>("name"));
        Assert.Equal(2990000.00, articleEvents[0].GetDataValue<double>("price"));
    }

    [Fact]
    public async Task CommitEvents_PersonalDataChange()
    {
        var uniqueId = $"personaldata-test-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/customer/{uniqueId}";

        // First add a customer
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Bruce", lastName = "Wayne", emailAddress = "bruce.wayne@enterprise.wayne" }
            }
        });

        // Then change personal data
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.customer-personaldata-changed",
                Data = new { firstName = "Angus", lastName = "MacGyver", emailAddress = "angus.macgyer@phoenix.foundation" }
            }
        });

        var events = await _client.StreamEventsAsync(subject);
        Assert.Equal(2, events.Count);

        var changedEvent = events.Find(e => e.Type == "io.genesisdb.app.customer-personaldata-changed");
        Assert.NotNull(changedEvent);
        Assert.Equal("Angus", changedEvent.GetDataValue<string>("firstName"));
        Assert.Equal("MacGyver", changedEvent.GetDataValue<string>("lastName"));
    }

    [Fact]
    public async Task QueryEvents_WithGDBQL()
    {
        var uniqueId = $"query-test-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

        // Commit some events to query
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = $"/customer/{uniqueId}",
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Bruce", lastName = "Wayne", emailAddress = "bruce.wayne@enterprise.wayne" }
            },
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = $"/customer/{uniqueId}",
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Alfred", lastName = "Pennyworth", emailAddress = "alfred.pennyworth@enterprise.wayne" }
            }
        });

        var results = await _client.QueryEventsAsync(
            $"STREAM e FROM events WHERE e.subject == '/customer/{uniqueId}' ORDER BY e.time MAP {{ id: e.id, firstName: e.data.firstName }}");

        Assert.Equal(2, results.Count);
        Assert.NotNull(results[0].GetProperty("id").GetString());
        Assert.NotNull(results[0].GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task CommitEvents_WithIsSubjectNewPrecondition()
    {
        var uniqueId = $"precondition-new-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/user/{uniqueId}";

        // This should succeed since it's a new subject
        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent
                {
                    Source = "io.genesisdb.app",
                    Subject = subject,
                    Type = "io.genesisdb.app.user-created",
                    Data = new { firstName = "John", lastName = "Doe", email = "john.doe@example.com" }
                }
            },
            new[] { Preconditions.IsSubjectNew(subject) }
        );

        // Committing again with isSubjectNew should fail (HTTP 412)
        var ex = await Assert.ThrowsAsync<GenesisDbException>(() =>
            _client.CommitEventsAsync(
                new[]
                {
                    new CommitEvent
                    {
                        Source = "io.genesisdb.app",
                        Subject = subject,
                        Type = "io.genesisdb.app.user-created",
                        Data = new { firstName = "Jane", lastName = "Doe", email = "jane.doe@example.com" }
                    }
                },
                new[] { Preconditions.IsSubjectNew(subject) }
            ));
        Assert.Contains("API Error", ex.Message);
    }

    [Fact]
    public async Task CommitEvents_WithIsSubjectExistingPrecondition()
    {
        var uniqueId = $"precondition-existing-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/user/{uniqueId}";

        // First create the subject
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.user-created",
                Data = new { firstName = "John", lastName = "Doe", email = "john.doe@example.com" }
            }
        });

        // This should succeed since the subject now exists
        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent
                {
                    Source = "io.genesisdb.app",
                    Subject = subject,
                    Type = "io.genesisdb.app.user-updated",
                    Data = new { firstName = "John", lastName = "Doe", email = "john.doe@example.com" }
                }
            },
            new[] { Preconditions.IsSubjectExisting(subject) }
        );
    }

    [Fact]
    public async Task CommitEvents_WithIsQueryResultTruePrecondition()
    {
        var uniqueId = $"precondition-query-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/user/{uniqueId}";

        // This should succeed since no events with this email exist yet
        await _client.CommitEventsAsync(
            new[]
            {
                new CommitEvent
                {
                    Source = "io.genesisdb.app",
                    Subject = subject,
                    Type = "io.genesisdb.app.user-created",
                    Data = new { firstName = "John", lastName = "Doe", email = $"john.doe.{uniqueId}@example.com" }
                }
            },
            new[] { Preconditions.IsQueryResultTrue($"STREAM e FROM events WHERE e.data.email == 'john.doe.{uniqueId}@example.com' MAP COUNT() == 0") }
        );
    }

    [Fact]
    public async Task StreamEvents_WithLowerAndUpperBounds()
    {
        var uniqueId = $"bounds-test-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/customer/{uniqueId}";

        // Commit 3 events separately
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Bruce", lastName = "Wayne", emailAddress = "bruce@wayne.com" }
            }
        });
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Alfred", lastName = "Pennyworth", emailAddress = "alfred@wayne.com" }
            }
        });
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Angus", lastName = "MacGyver", emailAddress = "angus@phoenix.foundation" }
            }
        });

        // Stream all events to get the IDs
        var allEvents = await _client.StreamEventsAsync(subject);
        Assert.Equal(3, allEvents.Count);

        // Stream from lower bound (skip the first event)
        var fromSecond = await _client.StreamEventsAsync(subject, new StreamOptions
        {
            LowerBound = allEvents[0].Id!,
            IncludeLowerBoundEvent = false
        });
        Assert.Equal(2, fromSecond.Count);

        // Stream with upper bound (exclude the last event)
        var untilSecond = await _client.StreamEventsAsync(subject, new StreamOptions
        {
            UpperBound = allEvents[2].Id!,
            IncludeUpperBoundEvent = false
        });
        Assert.Equal(2, untilSecond.Count);

        // Stream with both bounds (only the middle event)
        var middle = await _client.StreamEventsAsync(subject, new StreamOptions
        {
            LowerBound = allEvents[0].Id!,
            IncludeLowerBoundEvent = false,
            UpperBound = allEvents[2].Id!,
            IncludeUpperBoundEvent = false
        });
        Assert.Single(middle);
        Assert.Equal("Alfred", middle[0].GetDataValue<string>("firstName"));
    }

    [Fact]
    public async Task StoreDataAsReference_AndErase_GDPR()
    {
        var uniqueId = $"gdpr-test-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/user/{uniqueId}";

        // Commit with storeDataAsReference
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.user-created",
                Data = new { firstName = "John", lastName = "Doe", email = "john.doe@example.com" },
                Options = new CommitEventOptions { StoreDataAsReference = true }
            }
        });

        // Verify event was committed
        var eventsBefore = await _client.StreamEventsAsync(subject);
        Assert.Single(eventsBefore);
        Assert.Equal("John", eventsBefore[0].GetDataValue<string>("firstName"));

        // Erase referenced data
        await _client.EraseDataAsync(subject);

        // Verify data is erased — the event still exists but data fields should be emptied
        var eventsAfter = await _client.StreamEventsAsync(subject);
        Assert.Single(eventsAfter);

        Assert.Equal("", eventsAfter[0].GetDataValue<string>("firstName"));
        Assert.Equal("", eventsAfter[0].GetDataValue<string>("lastName"));
        Assert.Equal("", eventsAfter[0].GetDataValue<string>("email"));
    }

    [Fact]
    public async Task ObserveEvents_ReceivesExistingEvent()
    {
        var uniqueId = $"observe-test-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var subject = $"/customer/{uniqueId}";

        // Commit an event first
        await _client.CommitEventsAsync(new[]
        {
            new CommitEvent
            {
                Source = "io.genesisdb.app",
                Subject = subject,
                Type = "io.genesisdb.app.customer-added",
                Data = new { firstName = "Bruce", lastName = "Wayne", emailAddress = "bruce.wayne@enterprise.wayne" }
            }
        });

        // Get the event to know its ID
        var events = await _client.StreamEventsAsync(subject);
        var firstEventId = events[0].Id!;

        // Observe with lowerBound to pick up the existing event
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<CloudEvent>();

        await foreach (var e in _client.ObserveEventsAsync(subject, new StreamOptions
        {
            LowerBound = firstEventId,
            IncludeLowerBoundEvent = true
        }, cts.Token))
        {
            received.Add(e);
            break; // got our event, stop
        }

        Assert.Single(received);
        Assert.Equal("Bruce", received[0].GetDataValue<string>("firstName"));
    }
}
