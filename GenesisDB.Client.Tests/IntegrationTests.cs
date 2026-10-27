using System.Text.Json;

namespace GenesisDB.Client.Tests;

/// <summary>
/// Runs against a live GenesisDB. Skipped unless GENESISDB_URL and GENESISDB_AUTH_TOKEN (an administrator token)
/// are set. Writes under /sdk-test-dotnet/<run id>.
/// </summary>
public sealed class IntegrationTests
{
    private static readonly string Run = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x");
    private static readonly string Root = "/sdk-test-dotnet/" + Run;

    private static bool Configured =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENESISDB_URL")) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GENESISDB_AUTH_TOKEN"));

    private static NewEvent Event(string subject, string type, object data) => new("sdk-test", subject, type, data);

    [Fact]
    public async Task FullRoundTrip()
    {
        if (!Configured) return;
        using var db = new GenesisDbClient();
        Assert.Equal("Pong", await db.PingAsync());

        // commit, fetch, query
        var stored = await db.CommitAsync(new[]
        {
            Event($"{Root}/customer/1", "sdk.customer-added", new { name = "Bruce", age = 40 }),
            Event($"{Root}/customer/2", "sdk.customer-added", new { name = "Selina", age = 35 }),
        });
        Assert.Equal(2, stored.Count);
        Assert.Equal(36, stored[0].Id.Length);
        Assert.Equal(64, stored[0].Hash.Length);
        Assert.Equal(stored[0].Hash, stored[1].PredecessorHash);

        var fetched = await db.FetchAsync(Root);
        Assert.Equal(new[] { "Bruce", "Selina" }, fetched.Select(e => e.Data.GetProperty("name").GetString()));

        var bounded = await db.FetchAsync(Root, new FetchOptions { LowerBound = stored[0].Id, IncludeLowerBoundEvent = false });
        Assert.Equal(new[] { stored[1].Id }, bounded.Select(e => e.Id));

        var rows = await db.QueryAsync($"STREAM e FROM events WHERE e.subject DESCENDANTS '{Root}' ORDER BY e.data.age MAP {{ name: e.data.name }}");
        Assert.Equal(new[] { "Selina", "Bruce" }, rows.Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(new[] { 2 }, await db.QueryAsync<int>($"STREAM e FROM events WHERE e.subject DESCENDANTS '{Root}' MAP COUNT()"));

        // preconditions
        var failed = await Assert.ThrowsAsync<PreconditionFailedException>(() => db.CommitAsync(
            new[] { Event($"{Root}/customer/1", "sdk.noop", new { }) },
            new[] { Precondition.IsSubjectNew($"{Root}/customer/1") }));
        Assert.Equal("isSubjectNew", failed.PreconditionType);

        // catalog
        Assert.Contains("/sdk-test-dotnet", await db.SubjectsAsync());
        Assert.Contains("sdk.customer-added", await db.TypesAsync());

        // reference payloads and erase
        var user = $"{Root}/user/1";
        var committed = await db.CommitAsync(new[] { Event(user, "sdk.user-added", new { email = "bruce@example.com" }).AsReference() });
        Assert.Equal("bruce@example.com", committed[0].Data.GetProperty("email").GetString());
        Assert.Equal("bruce@example.com", (await db.FetchAsync(user))[0].Data.GetProperty("email").GetString());
        Assert.Contains("Erased 1 ref data", await db.EraseAsync(user));
        Assert.Equal("{}", (await db.FetchAsync(user))[0].Data.GetRawText());
        await Assert.ThrowsAsync<UnprocessableException>(() => db.EraseAsync($"{Root}/customer/1"));

        // schemas
        var schemaType = $"sdk.schema-test-{Run}";
        await db.RegisterSchemaAsync(schemaType, new { type = "object", properties = new { n = new { type = "number" } }, required = new[] { "n" } });
        await Assert.ThrowsAsync<ConflictException>(() => db.RegisterSchemaAsync(schemaType, new { type = "object" }));
        Assert.Contains(await db.SchemasAsync(), s => s.Type == schemaType);
        var violation = await Assert.ThrowsAsync<GenesisDbException>(() => db.CommitAsync(new[] { Event($"{Root}/s/1", schemaType, new { n = "not a number" }) }));
        Assert.Equal(400, violation.Status);

        // subscribe
        var live = $"{Root}/live";
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<string>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var e in db.SubscribeAsync(live, new SubscribeOptions { Interval = TimeSpan.FromMilliseconds(100) }, stop.Token))
            {
                received.Add(e.Id);
                if (received.Count == 2) break;
            }
        });
        await Task.Delay(300);
        var liveEvents = await db.CommitAsync(new[]
        {
            Event($"{live}/1", "sdk.live", new { n = 1 }),
            Event($"{live}/2", "sdk.live", new { n = 2 }),
        });
        await consumer;
        Assert.Equal(liveEvents.Select(e => e.Id), received);

        // status, audit
        var status = await db.StatusAsync();
        Assert.True(status.Storage.Ready);
        Assert.True(status.Events.Count >= 5);
        Assert.StartsWith("Audit passed", await db.AuditAsync());
    }
}
