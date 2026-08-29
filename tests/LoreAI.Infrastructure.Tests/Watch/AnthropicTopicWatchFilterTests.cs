using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Classification;
using LoreAI.Infrastructure.Watch;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Watch;

public class AnthropicTopicWatchFilterTests
{
    private static readonly Item Candidate = new(
        SourceType.Watch, "1", "https://blog.example.com/article", "Un article", null, null, [], DateTimeOffset.UtcNow);

    [Fact]
    public async Task EvaluateAsync_ValidToolUseResponse_ReturnsEvaluation()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    { "id": "msg_1", "type": "message", "content": [
                      { "type": "tool_use", "id": "toolu_1", "name": "evaluate_watch_candidate",
                        "input": { "isRelevant": true, "matchedTopic": "dotnet-perf", "isNew": true, "reason": "Nouveau" } } ] }
                    """));

        var filter = CreateFilter(server);
        var result = await filter.EvaluateAsync(Candidate, [], [], TestContext.Current.CancellationToken);

        Assert.True(result.IsRelevant);
        Assert.True(result.IsNew);
        Assert.Equal("dotnet-perf", result.MatchedTopic);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public async Task EvaluateAsync_HttpFailure_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("""{ "error": "boom" }"""));

        var filter = CreateFilter(server);
        var result = await filter.EvaluateAsync(Candidate, [], [], TestContext.Current.CancellationToken);

        Assert.True(result.IsFallback);
        Assert.False(result.IsRelevant);
        Assert.False(result.IsNew);
    }

    [Fact]
    public async Task EvaluateAsync_MissingToolUseBlock_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "msg_1", "type": "message", "content": [] }"""));

        var filter = CreateFilter(server);
        var result = await filter.EvaluateAsync(Candidate, [], [], TestContext.Current.CancellationToken);

        Assert.True(result.IsFallback);
    }

    [Fact]
    public async Task EvaluateAsync_TruncatedResponse_ReturnsFallback()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "msg_1", "type": "message", "stop_reason": "max_tokens", "content": [] }"""));

        var filter = CreateFilter(server);
        var result = await filter.EvaluateAsync(Candidate, [], [], TestContext.Current.CancellationToken);

        Assert.True(result.IsFallback);
        Assert.Contains("max_tokens", result.Reason, StringComparison.Ordinal);
    }

    private static AnthropicTopicWatchFilter CreateFilter(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new ClassifierOptions
        {
            ApiKey = "test-key",
            Model = "claude-haiku-4-5",
            BaseUrl = server.Urls[0],
            AnthropicVersion = "2023-06-01",
        });

        return new AnthropicTopicWatchFilter(httpClient, options, NullLogger<AnthropicTopicWatchFilter>.Instance);
    }
}
