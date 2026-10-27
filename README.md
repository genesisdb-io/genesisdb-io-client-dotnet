# GenesisDB client for .NET

The official client for [GenesisDB](https://www.genesisdb.io), the open source event sourcing database. .NET 6 and 8, async throughout, built on `HttpClient` and `System.Text.Json`, no further dependencies.

> **Version 2.0.0 targets GenesisDB 2.0 and is not compatible with GenesisDB 1.x servers or with the 1.x client API.** Routes, configuration, type names and method signatures have changed. Keep a 1.x client for a 1.x server.
>
> Existing 1.x clients keep working against a GenesisDB 2.0 server through deprecated route aliases, so the server can be upgraded first and the clients at your own pace. New 2.0 features (stored events returned by commit, upper bounds, schemas, typed errors) need this 2.0.0 client.

```bash
dotnet add package GenesisDB.Client
```

## Quick start

```csharp
using GenesisDB.Client;

using var db = new GenesisDbClient("http://localhost:8080", Environment.GetEnvironmentVariable("GENESISDB_AUTH_TOKEN"));

await db.CommitAsync(new[]
{
    new NewEvent("io.example.shop", "/customer/42", "io.example.shop.customer-registered", new { name = "Bruce Wayne" }),
});

var events = await db.FetchAsync("/customer/42");
```

`url` and `token` fall back to `GENESISDB_URL` and `GENESISDB_AUTH_TOKEN`, so `new GenesisDbClient()` works in an environment that sets both. The client is thread-safe and shares one `HttpClient`, register it as a singleton.

## Events

```csharp
// Commit a batch atomically. The server assigns ID, time and hashes and returns the stored events.
var stored = await db.CommitAsync(new[]
{
    new NewEvent("io.example.shop", "/order/1", "io.example.shop.order-placed", new { total = 99.5 }),
    new NewEvent("io.example.shop", "/order/1/item/1", "io.example.shop.item-added", new { sku = "A1", qty = 2 }),
});
stored[0].Id;    // "3f2c..."
stored[0].Hash;  // "a9b1..."
stored[0].Time;  // DateTimeOffset
stored[0].Data;  // JsonElement

// A subject with one segment gets a generated ID appended.
await db.CommitAsync(new[] { new NewEvent("io.example.shop", "/customer", "io.example.shop.customer-registered", new { name = "Selina" }) });

// Fetch a subject with all descendants, in commit order.
var all = await db.FetchAsync("/order/1");

// Resume after a known event, or stop at one.
var since = await db.FetchAsync("/order", new FetchOptions { LowerBound = lastSeenId, IncludeLowerBoundEvent = false });
var until = await db.FetchAsync("/order", new FetchOptions { UpperBound = someId, IncludeUpperBoundEvent = true });

// Only the newest event of a type below the subject.
var latest = await db.FetchAsync("/order/1", new FetchOptions { LatestByEventType = "io.example.shop.order-placed" });

// Typed payloads
record Order(double Total);
var order = stored[0].DataAs<Order>();
```

`data` accepts an anonymous object, a dictionary, a `JsonElement` or any class that serialises to a JSON object.

## Preconditions

Preconditions are checked atomically with the commit. A failed precondition throws `PreconditionFailedException` and writes nothing.

```csharp
try
{
    await db.CommitAsync(
        new[] { new NewEvent("io.example.shop", "/customer/42", "io.example.shop.customer-registered", new { email = "bruce@example.com" }) },
        new[]
        {
            Precondition.IsSubjectNew("/customer/42"),
            Precondition.IsQueryResultTrue("STREAM e FROM events WHERE e.data.email == 'bruce@example.com' MAP COUNT() == 0"),
        });
}
catch (PreconditionFailedException e)
{
    Console.WriteLine($"{e.PreconditionType}: {e.Reason}"); // "isSubjectNew: subject /customer/42 already has events"
}
```

Factories: `IsSubjectNew`, `IsSubjectExisting`, `IsQueryResultTrue`.

## Subscriptions

`SubscribeAsync` returns an `IAsyncEnumerable<Event>` over Server-Sent Events. It yields the existing history first, then new events as they are committed, and runs until the token is cancelled.

```csharp
using var stop = new CancellationTokenSource();

await foreach (var e in db.SubscribeAsync("/order", new SubscribeOptions { Interval = TimeSpan.FromMilliseconds(500) }, stop.Token))
{
    Console.WriteLine($"{e.Type} {e.Data}");
}
```

`Interval` is the poll interval (default 1 s). The bound options of `FetchOptions` work here as well through `SubscribeOptions.Fetch`, so a consumer that persists the last processed event ID resumes with `LowerBound`. A stream the server ends with an error throws `GenesisDbException`.

## Queries

[GDBQL](https://github.com/genesisdb-io/genesisdb/blob/main/docs/gdbql.md) runs on the server. Results come back as rows, a bare aggregate as a single-element list.

```csharp
var rows = await db.QueryAsync("STREAM e FROM events WHERE e.subject UNDER '/customer' MAP { name: e.data.name, total: e.data.total }");

record Row(string Name, double Total);
var typed = await db.QueryAsync<Row>("STREAM e FROM events WHERE e.subject UNDER '/customer' MAP { name: e.data.name, total: e.data.total }");

var count = await db.QueryAsync<int>("STREAM e FROM events WHERE e.type == 'io.example.shop.order-placed' MAP COUNT()");

var byCategory = await db.QueryAsync(
    "STREAM e FROM events WHERE e.type == 'io.example.shop.item-added' GROUP BY e.data.category MAP { category: e.data.category, items: COUNT(), revenue: SUM(e.data.total) }");
```

## GDPR: reference payloads and erasure

Store a payload as reference data and it can be erased later. The event, its hash and the chain stay intact, the payload becomes `{}`.

```csharp
await db.CommitAsync(new[]
{
    new NewEvent("io.example.shop", "/customer/42", "io.example.shop.customer-registered", new { name = "Bruce", email = "bruce@example.com" }).AsReference(),
});

await db.EraseAsync("/customer/42"); // administrator token
```

Inline payloads are immutable. `EraseAsync` on a subject without reference payloads throws `UnprocessableException` (422).

## Schemas

```csharp
await db.RegisterSchemaAsync("io.example.shop.order-placed", new
{
    type = "object",
    properties = new { total = new { type = "number" }, currency = new { type = "string" } },
    required = new[] { "total" },
});

var schemas = await db.SchemasAsync();
```

Commits of that type are validated. A violation throws `GenesisDbException` with status 400.

## Catalog, status, administration

```csharp
await db.SubjectsAsync();        // ["/customer", "/order"]
await db.TypesAsync();           // ["io.example.shop.customer-registered", ...]
await db.StatusAsync();          // engine version, event counts, storage format, readiness
await db.PingAsync();            // "Pong", no token required

await db.AuditAsync();           // "Audit passed. All 1234 events integrity checks succeeded."
```

Audit, erase and schema registration need the administrator token. Everything else works with a reader token (`GENESISDB_READ_TOKEN`), commit additionally with a writer token.

## Errors

Every non-2xx answer throws `GenesisDbException` with `Status`, `Body` and `Operation`. Subclasses: `PreconditionFailedException` (412, with `PreconditionType` and `Reason`), `AuthenticationException` (401, 403), `NotFoundException` (404), `ConflictException` (409), `UnprocessableException` (422), `NotReadyException` (503). Transport failures and timeouts throw `GenesisDbException` with status 0 and the cause as inner exception.

## Configuration

```csharp
new GenesisDbClient(
    url: "http://localhost:8080",          // or GENESISDB_URL
    token: "...",                          // or GENESISDB_AUTH_TOKEN
    timeout: TimeSpan.FromSeconds(30),     // for non-streaming calls
    http: new HttpClient(handler));        // optional, bring your own HttpClient (proxies, TLS, handlers)
```

## Development

```bash
dotnet test                                                                              # unit tests against a stubbed handler
GENESISDB_URL=http://localhost:8080 GENESISDB_AUTH_TOKEN=... dotnet test                 # also runs IntegrationTests against a running GenesisDB
```

## License

MIT
