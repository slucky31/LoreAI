using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class ReadingQueueScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Score_HumanHandled_IsExcluded()
    {
        var articles = new[] { CreateArticle(1, Priority.Haute, humanHandledAtUtc: Now) };

        Assert.Empty(ReadingQueueScorer.Score(articles, Now, 10));
    }

    [Fact]
    public void Score_Deleted_IsExcluded()
    {
        var articles = new[] { CreateArticle(1, Priority.Haute, linkStatus: LinkStatus.Deleted) };

        Assert.Empty(ReadingQueueScorer.Score(articles, Now, 10));
    }

    [Fact]
    public void Score_HigherPriority_RanksFirst()
    {
        var articles = new[]
        {
            CreateArticle(1, Priority.Basse),
            CreateArticle(2, Priority.Haute),
        };

        var result = ReadingQueueScorer.Score(articles, Now, 10);

        Assert.Equal(2, result[0].Id);
        Assert.Equal(1, result[1].Id);
    }

    [Fact]
    public void Score_Fresher_RanksFirstAtEqualPriority()
    {
        var articles = new[]
        {
            CreateArticle(1, Priority.Moyenne, capturedAtUtc: Now.AddDays(-80)),
            CreateArticle(2, Priority.Moyenne, capturedAtUtc: Now.AddDays(-1)),
        };

        var result = ReadingQueueScorer.Score(articles, Now, 10);

        Assert.Equal(2, result[0].Id);
        Assert.Equal(1, result[1].Id);
    }

    [Fact]
    public void Score_RespectsLimit()
    {
        var articles = new[] { CreateArticle(1, Priority.Haute), CreateArticle(2, Priority.Haute), CreateArticle(3, Priority.Haute) };

        var result = ReadingQueueScorer.Score(articles, Now, 2);

        Assert.Equal(2, result.Count);
    }

    private static TrackedArticle CreateArticle(
        long id,
        Priority priority,
        DateTimeOffset? capturedAtUtc = null,
        DateTimeOffset? humanHandledAtUtc = null,
        LinkStatus? linkStatus = null) =>
        new(id, $"Titre {id}", $"https://example.com/{id}", RecommendedAction.ALire, priority,
            capturedAtUtc ?? Now, Now, null, humanHandledAtUtc, linkStatus ?? LinkStatus.Ok, SourceType.Raindrop, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
