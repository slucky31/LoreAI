using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Classification;
using RichardSzalay.MockHttp;

namespace RaindropAI.Infrastructure.Tests.Classification;

public class AnthropicClassifierTests
{
    private const string BaseUrl = "https://api.anthropic.com";

    [Fact]
    public async Task ClassifyAsync_ValidToolUseResponse_MapsToClassificationResult()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, $"{BaseUrl}/v1/messages")
            .Respond("application/json", """
                {
                  "id": "msg_1",
                  "type": "message",
                  "content": [
                    {
                      "type": "tool_use",
                      "id": "toolu_1",
                      "name": "classify",
                      "input": { "category": "ClaudeIA", "action": "ATester", "priority": "Haute", "reason": "Nouvel outil Claude à essayer." }
                    }
                  ]
                }
                """);

        var classifier = CreateClassifier(mockHttp);
        var result = await classifier.ClassifyAsync(CreateItem(), CancellationToken.None);

        Assert.Equal(Category.ClaudeIA, result.Category);
        Assert.Equal(RecommendedAction.ATester, result.Action);
        Assert.Equal(Priority.Haute, result.Priority);
        Assert.Equal("Nouvel outil Claude à essayer.", result.Reason);
    }

    [Fact]
    public async Task ClassifyAsync_HttpFailure_ReturnsFallback()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, $"{BaseUrl}/v1/messages")
            .Respond(System.Net.HttpStatusCode.InternalServerError, "application/json", """{ "error": "boom" }""");

        var classifier = CreateClassifier(mockHttp);
        var result = await classifier.ClassifyAsync(CreateItem(), CancellationToken.None);

        Assert.Equal(Category.Autre, result.Category);
        Assert.Equal(RecommendedAction.Reference, result.Action);
        Assert.Equal(Priority.Basse, result.Priority);
    }

    [Fact]
    public async Task ClassifyAsync_MissingToolUseBlock_ReturnsFallback()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, $"{BaseUrl}/v1/messages")
            .Respond("application/json", """{ "id": "msg_1", "type": "message", "content": [] }""");

        var classifier = CreateClassifier(mockHttp);
        var result = await classifier.ClassifyAsync(CreateItem(), CancellationToken.None);

        Assert.Equal(Category.Autre, result.Category);
    }

    private static AnthropicClassifier CreateClassifier(MockHttpMessageHandler mockHttp)
    {
        var httpClient = mockHttp.ToHttpClient();
        var options = Options.Create(new ClassifierOptions
        {
            ApiKey = "test-key",
            Model = "claude-haiku-4-5",
            BaseUrl = BaseUrl,
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
