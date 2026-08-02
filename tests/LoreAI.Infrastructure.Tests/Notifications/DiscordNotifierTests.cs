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

public class DiscordNotifierTests
{
    [Fact]
    public async Task NotifyAsync_PostsMessageContainingTitleAndLink()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));

        var notifier = CreateNotifier(server);
        var item = new RaindropItem(1, "Un outil à tester", "https://example.com/tool", null, null, [], null, null, null, DateTimeOffset.UtcNow, null);
        var classification = new ClassificationResult(".NET", ["dotnet"], RecommendedAction.ATester, Priority.Haute, "Très prometteur", "model", "raw");

        await notifier.NotifyAsync(item, classification, CancellationToken.None);

        var logEntry = Assert.Single(server.LogEntries);
        Assert.NotNull(logEntry.RequestMessage);
        var content = JsonDocument.Parse(logEntry.RequestMessage.Body!).RootElement.GetProperty("content").GetString();
        Assert.Contains("Un outil à tester", content);
        Assert.Contains("https://example.com/tool", content);
        Assert.Contains("Très prometteur", content);
        Assert.Contains(".NET", content);
        Assert.Contains("dotnet", content);
    }

    [Fact]
    public async Task NotifyAsync_HttpFailure_DoesNotThrow()
    {
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/webhook").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));

        var notifier = CreateNotifier(server);
        var item = new RaindropItem(1, "Titre", "https://example.com", null, null, [], null, null, null, DateTimeOffset.UtcNow, null);
        var classification = new ClassificationResult(null, [], RecommendedAction.Reference, Priority.Basse, "raison", "model", "raw");

        var exception = await Record.ExceptionAsync(() => notifier.NotifyAsync(item, classification, CancellationToken.None));

        Assert.Null(exception);
    }

    private static DiscordNotifier CreateNotifier(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new DiscordOptions { WebhookUrl = $"{server.Urls[0]}/webhook" });
        return new DiscordNotifier(httpClient, options, NullLogger<DiscordNotifier>.Instance);
    }
}
