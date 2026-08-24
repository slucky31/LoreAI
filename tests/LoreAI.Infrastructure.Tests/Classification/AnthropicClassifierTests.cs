using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Classification;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Classification;

public class AnthropicClassifierTests
{
    private static readonly RaindropTaxonomy SampleTaxonomy = new(
        [new RaindropCollection(1, "Claude")],
        [new RaindropTag("claude", 5)]);

    [Fact]
    public async Task ClassifyAsync_ValidToolUseResponse_MapsToClassificationResult()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "id": "msg_1",
                      "type": "message",
                      "content": [
                        {
                          "type": "tool_use",
                          "id": "toolu_1",
                          "name": "classify",
                          "input": { "suggestedCollection": "Claude", "tags": ["claude"], "action": "ATester", "priority": "Haute", "reason": "Nouvel outil Claude à essayer." }
                        }
                      ]
                    }
                    """));

        var classifier = CreateClassifier(server);
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, null, CancellationToken.None);

        Assert.Equal("Claude", result.SuggestedCollection);
        Assert.Equal(["claude"], result.Tags);
        Assert.Equal(RecommendedAction.ATester, result.Action);
        Assert.Equal(Priority.Haute, result.Priority);
        Assert.Equal("Nouvel outil Claude à essayer.", result.Reason);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public async Task ClassifyAsync_TruncatedResponse_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    {
                      "id": "msg_1",
                      "type": "message",
                      "stop_reason": "max_tokens",
                      "content": [
                        {
                          "type": "tool_use",
                          "id": "toolu_1",
                          "name": "classify",
                          "input": { "suggestedCollection": "Claude", "tags": ["claude"] }
                        }
                      ]
                    }
                    """));

        var classifier = CreateClassifier(server);
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, null, CancellationToken.None);

        // Le bloc tool_use est présent mais incomplet : on ne doit surtout pas conclure dessus.
        Assert.True(result.IsFallback);
        Assert.Contains("max_tokens", result.Reason);
    }

    [Fact]
    public async Task ClassifyAsync_HttpFailure_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "error": "boom" }"""));

        var classifier = CreateClassifier(server);
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, null, CancellationToken.None);

        Assert.Null(result.SuggestedCollection);
        Assert.Empty(result.Tags);
        Assert.Equal(RecommendedAction.Reference, result.Action);
        Assert.Equal(Priority.Basse, result.Priority);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public async Task ClassifyAsync_MissingToolUseBlock_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "msg_1", "type": "message", "content": [] }"""));

        var classifier = CreateClassifier(server);
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, null, CancellationToken.None);

        Assert.Null(result.SuggestedCollection);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public async Task ClassifyAsync_BodyThatIsNotJson_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("<html>502 Bad Gateway</html>"));

        var classifier = CreateClassifier(server);
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, null, TestContext.Current.CancellationToken);

        // Un intermédiaire qui renvoie du HTML en 200 reste une panne de transport : repli, pas de crash.
        Assert.True(result.IsFallback);
    }

    [Fact]
    public async Task ClassifyAsync_ResponseWithoutContentArray_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "msg_1", "type": "message" }"""));

        var classifier = CreateClassifier(server);
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, null, TestContext.Current.CancellationToken);

        Assert.True(result.IsFallback);
        Assert.Contains("content", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Marqueur de mesure pour la décision #34 (cache de prompt) : sans effet tant que le préfixe est sous
    /// le seuil minimal, mais nécessaire pour pouvoir un jour le mesurer.
    /// </summary>
    [Fact]
    public async Task ClassifyAsync_RequestBody_IncludesCacheControlOnTool()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    { "id": "msg_1", "type": "message", "content": [
                      { "type": "tool_use", "id": "toolu_1", "name": "classify",
                        "input": { "suggestedCollection": null, "tags": [], "action": "ATester", "priority": "Haute", "reason": "x", "summary": "y" } }
                    ] }
                    """));

        var classifier = CreateClassifier(server);
        await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, null, TestContext.Current.CancellationToken);

        var body = Assert.Single(server.LogEntries).RequestMessage!.Body!;
        Assert.Contains("\"cache_control\":{\"type\":\"ephemeral\"}", body);
    }

    [Fact]
    public async Task ClassifyAsync_ContentTextProvided_IsSentInsteadOfExcerpt()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    { "id": "msg_1", "type": "message", "content": [
                      { "type": "tool_use", "id": "toolu_1", "name": "classify",
                        "input": { "suggestedCollection": null, "tags": [], "action": "ATester", "priority": "Haute", "reason": "x", "summary": "y" } }
                    ] }
                    """));

        var classifier = CreateClassifier(server);
        await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, "Contenu recupere par IContentFetcher", TestContext.Current.CancellationToken);

        var body = Assert.Single(server.LogEntries).RequestMessage!.Body!;
        Assert.Contains("Contenu recupere par IContentFetcher", body);
    }

    private static AnthropicClassifier CreateClassifier(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new ClassifierOptions
        {
            ApiKey = "test-key",
            Model = "claude-haiku-4-5",
            BaseUrl = server.Urls[0],
            AnthropicVersion = "2023-06-01",
        });

        return new AnthropicClassifier(httpClient, options, NullLogger<AnthropicClassifier>.Instance);
    }

    private static Item CreateItem() => new(
        SourceType.Raindrop,
        "1",
        "https://example.com/claude-tool",
        "Un nouvel outil Claude",
        "Extrait",
        null,
        ["claude"],
        DateTimeOffset.UtcNow);
}
