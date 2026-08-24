using Microsoft.Extensions.Logging.Abstractions;
using LoreAI.Core.Enums;
using LoreAI.Infrastructure.Content;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Content;

public class HttpContentFetcherTests
{
    private static string LongParagraph() => string.Join(' ', Enumerable.Repeat("mot", 40));

    [Fact]
    public async Task FetchAsync_SuccessfulHtml_ReturnsExtractedText()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/article").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/html; charset=utf-8")
                .WithBody($"<html><body><article><p>{LongParagraph()}</p></article></body></html>"));

        var fetcher = CreateFetcher();

        var result = await fetcher.FetchAsync($"{server.Urls[0]}/article", TestContext.Current.CancellationToken);

        Assert.Equal(ContentFetchStatus.Success, result.Status);
        Assert.NotNull(result.Text);
        Assert.True(result.WordCount > 0);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(500)]
    public async Task FetchAsync_HttpFailureStatus_ReturnsHttpError(int statusCode)
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/article").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(statusCode));

        var fetcher = CreateFetcher();

        var result = await fetcher.FetchAsync($"{server.Urls[0]}/article", TestContext.Current.CancellationToken);

        Assert.Equal(ContentFetchStatus.HttpError, result.Status);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task FetchAsync_NonHtmlContentType_ReturnsUnsupportedContentType()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/document.pdf").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody("%PDF-1.4 ..."));

        var fetcher = CreateFetcher();

        var result = await fetcher.FetchAsync($"{server.Urls[0]}/document.pdf", TestContext.Current.CancellationToken);

        Assert.Equal(ContentFetchStatus.UnsupportedContentType, result.Status);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task FetchAsync_ExtractionEmpty_ReturnsExtractionEmptyStatus()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/article").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "text/html")
                .WithBody("<html><body><article><p>Chargement…</p></article></body></html>"));

        var fetcher = CreateFetcher();

        var result = await fetcher.FetchAsync($"{server.Urls[0]}/article", TestContext.Current.CancellationToken);

        Assert.Equal(ContentFetchStatus.ExtractionEmpty, result.Status);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task FetchAsync_SendsIdentifiableUserAgent()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/article").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/html").WithBody("<html></html>"));

        var fetcher = CreateFetcher();
        await fetcher.FetchAsync($"{server.Urls[0]}/article", TestContext.Current.CancellationToken);

        var request = Assert.Single(server.LogEntries).RequestMessage;
        Assert.NotNull(request);
        var userAgent = Assert.Single(request.Headers!["User-Agent"]);
        Assert.Contains("LoreAI", userAgent, StringComparison.Ordinal);
    }

    private static HttpContentFetcher CreateFetcher() => new(new HttpClient(), NullLogger<HttpContentFetcher>.Instance);
}
