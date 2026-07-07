using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Notifications;
using RichardSzalay.MockHttp;

namespace RaindropAI.Infrastructure.Tests.Notifications;

public class DiscordNotifierTests
{
    private const string WebhookUrl = "https://discord.com/api/webhooks/123/abc";

    [Fact]
    public async Task NotifyAsync_PostsMessageContainingTitleAndLink()
    {
        var mockHttp = new MockHttpMessageHandler();
        string? capturedBody = null;
        mockHttp.When(HttpMethod.Post, WebhookUrl)
            .Respond(async request =>
            {
                capturedBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            });

        var notifier = CreateNotifier(mockHttp);
        var item = new RaindropItem(1, "Un outil à tester", "https://example.com/tool", null, null, [], null, null, null, DateTimeOffset.UtcNow, null);
        var classification = new ClassificationResult(Category.DotNet, RecommendedAction.ATester, Priority.Haute, "Très prometteur", "model", "raw");

        await notifier.NotifyAsync(item, classification, CancellationToken.None);

        Assert.NotNull(capturedBody);
        var content = JsonDocument.Parse(capturedBody).RootElement.GetProperty("content").GetString();
        Assert.Contains("Un outil à tester", content);
        Assert.Contains("https://example.com/tool", content);
        Assert.Contains("Très prometteur", content);
    }

    [Fact]
    public async Task NotifyAsync_HttpFailure_DoesNotThrow()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, WebhookUrl).Respond(System.Net.HttpStatusCode.InternalServerError);

        var notifier = CreateNotifier(mockHttp);
        var item = new RaindropItem(1, "Titre", "https://example.com", null, null, [], null, null, null, DateTimeOffset.UtcNow, null);
        var classification = new ClassificationResult(Category.Autre, RecommendedAction.Reference, Priority.Basse, "raison", "model", "raw");

        var exception = await Record.ExceptionAsync(() => notifier.NotifyAsync(item, classification, CancellationToken.None));

        Assert.Null(exception);
    }

    private static DiscordNotifier CreateNotifier(MockHttpMessageHandler mockHttp)
    {
        var httpClient = mockHttp.ToHttpClient();
        var options = Options.Create(new DiscordOptions { WebhookUrl = WebhookUrl });
        return new DiscordNotifier(httpClient, options, NullLogger<DiscordNotifier>.Instance);
    }
}
