using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Classification;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Classification;

public class AnthropicThemeNarrativeGeneratorTests
{
    private static readonly IReadOnlyList<MonthlyReviewArticle> SampleArticles =
    [
        new(1, "Article 1", "https://example.com/1", "Veille .NET", [], "Résumé 1", null, Priority.Moyenne),
    ];

    [Fact]
    public async Task GenerateNarrativeAsync_ValidTextResponse_ReturnsText()
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
                      "content": [ { "type": "text", "text": "Un mois riche en actualités .NET." } ]
                    }
                    """));

        var generator = CreateGenerator(server);
        var narrative = await generator.GenerateNarrativeAsync("Veille .NET", SampleArticles, TestContext.Current.CancellationToken);

        Assert.Equal("Un mois riche en actualités .NET.", narrative);
    }

    [Fact]
    public async Task GenerateNarrativeAsync_TruncatedResponse_ReturnsFallbackText()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    { "id": "msg_1", "type": "message", "stop_reason": "max_tokens", "content": [ { "type": "text", "text": "tronqu" } ] }
                    """));

        var generator = CreateGenerator(server);
        var narrative = await generator.GenerateNarrativeAsync("Veille .NET", SampleArticles, TestContext.Current.CancellationToken);

        Assert.Equal("Revue indisponible pour ce thème.", narrative);
    }

    [Fact]
    public async Task GenerateNarrativeAsync_HttpFailure_ReturnsFallbackText()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "error": "boom" }"""));

        var generator = CreateGenerator(server);
        var narrative = await generator.GenerateNarrativeAsync("Veille .NET", SampleArticles, TestContext.Current.CancellationToken);

        Assert.Equal("Revue indisponible pour ce thème.", narrative);
    }

    [Fact]
    public async Task GenerateNarrativeAsync_MissingTextBlock_ReturnsFallbackText()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "msg_1", "type": "message", "content": [] }"""));

        var generator = CreateGenerator(server);
        var narrative = await generator.GenerateNarrativeAsync("Veille .NET", SampleArticles, TestContext.Current.CancellationToken);

        Assert.Equal("Revue indisponible pour ce thème.", narrative);
    }

    [Fact]
    public async Task GenerateNarrativeAsync_SummaryModelSet_IsUsedInsteadOfModel()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "msg_1", "type": "message", "content": [ { "type": "text", "text": "ok" } ] }"""));

        var httpClient = new HttpClient();
        var options = Options.Create(new ClassifierOptions
        {
            ApiKey = "test-key",
            Model = "claude-haiku-4-5",
            SummaryModel = "claude-summary-model",
            BaseUrl = server.Urls[0],
            AnthropicVersion = "2023-06-01",
        });
        var generator = new AnthropicThemeNarrativeGenerator(httpClient, options, NullLogger<AnthropicThemeNarrativeGenerator>.Instance);

        await generator.GenerateNarrativeAsync("Veille .NET", SampleArticles, TestContext.Current.CancellationToken);

        var body = Assert.Single(server.LogEntries).RequestMessage!.Body!;
        Assert.Contains("claude-summary-model", body);
    }

    private static AnthropicThemeNarrativeGenerator CreateGenerator(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new ClassifierOptions
        {
            ApiKey = "test-key",
            Model = "claude-haiku-4-5",
            BaseUrl = server.Urls[0],
            AnthropicVersion = "2023-06-01",
        });

        return new AnthropicThemeNarrativeGenerator(httpClient, options, NullLogger<AnthropicThemeNarrativeGenerator>.Instance);
    }
}
