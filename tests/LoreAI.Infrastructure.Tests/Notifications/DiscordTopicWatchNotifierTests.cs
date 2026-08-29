using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Notifications;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Notifications;

public class DiscordTopicWatchNotifierTests
{
    [Fact]
    public async Task NotifyAsync_PostsMessageContainingTitleTopicAndLink()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        var notifier = CreateNotifier(server);
        var candidate = new Item(SourceType.Watch, "1", "https://example.com/article", "Un vrai scoop", null, null, [], DateTimeOffset.UtcNow);
        var evaluation = new WatchEvaluation(true, true, "dotnet-perf", "Apporte un nouveau benchmark", "model", "raw");

        await notifier.NotifyAsync(candidate, evaluation, CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        var content = JsonDocument.Parse(logEntry.RequestMessage!.Body!).RootElement.GetProperty("content").GetString();
        Assert.Contains("Un vrai scoop", content, StringComparison.Ordinal);
        Assert.Contains("dotnet-perf", content, StringComparison.Ordinal);
        Assert.Contains("https://example.com/article", content, StringComparison.Ordinal);
        Assert.Contains("Apporte un nouveau benchmark", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotifyAsync_HttpFailure_DoesNotThrow()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var notifier = CreateNotifier(server);
        var candidate = new Item(SourceType.Watch, "1", "https://example.com", "Titre", null, null, [], DateTimeOffset.UtcNow);
        var evaluation = new WatchEvaluation(true, true, "sujet", "raison", "model", "raw");

        var exception = await Record.ExceptionAsync(() => notifier.NotifyAsync(candidate, evaluation, CancellationToken.None));

        Assert.Null(exception);
    }

    private static DiscordTopicWatchNotifier CreateNotifier(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new DiscordOptions { WebhookUrl = $"{server.Urls[0]}/webhook" });
        return new DiscordTopicWatchNotifier(httpClient, options, NullLogger<DiscordTopicWatchNotifier>.Instance);
    }
}
