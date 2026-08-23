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

public class DiscordCycleReportNotifierTests
{
    [Fact]
    public async Task NotifyCycleCompletedAsync_PostsMessageContainingCounts()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        var notifier = CreateNotifier(server);
        var run = new CycleRun(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            CycleOutcome.Ok,
            ItemsSeen: 5,
            ItemsProcessed: 5,
            Moved: 3,
            TagsApplied: 7,
            Notified: 1,
            FailureReason: null);

        await notifier.NotifyCycleCompletedAsync(run, CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        Assert.NotNull(logEntry.RequestMessage);
        var content = JsonDocument.Parse(logEntry.RequestMessage.Body!).RootElement.GetProperty("content").GetString();
        Assert.Contains("5/5", content);
        Assert.Contains("Déplacés : 3", content);
        // 5 traités - 3 déplacés = 2 restés dans « Non trié ».
        Assert.Contains("Restés dans « Non trié » : 2", content);
        Assert.Contains("Tags ajoutés : 7", content);
    }

    [Fact]
    public async Task NotifyCycleCompletedAsync_InterruptedRun_IncludesFailureReason()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        var notifier = CreateNotifier(server);
        var run = new CycleRun(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            CycleOutcome.Interrupted,
            ItemsSeen: 3,
            ItemsProcessed: 1,
            Moved: 0,
            TagsApplied: 2,
            Notified: 0,
            FailureReason: "Classification en repli pour l'item 42.");

        await notifier.NotifyCycleCompletedAsync(run, CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        Assert.NotNull(logEntry.RequestMessage);
        var content = JsonDocument.Parse(logEntry.RequestMessage.Body!).RootElement.GetProperty("content").GetString();
        Assert.Contains("interrompu", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Classification en repli pour l'item 42.", content);
    }

    [Fact]
    public async Task NotifyCycleCompletedAsync_HttpFailure_DoesNotThrow()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var notifier = CreateNotifier(server);
        var run = new CycleRun(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            CycleOutcome.Ok,
            ItemsSeen: 1,
            ItemsProcessed: 1,
            Moved: 0,
            TagsApplied: 0,
            Notified: 0,
            FailureReason: null);

        var exception = await Record.ExceptionAsync(() => notifier.NotifyCycleCompletedAsync(run, CancellationToken.None));

        Assert.Null(exception);
    }

    private static DiscordCycleReportNotifier CreateNotifier(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new DiscordOptions { WebhookUrl = $"{server.Urls[0]}/webhook" });
        return new DiscordCycleReportNotifier(httpClient, options, NullLogger<DiscordCycleReportNotifier>.Instance);
    }
}
