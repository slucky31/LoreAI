using System.Globalization;
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

public class DiscordWeeklyDigestNotifierTests
{
    [Fact]
    public async Task SendDigestAsync_HappyPath_PostsTwoMessages()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/webhook").UsingPost()).RespondWith(Response.Create().WithStatusCode(200));

        var notifier = CreateNotifier(server);

        await notifier.SendDigestAsync(EmptyReport(), TestContext.Current.CancellationToken);

        Assert.Equal(2, server.LogEntries.Count);
    }

    [Fact]
    public async Task SendDigestAsync_ActionableMessage_ContainsReadingQueueBrokenLinksAndStaleArticles()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/webhook").UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        var notifier = CreateNotifier(server);

        var report = EmptyReport() with
        {
            ReadingQueue = [new ReadingQueueEntry(1, "Titre A", "https://a.example", 1.0, 5, Priority.Haute, DateTimeOffset.UtcNow, SourceType.Raindrop, "1")],
            BrokenTrackedArticles = [new BrokenTrackedArticle(2, "Titre B", "https://b.example", LinkStatus.Broken)],
            StaleArticles = [new StaleArticle(3, "Titre C", "https://c.example", 95)],
        };

        await notifier.SendDigestAsync(report, TestContext.Current.CancellationToken);

        var fields = GetFields(server, messageIndex: 0);
        Assert.Contains(fields, f => f.Name == "File de lecture (L1)" && f.Value.Contains("[Titre A](https://a.example)", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "Liens morts (N3)" && f.Value.Contains("[Titre B](https://b.example)", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "Articles périmés (N4)" && f.Value.Contains("[Titre C](https://c.example)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendDigestAsync_HygieneMessage_ContainsDuplicatesTagsCollectionsTrendsAndCost()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/webhook").UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        var notifier = CreateNotifier(server);

        var report = EmptyReport() with
        {
            DuplicateUrls = [new DuplicateUrlGroup("example.com/a", [new DuplicateLink(1, "Titre A", "https://a.example"), new DuplicateLink(2, "Titre B", "https://b.example")])],
            TagHygiene = new TagHygieneResult([new TagCluster(["dotnet", "dot-net"])], ["obscure"]),
            UnbalancedCollections = [new UnbalancedCollection("Veille", 1)],
            TopDomains = [new DomainTrend("example.com", 3)],
            TopTags = [new TagTrend("dotnet", 2)],
            LlmUsage = new LlmUsageSummary(42, 100_000, 10_000, 0, 0, 0.15m),
        };

        await notifier.SendDigestAsync(report, TestContext.Current.CancellationToken);

        var fields = GetFields(server, messageIndex: 1);
        Assert.Contains(fields, f => f.Name == "Doublons d'URL (N1)" && f.Value.Contains("[Titre A](https://a.example)", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "Hygiène des tags (N2)" && f.Value.Contains("dotnet / dot-net", StringComparison.Ordinal) && f.Value.Contains("obscure", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "Collections déséquilibrées (N5)" && f.Value.Contains("Veille", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "Tendances (S3)" && f.Value.Contains("example.com", StringComparison.Ordinal));
        Assert.Contains(fields, f => f.Name == "Coût LLM (S6)" && f.Value.Contains("Classifications ce mois-ci : 42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendDigestAsync_MoreThanTenEntriesInASection_CapsAndShowsRemainingCount()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/webhook").UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        var notifier = CreateNotifier(server);

        var readingQueue = Enumerable.Range(1, 13)
            .Select(i => new ReadingQueueEntry(i, $"Titre {i}", $"https://example.com/{i}", 1.0, null, Priority.Moyenne, DateTimeOffset.UtcNow, SourceType.Raindrop, i.ToString(CultureInfo.InvariantCulture)))
            .ToList();
        var report = EmptyReport() with { ReadingQueue = readingQueue };

        await notifier.SendDigestAsync(report, TestContext.Current.CancellationToken);

        var value = GetFields(server, messageIndex: 0).Single(f => f.Name == "File de lecture (L1)").Value;
        Assert.Contains("Titre 1]", value, StringComparison.Ordinal);
        Assert.Contains("Titre 10]", value, StringComparison.Ordinal);
        Assert.DoesNotContain("Titre 11]", value, StringComparison.Ordinal);
        Assert.Contains("et 3 de plus", value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendDigestAsync_EmptySections_ReportsNothingWithoutThrowing()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/webhook").UsingPost()).RespondWith(Response.Create().WithStatusCode(200));
        var notifier = CreateNotifier(server);

        var exception = await Record.ExceptionAsync(() => notifier.SendDigestAsync(EmptyReport(), TestContext.Current.CancellationToken));

        Assert.Null(exception);
        var fields = GetFields(server, messageIndex: 0);
        Assert.Contains(fields, f => f.Name == "File de lecture (L1)" && f.Value == "Aucun.");
    }

    [Fact]
    public async Task SendDigestAsync_HttpFailure_DoesNotThrow()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/webhook").UsingPost()).RespondWith(Response.Create().WithStatusCode(500));
        var notifier = CreateNotifier(server);

        var exception = await Record.ExceptionAsync(() => notifier.SendDigestAsync(EmptyReport(), TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    private static List<(string Name, string Value)> GetFields(WireMockServer server, int messageIndex)
    {
        var entry = server.LogEntries.ElementAt(messageIndex);
        var root = JsonDocument.Parse(entry.RequestMessage!.Body!).RootElement;
        return root.GetProperty("embeds")[0].GetProperty("fields")
            .EnumerateArray()
            .Select(f => (f.GetProperty("name").GetString()!, f.GetProperty("value").GetString()!))
            .ToList();
    }

    private static WeeklyInsightsReport EmptyReport() => new(
        [],
        new TagHygieneResult([], []),
        [],
        [],
        [],
        new LlmUsageSummary(0, 0, 0, 0, 0, 0m),
        [],
        [],
        [],
        DateTimeOffset.UnixEpoch);

    private static DiscordWeeklyDigestNotifier CreateNotifier(WireMockServer server)
    {
        var httpClient = new HttpClient();
        var options = Options.Create(new DiscordOptions { WebhookUrl = $"{server.Urls[0]}/webhook" });
        return new DiscordWeeklyDigestNotifier(httpClient, options, NullLogger<DiscordWeeklyDigestNotifier>.Instance);
    }
}
