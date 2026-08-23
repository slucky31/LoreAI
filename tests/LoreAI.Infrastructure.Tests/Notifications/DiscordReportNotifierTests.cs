using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LoreAI.Infrastructure.Notifications;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace LoreAI.Infrastructure.Tests.Notifications;

public class DiscordReportNotifierTests
{
    [Fact]
    public async Task SendReportAsync_PostsMultipartRequestContainingFileContent()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200));

        var notifier = CreateNotifier(server);

        await notifier.SendReportAsync("loreai-insights-2026-08-23.md", "# Rapport\n\nAucun doublon.", CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        Assert.NotNull(logEntry.RequestMessage);
        var body = logEntry.RequestMessage.Body!;
        Assert.Contains("loreai-insights-2026-08-23.md", body, StringComparison.Ordinal);
        Assert.Contains("Aucun doublon.", body, StringComparison.Ordinal);
        Assert.Contains("payload_json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendReportAsync_HttpFailure_DoesNotThrow()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var notifier = CreateNotifier(server);

        var exception = await Record.ExceptionAsync(() =>
            notifier.SendReportAsync("report.md", "contenu", CancellationToken.None));

        Assert.Null(exception);
    }

    private static DiscordReportNotifier CreateNotifier(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new DiscordOptions { WebhookUrl = $"{server.Urls[0]}/webhook" });
        return new DiscordReportNotifier(httpClient, options, NullLogger<DiscordReportNotifier>.Instance);
    }
}
