using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Notifications;

namespace LoreAI.Infrastructure.Tests.Notifications;

public class MarkdownReportBuilderTests
{
    [Fact]
    public void Build_EmptyReport_StillProducesAllSectionsWithoutThrowing()
    {
        var report = CreateReport(
            duplicates: [],
            hygiene: new TagHygieneResult([], []),
            unbalanced: [],
            domains: [],
            tags: [],
            usage: new LlmUsageSummary(0, 0, 0, 0, 0, 0m));

        var markdown = MarkdownReportBuilder.Build(report);

        Assert.Contains("## Doublons d'URL (N1)", markdown);
        Assert.Contains("## Hygiène des tags (N2)", markdown);
        Assert.Contains("## Collections déséquilibrées (N5)", markdown);
        Assert.Contains("## Tendances (S3)", markdown);
        Assert.Contains("## Coût LLM (S6)", markdown);
    }

    [Fact]
    public void Build_DuplicateGroup_ListsBothLinksAsMarkdownLinks()
    {
        var report = CreateReport(
            duplicates: [new DuplicateUrlGroup("example.com/a", [new DuplicateLink(1, "Titre A", "https://a.example"), new DuplicateLink(2, "Titre B", "https://b.example")])],
            hygiene: new TagHygieneResult([], []),
            unbalanced: [],
            domains: [],
            tags: [],
            usage: new LlmUsageSummary(0, 0, 0, 0, 0, 0m));

        var markdown = MarkdownReportBuilder.Build(report);

        Assert.Contains("[Titre A](https://a.example)", markdown);
        Assert.Contains("[Titre B](https://b.example)", markdown);
    }

    [Fact]
    public void Build_TagHygiene_ListsClustersAndSingleUseTags()
    {
        var report = CreateReport(
            duplicates: [],
            hygiene: new TagHygieneResult([new TagCluster(["dotnet", "dot-net"])], ["obscure"]),
            unbalanced: [],
            domains: [],
            tags: [],
            usage: new LlmUsageSummary(0, 0, 0, 0, 0, 0m));

        var markdown = MarkdownReportBuilder.Build(report);

        Assert.Contains("dotnet / dot-net", markdown);
        Assert.Contains("obscure", markdown);
    }

    [Fact]
    public void Build_LlmUsage_IncludesTokenCountsAndEstimatedCost()
    {
        var report = CreateReport(
            duplicates: [],
            hygiene: new TagHygieneResult([], []),
            unbalanced: [],
            domains: [],
            tags: [],
            usage: new LlmUsageSummary(42, 100_000, 10_000, 500, 200, 0.15m));

        var markdown = MarkdownReportBuilder.Build(report);

        Assert.Contains("Classifications ce mois-ci : 42", markdown);
        Assert.Contains("100000 / 10000", markdown);
        Assert.Contains("500 / 200", markdown);
        Assert.Contains("0.15 $", markdown);
    }

    [Fact]
    public void BuildMonthlyReview_IncludesThemeNarrativeAndArticleLinks()
    {
        var article = new MonthlyReviewArticle(1, "Titre A", "https://a.example", "Veille .NET", [], null, null, Priority.Moyenne);
        var report = new MonthlyReviewReport(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            [new ThemeReview("Veille .NET", "Un mois riche en actualités .NET.", [article])],
            DateTimeOffset.UnixEpoch);

        var markdown = MarkdownReportBuilder.BuildMonthlyReview(report);

        Assert.Contains("## Veille .NET", markdown);
        Assert.Contains("Un mois riche en actualités .NET.", markdown);
        Assert.Contains("[Titre A](https://a.example)", markdown);
    }

    private static WeeklyInsightsReport CreateReport(
        IReadOnlyList<DuplicateUrlGroup> duplicates,
        TagHygieneResult hygiene,
        IReadOnlyList<UnbalancedCollection> unbalanced,
        IReadOnlyList<DomainTrend> domains,
        IReadOnlyList<TagTrend> tags,
        LlmUsageSummary usage) =>
        new(duplicates, hygiene, unbalanced, domains, tags, usage, DateTimeOffset.UnixEpoch);
}
