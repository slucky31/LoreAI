using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class MonthlyReviewGrouperTests
{
    [Fact]
    public void GroupByTheme_GroupsByCollection()
    {
        var articles = new[]
        {
            CreateArticle(1, "dotnet 1", "Veille .NET"),
            CreateArticle(2, "dotnet 2", "Veille .NET"),
            CreateArticle(3, "IA", "IA"),
        };

        var groups = MonthlyReviewGrouper.GroupByTheme(articles);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Theme == "Veille .NET" && g.Articles.Count == 2);
        Assert.Contains(groups, g => g.Theme == "IA" && g.Articles.Count == 1);
    }

    [Fact]
    public void GroupByTheme_NullSuggestedCollection_FallsBackToUnclassifiedTheme()
    {
        var articles = new[] { CreateArticle(1, "Sans collection", suggestedCollection: null) };

        var groups = MonthlyReviewGrouper.GroupByTheme(articles);

        var single = Assert.Single(groups);
        Assert.Equal(MonthlyReviewGrouper.UnclassifiedTheme, single.Theme);
    }

    [Fact]
    public void GroupByTheme_OrdersByGroupSizeDescendingThenName()
    {
        var articles = new[]
        {
            CreateArticle(1, "A", "Z"),
            CreateArticle(2, "B", "A"),
            CreateArticle(3, "C", "A"),
        };

        var groups = MonthlyReviewGrouper.GroupByTheme(articles);

        Assert.Equal(["A", "Z"], groups.Select(g => g.Theme));
    }

    private static MonthlyReviewArticle CreateArticle(long id, string title, string? suggestedCollection) =>
        new(id, title, $"https://example.com/{id}", suggestedCollection, [], null, null, Priority.Moyenne);
}
