using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Notifications;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Notifications;

public class DiscordWatchDigestNotifierTests
{
    [Fact]
    public async Task NotifyAsync_PostsMessageContainingPerTopicCounts()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        var notifier = CreateNotifier(server);
        var summary = new WatchRunSummary([
            new WatchTopicRunResult("dotnet-perf", 5, 2),
            new WatchTopicRunResult("ia-outils", 3, 0),
        ]);

        await notifier.NotifyAsync(summary, CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        var content = JsonDocument.Parse(logEntry.RequestMessage!.Body!).RootElement.GetProperty("content").GetString();
        Assert.Contains("dotnet-perf", content, StringComparison.Ordinal);
        Assert.Contains("2/5", content, StringComparison.Ordinal);
        Assert.Contains("ia-outils", content, StringComparison.Ordinal);
        Assert.Contains("0/3", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotifyAsync_HttpFailure_DoesNotThrow()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var notifier = CreateNotifier(server);
        var summary = new WatchRunSummary([new WatchTopicRunResult("sujet", 1, 0)]);

        var exception = await Record.ExceptionAsync(() => notifier.NotifyAsync(summary, CancellationToken.None));

        Assert.Null(exception);
    }

    private static DiscordWatchDigestNotifier CreateNotifier(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new DiscordOptions { WebhookUrl = $"{server.Urls[0]}/webhook" });
        return new DiscordWatchDigestNotifier(httpClient, options, NullLogger<DiscordWatchDigestNotifier>.Instance);
    }
}
