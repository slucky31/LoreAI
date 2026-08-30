using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Infrastructure.Feed;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Feed;

/// <summary>
/// Lecteur pur (lot 9, #50, redesign) : pas de curseur interne, pas de <c>PollingState</c> — le curseur est
/// fourni par l'appelant à chaque appel (un sujet de veille = une catégorie), et sa persistance est de la
/// responsabilité de l'appelant (<c>TopicWatchJob</c>/<c>IWatchTopicRepository</c>).
/// </summary>
public class MinifluxWatchIngesterTests
{
    [Fact]
    public async Task GetNewEntriesAsync_NewEntries_ReturnsWatchItemsAndLastEntryId()
    {
        using var server = WireMockServer.Start();
        GivenEntries(server, categoryId: 7, afterEntryId: "100", entries:
        [
            (101, "Un article de veille", "https://blog.example.com/1", "2026-08-01T10:00:00Z"),
            (102, "Un autre article de veille", "https://blog.example.com/2", "2026-08-02T10:00:00Z"),
        ]);

        var ingester = CreateIngester(server);

        var (items, lastEntryId) = await ingester.GetNewEntriesAsync(7, "100", TestContext.Current.CancellationToken);

        Assert.Equal(2, items.Count);
        Assert.Equal(SourceType.Watch, items[0].SourceType);
        Assert.Equal("101", items[0].SourceId);
        Assert.Equal("https://blog.example.com/1", items[0].Url);
        Assert.Equal("Un article de veille", items[0].Title);
        Assert.Equal("102", items[1].SourceId);
        Assert.Equal("102", lastEntryId);
    }

    [Fact]
    public async Task GetNewEntriesAsync_NoNewEntries_ReturnsEmptyAndNullLastEntryId()
    {
        using var server = WireMockServer.Start();
        GivenEntries(server, categoryId: 7, afterEntryId: "100", entries: []);

        var ingester = CreateIngester(server);

        var (items, lastEntryId) = await ingester.GetNewEntriesAsync(7, "100", TestContext.Current.CancellationToken);

        Assert.Empty(items);
        Assert.Null(lastEntryId);
    }

    [Fact]
    public async Task GetNewEntriesAsync_QueriesConfiguredCategoryEndpoint()
    {
        using var server = WireMockServer.Start();
        GivenEntries(server, categoryId: 42, afterEntryId: "100", entries: []);

        var ingester = CreateIngester(server);

        await ingester.GetNewEntriesAsync(42, "100", TestContext.Current.CancellationToken);

        var request = Assert.Single(server.LogEntries);
        Assert.Contains("/v1/categories/42/entries", request.RequestMessage!.Path, StringComparison.Ordinal);
    }

    private static void GivenEntries(WireMockServer server, int categoryId, string afterEntryId, IReadOnlyList<(long Id, string Title, string Url, string PublishedAt)> entries)
    {
        var entriesJson = string.Join(",", entries.Select(e =>
            $$"""{ "id": {{e.Id}}, "title": {{System.Text.Json.JsonSerializer.Serialize(e.Title)}}, "url": {{System.Text.Json.JsonSerializer.Serialize(e.Url)}}, "published_at": {{System.Text.Json.JsonSerializer.Serialize(e.PublishedAt)}} }"""));

        server
            .Given(Request.Create()
                .WithPath($"/v1/categories/{categoryId}/entries")
                .UsingGet()
                .WithParam("after_entry_id", afterEntryId))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{ "total": {{entries.Count}}, "entries": [ {{entriesJson}} ] }"""));
    }

    private static MinifluxWatchIngester CreateIngester(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var minifluxOptions = Options.Create(new MinifluxOptions
        {
            BaseUrl = server.Urls[0],
            ApiToken = "test-token",
        });

        return new MinifluxWatchIngester(httpClient, minifluxOptions, NullLogger<MinifluxWatchIngester>.Instance);
    }
}
