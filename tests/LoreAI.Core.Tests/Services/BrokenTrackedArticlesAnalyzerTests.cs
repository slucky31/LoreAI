using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class BrokenTrackedArticlesAnalyzerTests
{
    [Fact]
    public void Detect_BrokenLink_IsReported()
    {
        var articles = new[] { CreateArticle(1, LinkStatus.Broken) };

        var result = BrokenTrackedArticlesAnalyzer.Detect(articles);

        var single = Assert.Single(result);
        Assert.Equal(LinkStatus.Broken, single.LinkStatus);
    }

    [Fact]
    public void Detect_DeletedLink_IsReported()
    {
        var articles = new[] { CreateArticle(1, LinkStatus.Deleted) };

        var result = BrokenTrackedArticlesAnalyzer.Detect(articles);

        Assert.Single(result);
    }

    [Fact]
    public void Detect_OkLink_IsIgnored()
    {
        var articles = new[] { CreateArticle(1, LinkStatus.Ok) };

        Assert.Empty(BrokenTrackedArticlesAnalyzer.Detect(articles));
    }

    [Fact]
    public void Detect_NeverReconciled_IsIgnored()
    {
        var articles = new[] { CreateArticle(1, linkStatus: null) };

        Assert.Empty(BrokenTrackedArticlesAnalyzer.Detect(articles));
    }

    private static TrackedArticle CreateArticle(long id, LinkStatus? linkStatus) =>
        new(id, $"Titre {id}", $"https://example.com/{id}", RecommendedAction.ALire, Priority.Basse,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, linkStatus);
}
