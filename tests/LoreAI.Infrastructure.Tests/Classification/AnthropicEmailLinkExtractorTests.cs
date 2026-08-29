using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using LoreAI.Core.Interfaces;
using LoreAI.Infrastructure.Classification;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Classification;

public class AnthropicEmailLinkExtractorTests
{
    private static readonly string[] CandidateUrls = ["https://blog.example.com/article"];

    [Fact]
    public async Task ExtractAsync_ValidToolUseResponse_ReturnsExtractedLinks()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    { "id": "msg_1", "type": "message", "content": [
                      { "type": "tool_use", "id": "toolu_1", "name": "extract_links",
                        "input": { "links": [ { "index": 0, "title": "Un vrai article" } ] } }
                    ] }
                    """));

        var (extractor, _) = CreateExtractor(server);
        var result = await extractor.ExtractAsync("Sujet", "Corps", CandidateUrls, TestContext.Current.CancellationToken);

        var single = Assert.Single(result);
        Assert.Equal("https://blog.example.com/article", single.Url);
        Assert.Equal("Un vrai article", single.Title);
    }

    [Fact]
    public async Task ExtractAsync_NoCandidateUrls_ShortCircuitsWithoutCallingApi()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{ "content": [] }"""));

        var (extractor, extractionLogRepository) = CreateExtractor(server);
        var result = await extractor.ExtractAsync("Sujet", "Corps", [], TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Empty(server.LogEntries);
        await extractionLogRepository.DidNotReceive().RecordAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_HttpFailure_ReturnsEmptyList()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("""{ "error": "boom" }"""));

        var (extractor, _) = CreateExtractor(server);
        var result = await extractor.ExtractAsync("Sujet", "Corps", CandidateUrls, TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractAsync_MissingToolUseBlock_ReturnsEmptyList()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{ "id": "msg_1", "type": "message", "content": [] }"""));

        var (extractor, _) = CreateExtractor(server);
        var result = await extractor.ExtractAsync("Sujet", "Corps", CandidateUrls, TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    /// <summary>S6 (lot 8) : chaque appel réel (succès ou repli) est journalisé, contrairement au court-circuit sans URL candidate.</summary>
    [Fact]
    public async Task ExtractAsync_SuccessfulCall_RecordsRawResponseForUsageTracking()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    { "id": "msg_1", "type": "message", "content": [
                        { "type": "tool_use", "id": "toolu_1", "name": "extract_links", "input": { "links": [] } } ] }
                    """));

        var (extractor, extractionLogRepository) = CreateExtractor(server);
        await extractor.ExtractAsync("Sujet", "Corps", CandidateUrls, TestContext.Current.CancellationToken);

        await extractionLogRepository.Received(1).RecordAsync(
            Arg.Is<string>(r => r!.Contains("extract_links", StringComparison.Ordinal)),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Best-effort (S6) : un échec du journal d'usage ne doit jamais faire perdre une extraction par ailleurs réussie.</summary>
    [Fact]
    public async Task ExtractAsync_LogRepositoryThrows_StillReturnsExtractedLinks()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/v1/messages").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""
                    { "id": "msg_1", "type": "message", "content": [
                        { "type": "tool_use", "id": "toolu_1", "name": "extract_links",
                          "input": { "links": [ { "index": 0, "title": "Titre" } ] } } ] }
                    """));

        var (extractor, extractionLogRepository) = CreateExtractor(server);
        extractionLogRepository
            .RecordAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var result = await extractor.ExtractAsync("Sujet", "Corps", CandidateUrls, TestContext.Current.CancellationToken);

        Assert.Single(result);
    }

    private static (AnthropicEmailLinkExtractor Extractor, IEmailExtractionLogRepository ExtractionLogRepository) CreateExtractor(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new ClassifierOptions
        {
            ApiKey = "test-key",
            Model = "claude-haiku-4-5",
            BaseUrl = server.Urls[0],
            AnthropicVersion = "2023-06-01",
        });
        var extractionLogRepository = Substitute.For<IEmailExtractionLogRepository>();

        return (new AnthropicEmailLinkExtractor(httpClient, options, extractionLogRepository, NullLogger<AnthropicEmailLinkExtractor>.Instance), extractionLogRepository);
    }
}
