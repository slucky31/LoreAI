using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class StaleArticlesAnalyzerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Detect_ALireOlderThan90Days_NeverHandled_IsReported()
    {
        var articles = new[] { CreateArticle(1, RecommendedAction.ALire, Now.AddDays(-91), humanHandledAtUtc: null) };

        var result = StaleArticlesAnalyzer.Detect(articles, Now);

        var single = Assert.Single(result);
        Assert.Equal(91, single.DaysSinceCaptured);
    }

    [Fact]
    public void Detect_ALireUnder90Days_IsIgnored()
    {
        var articles = new[] { CreateArticle(1, RecommendedAction.ALire, Now.AddDays(-10), humanHandledAtUtc: null) };

        Assert.Empty(StaleArticlesAnalyzer.Detect(articles, Now));
    }

    [Fact]
    public void Detect_AlreadyHumanHandled_IsIgnored()
    {
        var articles = new[] { CreateArticle(1, RecommendedAction.ALire, Now.AddDays(-91), humanHandledAtUtc: Now.AddDays(-1)) };

        Assert.Empty(StaleArticlesAnalyzer.Detect(articles, Now));
    }

    [Fact]
    public void Detect_NonALireAction_IsIgnored()
    {
        var articles = new[] { CreateArticle(1, RecommendedAction.ATester, Now.AddDays(-91), humanHandledAtUtc: null) };

        Assert.Empty(StaleArticlesAnalyzer.Detect(articles, Now));
    }

    private static TrackedArticle CreateArticle(long id, RecommendedAction action, DateTimeOffset capturedAtUtc, DateTimeOffset? humanHandledAtUtc) =>
        new(id, $"Titre {id}", $"https://example.com/{id}", action, Priority.Basse, capturedAtUtc, capturedAtUtc, null, humanHandledAtUtc, LinkStatus.Ok, SourceType.Raindrop, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
