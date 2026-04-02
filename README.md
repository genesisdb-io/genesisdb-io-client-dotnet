# .NET SDK

This is the official .NET SDK for GenesisDB, an awesome and production ready event sourcing database system for building event-driven apps.

## GenesisDB Advantages

* Incredibly fast when reading, fast when writing 🚀
* Easy backup creation and recovery
* [CloudEvents](https://cloudevents.io/) compatible
* GDPR-ready
* Easily accessible via the HTTP interface
* Auditable. Guarantee database consistency
* Logging and metrics for Prometheus
* SQL like query language called GenesisDB Query Language (GDBQL)
* ...

## Installation

### NuGet Package

```bash
dotnet add package GenesisDB.Client
```

### Package Manager

```powershell
Install-Package GenesisDB.Client
```

## Configuration

### Environment Variables

The following environment variables are required when no explicit configuration is provided:

```
GENESISDB_AUTH_TOKEN=<secret>
GENESISDB_API_URL=http://localhost:8080
GENESISDB_API_VERSION=v1
```

### Basic Setup

```csharp
using GenesisDB.Client;

// Initialize from environment variables
using var client = new GenesisDbClient();

// Or initialize with explicit configuration
using var client = new GenesisDbClient(new ClientConfig
{
    ApiUrl = "http://localhost:8080",
    ApiVersion = "v1",
    AuthToken = "secret"
});
```

### Custom HttpClient (for DI / testing)

```csharp
using var client = new GenesisDbClient(
    new ClientConfig
    {
        ApiUrl = "http://localhost:8080",
        ApiVersion = "v1",
        AuthToken = "secret"
    },
    httpClient  // your own HttpClient instance
);
```

## Streaming Events

### Basic Event Streaming

```csharp
// Stream all events for a subject
var events = await client.StreamEventsAsync("/customer");

foreach (var e in events)
{
    Console.WriteLine($"{e.Id}: {e.Type} — {e.GetDataValue<string>("firstName")}");
}
```

### Stream Events from Lower Bound

```csharp
var events = await client.StreamEventsAsync("/", new StreamOptions
{
    LowerBound = "2d6d4141-6107-4fb2-905f-445730f4f2a9",
    IncludeLowerBoundEvent = true
});
```

### Stream Events with Upper Bound

```csharp
var events = await client.StreamEventsAsync("/", new StreamOptions
{
    UpperBound = "9f3e4141-7208-4fb2-905f-445730f4f3b1",
    IncludeUpperBoundEvent = false
});
```

### Stream Events with Both Lower and Upper Bounds

```csharp
var events = await client.StreamEventsAsync("/", new StreamOptions
{
    LowerBound = "2d6d4141-6107-4fb2-905f-445730f4f2a9",
    IncludeLowerBoundEvent = true,
    UpperBound = "9f3e4141-7208-4fb2-905f-445730f4f3b1",
    IncludeUpperBoundEvent = false
});
```

### Stream Latest Events by Event Type

```csharp
var events = await client.StreamEventsAsync("/", new StreamOptions
{
    LatestByEventType = "io.genesisdb.app.customer-updated"
});
```

This feature allows you to stream only the latest event of a specific type for each subject. Useful for getting the current state of entities.

## Committing Events

### Basic Event Committing

```csharp
await client.CommitEventsAsync(new[]
{
    new CommitEvent
    {
        Source = "io.genesisdb.app",
        Subject = "/customer",
        Type = "io.genesisdb.app.customer-added",
        Data = new
        {
            firstName = "Bruce",
            lastName = "Wayne",
            emailAddress = "bruce.wayne@enterprise.wayne"
        }
    },
    new CommitEvent
    {
        Source = "io.genesisdb.app",
        Subject = "/customer",
        Type = "io.genesisdb.app.customer-added",
        Data = new
        {
            firstName = "Alfred",
            lastName = "Pennyworth",
            emailAddress = "alfred.pennyworth@enterprise.wayne"
        }
    },
    new CommitEvent
    {
        Source = "io.genesisdb.store",
        Subject = "/article",
        Type = "io.genesisdb.store.article-added",
        Data = new
        {
            name = "Tumbler",
            color = "black",
            price = 2990000.00
        }
    },
    new CommitEvent
    {
        Source = "io.genesisdb.app",
        Subject = "/customer/fed2902d-0135-460d-8605-263a06308448",
        Type = "io.genesisdb.app.customer-personaldata-changed",
        Data = new
        {
            firstName = "Angus",
            lastName = "MacGyver",
            emailAddress = "angus.macgyer@phoenix.foundation"
        }
    }
});
```

## Preconditions

Preconditions allow you to enforce certain checks on the server before committing events. GenesisDB supports multiple precondition types:

### isSubjectNew

Ensures that a subject is new (has no existing events):

```csharp
await client.CommitEventsAsync(
    new[]
    {
        new CommitEvent
        {
            Source = "io.genesisdb.app",
            Subject = "/user/456",
            Type = "io.genesisdb.app.user-created",
            Data = new
            {
                firstName = "John",
                lastName = "Doe",
                email = "john.doe@example.com"
            }
        }
    },
    new[] { Preconditions.IsSubjectNew("/user/456") }
);
```

### isSubjectExisting

Ensures that events exist for the specified subject:

```csharp
await client.CommitEventsAsync(
    new[]
    {
        new CommitEvent
        {
            Source = "io.genesisdb.app",
            Subject = "/user/456",
            Type = "io.genesisdb.app.user-updated",
            Data = new
            {
                firstName = "John",
                lastName = "Doe",
                email = "john.doe@example.com"
            }
        }
    },
    new[] { Preconditions.IsSubjectExisting("/user/456") }
);
```

### isQueryResultTrue

Evaluates a query and ensures the result is truthy. Supports the full GDBQL feature set including complex WHERE clauses, aggregations, and calculated fields.

**Basic uniqueness check:**

```csharp
await client.CommitEventsAsync(
    new[]
    {
        new CommitEvent
        {
            Source = "io.genesisdb.app",
            Subject = "/user/456",
            Type = "io.genesisdb.app.user-created",
            Data = new
            {
                firstName = "John",
                lastName = "Doe",
                email = "john.doe@example.com"
            }
        }
    },
    new[] { Preconditions.IsQueryResultTrue(
        "STREAM e FROM events WHERE e.data.email == 'john.doe@example.com' MAP COUNT() == 0"
    ) }
);
```

**Business rule enforcement (transaction limits):**

```csharp
await client.CommitEventsAsync(
    new[]
    {
        new CommitEvent
        {
            Source = "io.genesisdb.banking",
            Subject = "/user/123/transactions",
            Type = "io.genesisdb.banking.transaction-processed",
            Data = new { amount = 500.00, currency = "EUR" }
        }
    },
    new[] { Preconditions.IsQueryResultTrue(
        "STREAM e FROM events WHERE e.subject UNDER '/user/123' AND e.type == 'transaction-processed' AND e.time >= '2024-01-01T00:00:00Z' MAP SUM(e.data.amount) + 500 <= 10000"
    ) }
);
```

**Complex validation with aggregations:**

```csharp
await client.CommitEventsAsync(
    new[]
    {
        new CommitEvent
        {
            Source = "io.genesisdb.events",
            Subject = "/conference/2024/registrations",
            Type = "io.genesisdb.events.registration-created",
            Data = new { attendeeId = "att-789", ticketType = "premium" }
        }
    },
    new[] { Preconditions.IsQueryResultTrue(
        "STREAM e FROM events WHERE e.subject UNDER '/conference/2024/registrations' AND e.type == 'registration-created' GROUP BY e.data.ticketType HAVING e.data.ticketType == 'premium' MAP COUNT() < 50"
    ) }
);
```

### Generic Preconditions

For forward compatibility with future precondition types, you can use the generic `Precondition` class directly:

```csharp
await client.CommitEventsAsync(
    events,
    new[]
    {
        new Precondition
        {
            Type = "someCustomFuturePrecondition",
            Payload = new { foo = "bar", baz = 123 }
        }
    }
);
```

**Supported GDBQL Features in Preconditions:**
- WHERE conditions with AND/OR/IN/BETWEEN operators
- Hierarchical subject queries (UNDER, DESCENDANTS)
- Aggregation functions (COUNT, SUM, AVG, MIN, MAX)
- GROUP BY with HAVING clauses
- ORDER BY and LIMIT clauses
- Calculated fields and expressions
- Nested field access (e.data.address.city)
- String concatenation and arithmetic operations

If a precondition fails, the commit throws a `GenesisDbException` with HTTP 412 (Precondition Failed).

## GDPR Compliance

### Store Data as Reference

```csharp
await client.CommitEventsAsync(new[]
{
    new CommitEvent
    {
        Source = "io.genesisdb.app",
        Subject = "/user/456",
        Type = "io.genesisdb.app.user-created",
        Data = new
        {
            firstName = "John",
            lastName = "Doe",
            email = "john.doe@example.com"
        },
        Options = new CommitEventOptions { StoreDataAsReference = true }
    }
});
```

### Delete Referenced Data

```csharp
await client.EraseDataAsync("/user/456");
```

## Observing Events

The `ObserveEventsAsync` method returns an `IAsyncEnumerable<CloudEvent>`, which keeps the connection open and yields events as they arrive in real time.

### Basic Event Observation

```csharp
await foreach (var e in client.ObserveEventsAsync("/customer"))
{
    Console.WriteLine($"Received: {e.Id} — {e.Type}");
}
```

### Observe Events from Lower Bound (Message Queue)

```csharp
await foreach (var e in client.ObserveEventsAsync("/customer", new StreamOptions
{
    LowerBound = "2d6d4141-6107-4fb2-905f-445730f4f2a9",
    IncludeLowerBoundEvent = true
}))
{
    Console.WriteLine($"Received: {e.Id}");
}
```

### Observe Events with Upper Bound (Message Queue)

```csharp
await foreach (var e in client.ObserveEventsAsync("/customer", new StreamOptions
{
    UpperBound = "9f3e4141-7208-4fb2-905f-445730f4f3b1",
    IncludeUpperBoundEvent = false
}))
{
    Console.WriteLine($"Received: {e.Id}");
}
```

### Observe Events with Both Bounds (Message Queue)

```csharp
await foreach (var e in client.ObserveEventsAsync("/customer", new StreamOptions
{
    LowerBound = "2d6d4141-6107-4fb2-905f-445730f4f2a9",
    IncludeLowerBoundEvent = true,
    UpperBound = "9f3e4141-7208-4fb2-905f-445730f4f3b1",
    IncludeUpperBoundEvent = false
}))
{
    Console.WriteLine($"Received: {e.Id}");
}
```

### Observe Latest Events by Event Type (Message Queue)

```csharp
await foreach (var e in client.ObserveEventsAsync("/customer", new StreamOptions
{
    LatestByEventType = "io.genesisdb.app.customer-updated"
}))
{
    Console.WriteLine($"Received latest: {e.Id}");
}
```

### Cancellation Support

Use a `CancellationToken` to gracefully stop observing:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

await foreach (var e in client.ObserveEventsAsync("/customer", cancellationToken: cts.Token))
{
    Console.WriteLine($"Received: {e.Id}");

    if (someCondition)
    {
        cts.Cancel(); // stop observing
    }
}
```

## Querying Events

```csharp
var results = await client.QueryEventsAsync(
    "STREAM e FROM events WHERE e.type == 'io.genesisdb.app.customer-added' ORDER BY e.time DESC LIMIT 20 MAP { subject: e.subject, firstName: e.data.firstName }"
);

foreach (var result in results)
{
    Console.WriteLine($"{result.GetProperty("subject")} — {result.GetProperty("firstName")}");
}
```

Query results are returned as `List<JsonElement>`, giving you full flexibility to access any shape of data:

```csharp
var results = await client.QueryAsync(
    "STREAM e FROM events WHERE e.subject == '/customer/123' MAP e.data"
);

foreach (var row in results)
{
    var firstName = row.GetProperty("firstName").GetString();
    var age = row.GetProperty("age").GetInt32();
}
```

## Working with CloudEvent Data

The `CloudEvent` class provides helper methods for accessing event data:

```csharp
var events = await client.StreamEventsAsync("/customer");

foreach (var e in events)
{
    // Access individual properties
    var firstName = e.GetDataValue<string>("firstName");
    var age = e.GetDataValue<int>("age");

    // Or deserialize the entire data payload to a typed object
    var customer = e.GetData<CustomerData>();
}
```

## Error Handling

API errors throw a `GenesisDbException` with the HTTP status code and reason:

```csharp
try
{
    await client.CommitEventsAsync(events, preconditions);
}
catch (GenesisDbException ex) when (ex.StatusCode == System.Net.HttpStatusCode.PreconditionFailed)
{
    Console.WriteLine("Precondition failed — commit rejected");
}
catch (GenesisDbException ex)
{
    Console.WriteLine($"API error: {ex.StatusCode} — {ex.ReasonPhrase}");
}
```

## Health Checks

```csharp
// Check API status
var pingResponse = await client.PingAsync();
Console.WriteLine($"Ping: {pingResponse}");

// Run audit to check event consistency
var auditResponse = await client.AuditAsync();
Console.WriteLine($"Audit: {auditResponse}");
```

## API Reference

| Method | Description |
|---|---|
| `PingAsync()` | Pings the server, returns a string response |
| `AuditAsync()` | Runs an audit check, returns audit information |
| `CommitEventsAsync(events, preconditions?)` | Commits one or more events with optional preconditions |
| `StreamEventsAsync(subject, options?)` | Streams events for a subject, returns `List<CloudEvent>` |
| `EraseDataAsync(subject)` | Erases referenced data for GDPR compliance |
| `QueryAsync(query)` | Executes a GDBQL query, returns `List<JsonElement>` |
| `QueryEventsAsync(query)` | Alias for `QueryAsync` |
| `ObserveEventsAsync(subject, options?, cancellationToken)` | Observes events in real time via `IAsyncEnumerable<CloudEvent>` |

## Running Tests

```bash
# Unit tests (no server required)
dotnet test --filter "Category!=Integration"

# Integration tests (requires GenesisDB on localhost:8080)
dotnet test --filter "Category=Integration"

# All tests
dotnet test
```

## Requirements

- .NET 8.0 or later

## License

MIT

## Author

* E-Mail: mail@genesisdb.io
* URL: https://www.genesisdb.io
* Docs: https://docs.genesisdb.io
