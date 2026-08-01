using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Raindrop;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace RaindropAI.Infrastructure.Tests.Raindrop;

public class RaindropClientTests
{
    [Fact]
    public async Task GetNewRaindropsAsync_NoPriorState_ReturnsAllItemsOldestFirst()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrops/0").UsingGet().WithParam("page", "0"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "result": true,
                      "count": 2,
                      "items": [
                        { "_id": 102, "title": "B", "link": "https://b.example", "tags": [], "domain": "b.example", "type": "article", "created": "2026-01-02T00:00:00Z" },
                        { "_id": 101, "title": "A", "link": "https://a.example", "tags": [], "domain": "a.example", "type": "article", "created": "2026-01-01T00:00:00Z" }
                      ]
                    }
                    """));
        // Sans état de polling, rien n'arrête la pagination : c'est la page vide qui marque la fin.
        GivenPage(server, 1, string.Empty);

        var client = CreateClient(server, pageSize: 50);

        var items = await client.GetNewRaindropsAsync(PollingState.Initial, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(101, items[0].Id);
        Assert.Equal(102, items[1].Id);
    }

    [Fact]
    public async Task GetNewRaindropsAsync_StopsAtAlreadyKnownItem()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrops/0").UsingGet().WithParam("page", "0"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "result": true,
                      "count": 2,
                      "items": [
                        { "_id": 102, "title": "Nouveau", "link": "https://b.example", "tags": [], "domain": "b.example", "type": "article", "created": "2026-01-02T00:00:00Z" },
                        { "_id": 101, "title": "Connu", "link": "https://a.example", "tags": [], "domain": "a.example", "type": "article", "created": "2026-01-01T00:00:00Z" }
                      ]
                    }
                    """));

        var client = CreateClient(server, pageSize: 50);
        var lastState = new PollingState(101, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.UtcNow);

        var items = await client.GetNewRaindropsAsync(lastState, CancellationToken.None);

        var single = Assert.Single(items);
        Assert.Equal(102, single.Id);
    }

    [Fact]
    public async Task GetNewRaindropsAsync_PaginatesAcrossFullPages()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrops/0").UsingGet().WithParam("page", "0"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "result": true,
                      "count": 2,
                      "items": [
                        { "_id": 103, "title": "C", "link": "https://c.example", "tags": [], "domain": "c.example", "type": "article", "created": "2026-01-03T00:00:00Z" },
                        { "_id": 102, "title": "B", "link": "https://b.example", "tags": [], "domain": "b.example", "type": "article", "created": "2026-01-02T00:00:00Z" }
                      ]
                    }
                    """));
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrops/0").UsingGet().WithParam("page", "1"))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "result": true,
                      "count": 1,
                      "items": [
                        { "_id": 101, "title": "A", "link": "https://a.example", "tags": [], "domain": "a.example", "type": "article", "created": "2026-01-01T00:00:00Z" }
                      ]
                    }
                    """));

        GivenPage(server, 2, string.Empty);

        var client = CreateClient(server, pageSize: 2);

        var items = await client.GetNewRaindropsAsync(PollingState.Initial, CancellationToken.None);

        Assert.Equal(3, items.Count);
        Assert.Equal([101, 102, 103], items.Select(i => i.Id));
    }

    /// <summary>
    /// F-12 : une page plus courte que le <c>perpage</c> demandé n'est pas une fin de liste. L'ancienne
    /// condition s'arrêtait là et perdait silencieusement tout ce qui suivait.
    /// </summary>
    [Fact]
    public async Task GetNewRaindropsAsync_ShortPageFollowedByMoreItems_DoesNotStopEarly()
    {
        using var server = WireMockServer.Start();
        GivenPage(server, 0, $"{Item(105, "2026-01-05T00:00:00Z")}, {Item(104, "2026-01-04T00:00:00Z")}");
        GivenPage(server, 1, $"{Item(103, "2026-01-03T00:00:00Z")}, {Item(102, "2026-01-02T00:00:00Z")}, {Item(101, "2026-01-01T00:00:00Z")}");
        GivenPage(server, 2, string.Empty);

        // perpage=5 mais la première page n'en rend que 2 : la pagination doit continuer.
        var client = CreateClient(server, pageSize: 5);

        var items = await client.GetNewRaindropsAsync(PollingState.Initial, CancellationToken.None);

        Assert.Equal([101, 102, 103, 104, 105], items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetNewRaindropsAsync_EmptyFirstPage_ReturnsNothing()
    {
        using var server = WireMockServer.Start();
        GivenPage(server, 0, string.Empty);

        var client = CreateClient(server, pageSize: 50);

        Assert.Empty(await client.GetNewRaindropsAsync(PollingState.Initial, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRaindropAsync_WithoutCollectionId_DoesNotIncludeCollectionInBody()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrop/42").UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "result": true }"""));

        var client = CreateClient(server, pageSize: 50);

        await client.UpdateRaindropAsync(42, ["dotnet"], "note", null, CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        Assert.NotNull(logEntry.RequestMessage);
        Assert.Equal("/rest/v1/raindrop/42", logEntry.RequestMessage.Path);
        Assert.Equal("PUT", logEntry.RequestMessage.Method);
        Assert.DoesNotContain("collection", logEntry.RequestMessage.Body);
    }

    [Fact]
    public async Task UpdateRaindropAsync_WithCollectionId_IncludesCollectionReferenceInBody()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrop/42").UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "result": true }"""));

        var client = CreateClient(server, pageSize: 50);

        await client.UpdateRaindropAsync(42, ["dotnet"], "note", 7, CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        Assert.NotNull(logEntry.RequestMessage);
        Assert.Contains("\"$id\":7", logEntry.RequestMessage.Body);
    }

    [Fact]
    public async Task GetTaxonomyAsync_MergesRootAndNestedCollectionsWithTags()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/rest/v1/collections").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "result": true, "items": [ { "_id": 1, "title": ".NET" } ] }"""));
        server
            .Given(Request.Create().WithPath("/rest/v1/collections/childrens").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "result": true, "items": [ { "_id": 2, "title": "Formations" } ] }"""));
        server
            .Given(Request.Create().WithPath("/rest/v1/tags").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "result": true, "items": [ { "_id": "dotnet", "count": 10 } ] }"""));

        var client = CreateClient(server, pageSize: 50);

        var taxonomy = await client.GetTaxonomyAsync(CancellationToken.None);

        Assert.Equal(2, taxonomy.Collections.Count);
        Assert.Contains(taxonomy.Collections, c => c.Title == ".NET");
        Assert.Contains(taxonomy.Collections, c => c.Title == "Formations");
        var tag = Assert.Single(taxonomy.Tags);
        Assert.Equal("dotnet", tag.Name);
        Assert.Equal(10, tag.Count);
    }

    private static RaindropClient CreateClient(WireMockServer server, int pageSize)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new RaindropApiOptions
        {
            BaseUrl = $"{server.Urls[0]}/rest/v1",
            Token = "test-token",
            CollectionId = 0,
            PageSize = pageSize,
        });

        return new RaindropClient(httpClient, options, NullLogger<RaindropClient>.Instance);
    }

    private static void GivenPage(WireMockServer server, int page, string itemsJson) =>
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrops/0").UsingGet().WithParam("page", page.ToString()))
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($$"""{ "result": true, "items": [{{itemsJson}}] }"""));

    private static string Item(long id, string createdUtc) =>
        $$"""{ "_id": {{id}}, "title": "Article {{id}}", "link": "https://example.com/{{id}}", "tags": [], "domain": "example.com", "type": "article", "created": "{{createdUtc}}" }""";
}
