using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Classification;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace RaindropAI.Infrastructure.Tests.Classification;

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
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, CancellationToken.None);

        Assert.Equal("Claude", result.SuggestedCollection);
        Assert.Equal(["claude"], result.Tags);
        Assert.Equal(RecommendedAction.ATester, result.Action);
        Assert.Equal(Priority.Haute, result.Priority);
        Assert.Equal("Nouvel outil Claude à essayer.", result.Reason);
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
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, CancellationToken.None);

        Assert.Null(result.SuggestedCollection);
        Assert.Empty(result.Tags);
        Assert.Equal(RecommendedAction.Reference, result.Action);
        Assert.Equal(Priority.Basse, result.Priority);
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
        var result = await classifier.ClassifyAsync(CreateItem(), SampleTaxonomy, CancellationToken.None);

        Assert.Null(result.SuggestedCollection);
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

    private static RaindropItem CreateItem() => new(
        1,
        "Un nouvel outil Claude",
        "https://example.com/claude-tool",
        "Extrait",
        null,
        ["claude"],
        null,
        "example.com",
        "article",
        DateTimeOffset.UtcNow,
        null);
}
