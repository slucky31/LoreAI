using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Feed;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Feed;

/// <summary>
/// Couvre ce qui distingue cet ingesteur de <c>MinifluxIngester</c> (lot 7) : la portée sur une catégorie
/// Miniflux dédiée et le curseur <see cref="SourceType.Watch"/>, distinct de <see cref="SourceType.Feed"/>.
/// </summary>
public class MinifluxWatchIngesterTests
{
    [Fact]
    public async Task GetNewItemsAsync_NoCursor_ReturnsEmptyWithoutCallingApi()
    {
        using var server = WireMockServer.Start();
        var (ingester, _) = CreateIngester(server);

        var items = await ingester.GetNewItemsAsync(PollingState.Initial(SourceType.Watch), TestContext.Current.CancellationToken);

        Assert.Empty(items);
        Assert.Empty(server.LogEntries);
    }

    [Fact]
    public async Task GetNewItemsAsync_NewEntries_ReturnsWatchItemsAndAdvancesCursorToLastEntryId()
    {
        using var server = WireMockServer.Start();
        GivenEntries(server, categoryId: 7, afterEntryId: "100", entries:
        [
            (101, "Un article de veille", "https://blog.example.com/1", "2026-08-01T10:00:00Z"),
            (102, "Un autre article de veille", "https://blog.example.com/2", "2026-08-02T10:00:00Z"),
        ]);

        var (ingester, pollingStateRepository) = CreateIngester(server, categoryId: 7);

        var items = await ingester.GetNewItemsAsync(
            new PollingState(SourceType.Watch, "100", null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, items.Count);
        Assert.Equal(SourceType.Watch, items[0].SourceType);
        Assert.Equal("101", items[0].SourceId);
        Assert.Equal("https://blog.example.com/1", items[0].Url);
        Assert.Equal("Un article de veille", items[0].Title);
        Assert.Equal("102", items[1].SourceId);

        await pollingStateRepository.Received(1).UpdateAsync(
            Arg.Is<PollingState>(s => s!.SourceType == SourceType.Watch && s.LastSourceItemId == "102"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNewItemsAsync_NoNewEntries_ReturnsEmptyAndDoesNotAdvanceCursor()
    {
        using var server = WireMockServer.Start();
        GivenEntries(server, categoryId: 7, afterEntryId: "100", entries: []);

        var (ingester, pollingStateRepository) = CreateIngester(server, categoryId: 7);

        var items = await ingester.GetNewItemsAsync(
            new PollingState(SourceType.Watch, "100", null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Assert.Empty(items);
        await pollingStateRepository.DidNotReceive().UpdateAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNewItemsAsync_QueriesConfiguredCategoryEndpoint()
    {
        using var server = WireMockServer.Start();
        GivenEntries(server, categoryId: 42, afterEntryId: "100", entries: []);

        var (ingester, _) = CreateIngester(server, categoryId: 42);

        await ingester.GetNewItemsAsync(
            new PollingState(SourceType.Watch, "100", null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

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

    private static (MinifluxWatchIngester Ingester, IPollingStateRepository PollingStateRepository) CreateIngester(WireMockServer server, int categoryId = 1)
    {
        var httpClient = new HttpClient();
        var minifluxOptions = Options.Create(new MinifluxOptions
        {
            BaseUrl = server.Urls[0],
            ApiToken = "test-token",
        });
        var watchOptions = Options.Create(new WatchOptions { MinifluxCategoryId = categoryId });
        var pollingStateRepository = Substitute.For<IPollingStateRepository>();

        return (new MinifluxWatchIngester(httpClient, minifluxOptions, watchOptions, pollingStateRepository, NullLogger<MinifluxWatchIngester>.Instance), pollingStateRepository);
    }
}
