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

        var client = CreateClient(server, pageSize: 2);

        var items = await client.GetNewRaindropsAsync(PollingState.Initial, CancellationToken.None);

        Assert.Equal(3, items.Count);
        Assert.Equal([101, 102, 103], items.Select(i => i.Id));
    }

    [Fact]
    public async Task UpdateRaindropAsync_SendsPutToExpectedEndpoint()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/rest/v1/raindrop/42").UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "result": true }"""));

        var client = CreateClient(server, pageSize: 50);

        await client.UpdateRaindropAsync(42, ["dotnet"], "note", CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        Assert.NotNull(logEntry.RequestMessage);
        Assert.Equal("/rest/v1/raindrop/42", logEntry.RequestMessage.Path);
        Assert.Equal("PUT", logEntry.RequestMessage.Method);
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

        return new RaindropClient(httpClient, options);
    }
}
