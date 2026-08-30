using Microsoft.Extensions.Options;
using NSubstitute;
using LoreAI.Core.Interfaces;
using LoreAI.Infrastructure.Feed;
using LoreAI.Infrastructure.Watch;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Watch;

public class WatchTopicProvisionerTests
{
    [Fact]
    public async Task ProvisionAsync_CreatesRaindropCollectionAndMinifluxCategory()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/categories").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": 7, "title": "dotnet-perf" }"""));

        var raindropClient = Substitute.For<IRaindropClient>();
        raindropClient.CreateCollectionAsync("dotnet-perf", Arg.Any<CancellationToken>()).Returns(42L);

        var provisioner = CreateProvisioner(server, raindropClient);

        var topic = await provisioner.ProvisionAsync("dotnet-perf", "Optimisations .NET", TestContext.Current.CancellationToken);

        Assert.Equal("dotnet-perf", topic.Name);
        Assert.Equal("Optimisations .NET", topic.Description);
        Assert.Equal(42, topic.RaindropCollectionId);
        Assert.Equal(7, topic.MinifluxCategoryId);
        Assert.Equal(0, topic.Id);
        Assert.Null(topic.LastMinifluxEntryId);

        await raindropClient.Received(1).CreateCollectionAsync("dotnet-perf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_SendsApiTokenHeaderToMiniflux()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/categories").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": 1, "title": "sujet" }"""));

        var raindropClient = Substitute.For<IRaindropClient>();
        raindropClient.CreateCollectionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1L);

        var provisioner = CreateProvisioner(server, raindropClient, apiToken: "secret-token");

        await provisioner.ProvisionAsync("sujet", "description", TestContext.Current.CancellationToken);

        var request = Assert.Single(server.LogEntries);
        Assert.Equal("secret-token", request.RequestMessage!.Headers!["X-Auth-Token"].Single());
    }

    private static WatchTopicProvisioner CreateProvisioner(WireMockServer server, IRaindropClient raindropClient, string apiToken = "test-token")
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new MinifluxOptions { BaseUrl = server.Urls[0], ApiToken = apiToken });

        return new WatchTopicProvisioner(raindropClient, httpClient, options);
    }
}
