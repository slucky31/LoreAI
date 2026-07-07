using Microsoft.Extensions.Options;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Raindrop;
using RichardSzalay.MockHttp;

namespace RaindropAI.Infrastructure.Tests.Raindrop;

public class RaindropClientTests
{
    private const string BaseUrl = "https://api.raindrop.io/rest/v1";

    [Fact]
    public async Task GetNewRaindropsAsync_NoPriorState_ReturnsAllItemsOldestFirst()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{BaseUrl}/raindrops/0")
            .WithQueryString("page", "0")
            .Respond("application/json", """
                {
                  "result": true,
                  "count": 2,
                  "items": [
                    { "_id": 102, "title": "B", "link": "https://b.example", "tags": [], "domain": "b.example", "type": "article", "created": "2026-01-02T00:00:00Z" },
                    { "_id": 101, "title": "A", "link": "https://a.example", "tags": [], "domain": "a.example", "type": "article", "created": "2026-01-01T00:00:00Z" }
                  ]
                }
                """);

        var client = CreateClient(mockHttp, pageSize: 50);

        var items = await client.GetNewRaindropsAsync(PollingState.Initial, CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(101, items[0].Id);
        Assert.Equal(102, items[1].Id);
    }

    [Fact]
    public async Task GetNewRaindropsAsync_StopsAtAlreadyKnownItem()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{BaseUrl}/raindrops/0")
            .WithQueryString("page", "0")
            .Respond("application/json", """
                {
                  "result": true,
                  "count": 2,
                  "items": [
                    { "_id": 102, "title": "Nouveau", "link": "https://b.example", "tags": [], "domain": "b.example", "type": "article", "created": "2026-01-02T00:00:00Z" },
                    { "_id": 101, "title": "Connu", "link": "https://a.example", "tags": [], "domain": "a.example", "type": "article", "created": "2026-01-01T00:00:00Z" }
                  ]
                }
                """);

        var client = CreateClient(mockHttp, pageSize: 50);
        var lastState = new PollingState(101, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.UtcNow);

        var items = await client.GetNewRaindropsAsync(lastState, CancellationToken.None);

        var single = Assert.Single(items);
        Assert.Equal(102, single.Id);
    }

    [Fact]
    public async Task GetNewRaindropsAsync_PaginatesAcrossFullPages()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When($"{BaseUrl}/raindrops/0")
            .WithQueryString("page", "0")
            .Respond("application/json", """
                {
                  "result": true,
                  "count": 2,
                  "items": [
                    { "_id": 103, "title": "C", "link": "https://c.example", "tags": [], "domain": "c.example", "type": "article", "created": "2026-01-03T00:00:00Z" },
                    { "_id": 102, "title": "B", "link": "https://b.example", "tags": [], "domain": "b.example", "type": "article", "created": "2026-01-02T00:00:00Z" }
                  ]
                }
                """);
        mockHttp.When($"{BaseUrl}/raindrops/0")
            .WithQueryString("page", "1")
            .Respond("application/json", """
                {
                  "result": true,
                  "count": 1,
                  "items": [
                    { "_id": 101, "title": "A", "link": "https://a.example", "tags": [], "domain": "a.example", "type": "article", "created": "2026-01-01T00:00:00Z" }
                  ]
                }
                """);

        var client = CreateClient(mockHttp, pageSize: 2);

        var items = await client.GetNewRaindropsAsync(PollingState.Initial, CancellationToken.None);

        Assert.Equal(3, items.Count);
        Assert.Equal([101, 102, 103], items.Select(i => i.Id));
    }

    [Fact]
    public async Task UpdateRaindropAsync_SendsPutToExpectedEndpoint()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.Expect(HttpMethod.Put, $"{BaseUrl}/raindrop/42")
            .Respond("application/json", """{ "result": true }""");

        var client = CreateClient(mockHttp, pageSize: 50);

        await client.UpdateRaindropAsync(42, ["dotnet"], "note", CancellationToken.None);

        mockHttp.VerifyNoOutstandingExpectation();
    }

    private static RaindropClient CreateClient(MockHttpMessageHandler mockHttp, int pageSize)
    {
        var httpClient = mockHttp.ToHttpClient();
        var options = Options.Create(new RaindropApiOptions
        {
            BaseUrl = BaseUrl,
            Token = "test-token",
            CollectionId = 0,
            PageSize = pageSize,
        });

        return new RaindropClient(httpClient, options);
    }
}
