using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Gmail;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Gmail;

public class GmailIngesterTests
{
    private const string PlainTextMessageBody = "aW52aXRhdGlvbiA6IGh0dHBzOi8vYmxvZy5leGFtcGxlLmNvbS9hcnRpY2xl";
    // Décodage : "invitation : https://blog.example.com/article"

    [Fact]
    public async Task GetNewItemsAsync_NoCursor_ReturnsEmptyWithoutCallingApi()
    {
        using var server = WireMockServer.Start();
        var (ingester, _, _) = CreateIngester(server);

        var items = await ingester.GetNewItemsAsync(PollingState.Initial(SourceType.Newsletter), TestContext.Current.CancellationToken);

        Assert.Empty(items);
        Assert.Empty(server.LogEntries);
    }

    [Fact]
    public async Task GetNewItemsAsync_NewMessage_ReturnsExtractedItemsAndAdvancesCursor()
    {
        using var server = WireMockServer.Start();
        GivenTokenEndpoint(server);
        GivenLabels(server);
        GivenHistory(server, startHistoryId: "100", messageIds: ["msg1"], newHistoryId: "200");
        GivenMessage(server, "msg1", PlainTextMessageBody);

        var (ingester, extractor, pollingStateRepository) = CreateIngester(server);
        extractor
            .ExtractAsync("Newsletter", Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ExtractedLink("https://blog.example.com/article", "Un vrai article")]);

        var items = await ingester.GetNewItemsAsync(
            new PollingState(SourceType.Newsletter, "100", null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        var single = Assert.Single(items);
        Assert.Equal(SourceType.Newsletter, single.SourceType);
        Assert.Equal("msg1:0", single.SourceId);
        Assert.Equal("https://blog.example.com/article", single.Url);
        Assert.Equal("Un vrai article", single.Title);

        await pollingStateRepository.Received(1).UpdateAsync(
            Arg.Is<PollingState>(s => s!.SourceType == SourceType.Newsletter && s.LastSourceItemId == "200"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNewItemsAsync_UnknownLabel_ReturnsEmptyAndDoesNotAdvanceCursor()
    {
        using var server = WireMockServer.Start();
        GivenTokenEndpoint(server);
        server
            .Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{ "labels": [ { "id": "Label_2", "name": "AutreLabel" } ] }"""));

        var (ingester, _, pollingStateRepository) = CreateIngester(server);

        var items = await ingester.GetNewItemsAsync(
            new PollingState(SourceType.Newsletter, "100", null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Assert.Empty(items);
        await pollingStateRepository.DidNotReceive().UpdateAsync(Arg.Any<PollingState>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Court-circuit du filtre heuristique (cf. EmailLinkNoiseFilter) : pas d'appel LLM si seul du bruit survit.</summary>
    [Fact]
    public async Task GetNewItemsAsync_OnlyNoisyUrlsInMessage_SkipsExtractorButStillAdvancesCursor()
    {
        using var server = WireMockServer.Start();
        // "se desinscrire : https://newsletter.example.com/unsubscribe?id=1"
        const string noisyOnlyBody = "c2UgZGVzaW5zY3JpcmUgOiBodHRwczovL25ld3NsZXR0ZXIuZXhhbXBsZS5jb20vdW5zdWJzY3JpYmU_aWQ9MQ";
        GivenTokenEndpoint(server);
        GivenLabels(server);
        GivenHistory(server, startHistoryId: "100", messageIds: ["msg1"], newHistoryId: "200");
        GivenMessage(server, "msg1", noisyOnlyBody);

        var (ingester, extractor, pollingStateRepository) = CreateIngester(server);

        var items = await ingester.GetNewItemsAsync(
            new PollingState(SourceType.Newsletter, "100", null, DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Assert.Empty(items);
        await extractor.DidNotReceive().ExtractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await pollingStateRepository.Received(1).UpdateAsync(
            Arg.Is<PollingState>(s => s!.LastSourceItemId == "200"), Arg.Any<CancellationToken>());
    }

    private static void GivenTokenEndpoint(WireMockServer server) =>
        server
            .Given(Request.Create().WithPath("/token").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{ "access_token": "fake-access-token", "expires_in": 3599, "token_type": "Bearer" }"""));

    private static void GivenLabels(WireMockServer server) =>
        server
            .Given(Request.Create().WithPath("/gmail/v1/users/me/labels").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody("""{ "labels": [ { "id": "Label_1", "name": "Newsletters" } ] }"""));

    private static void GivenHistory(WireMockServer server, string startHistoryId, IReadOnlyList<string> messageIds, string newHistoryId)
    {
        var messagesAddedJson = string.Join(",", messageIds.Select(id => $$"""{ "message": { "id": {{System.Text.Json.JsonSerializer.Serialize(id)}} } }"""));
        server
            .Given(Request.Create()
                .WithPath("/gmail/v1/users/me/history")
                .UsingGet()
                .WithParam("startHistoryId", startHistoryId)
                .WithParam("labelId", "Label_1"))
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""{ "history": [ { "messagesAdded": [ {{messagesAddedJson}} ] } ], "historyId": {{System.Text.Json.JsonSerializer.Serialize(newHistoryId)}} }"""));
    }

    private static void GivenMessage(WireMockServer server, string messageId, string base64UrlBody) =>
        server
            .Given(Request.Create().WithPath($"/gmail/v1/users/me/messages/{messageId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json")
                .WithBody($$"""
                    {
                        "id": {{System.Text.Json.JsonSerializer.Serialize(messageId)}},
                        "internalDate": "1700000000000",
                        "payload": {
                            "headers": [ { "name": "Subject", "value": "Newsletter" } ],
                            "mimeType": "text/plain",
                            "body": { "data": {{System.Text.Json.JsonSerializer.Serialize(base64UrlBody)}} }
                        }
                    }
                    """));

    private static (GmailIngester Ingester, IEmailLinkExtractor Extractor, IPollingStateRepository PollingStateRepository) CreateIngester(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new GoogleOAuthOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RefreshToken = "refresh-token",
            Label = "Newsletters",
            TokenUrl = server.Urls[0] + "/token",
            ApiBaseUrl = server.Urls[0] + "/gmail/v1/",
        });
        var extractor = Substitute.For<IEmailLinkExtractor>();
        var pollingStateRepository = Substitute.For<IPollingStateRepository>();

        return (new GmailIngester(httpClient, options, extractor, pollingStateRepository, NullLogger<GmailIngester>.Instance), extractor, pollingStateRepository);
    }
}
