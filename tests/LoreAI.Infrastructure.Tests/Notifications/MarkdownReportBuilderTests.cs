using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Notifications;

namespace LoreAI.Infrastructure.Tests.Notifications;

public class MarkdownReportBuilderTests
{
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

    [Fact]
    public void BuildToolCard_IncludesFrontmatterAndRelatedArticles()
    {
        var card = new ToolCard(
            1,
            "Ollama",
            "CLI",
            "À évaluer",
            null,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
            [new ToolRelatedArticle(1, "Découverte d'Ollama", "https://a.example", "Un outil pour faire tourner des LLM en local.")]);

        var markdown = MarkdownReportBuilder.BuildToolCard(card);

        Assert.Contains("name: Ollama", markdown);
        Assert.Contains("category: CLI", markdown);
        Assert.Contains("status: À évaluer", markdown);
        Assert.Contains("verdict: (à déterminer)", markdown);
        Assert.Contains("[Découverte d'Ollama](https://a.example)", markdown);
    }

    [Fact]
    public void BuildToolCard_NoRelatedArticles_StillProducesValidMarkdown()
    {
        var card = new ToolCard(1, "Ollama", null, "À évaluer", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, []);

        var markdown = MarkdownReportBuilder.BuildToolCard(card);

        Assert.Contains("Aucun.", markdown);
    }

    [Fact]
    public void BuildToolCard_WithUrl_IncludesUrlInFrontmatter()
    {
        var card = new ToolCard(1, "Ollama", "CLI", "À évaluer", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [], "https://ollama.com");

        var markdown = MarkdownReportBuilder.BuildToolCard(card);

        Assert.Contains("url: https://ollama.com", markdown);
    }

    [Fact]
    public void BuildToolCard_NoUrl_OmitsUrlLine()
    {
        var card = new ToolCard(1, "Ollama", "CLI", "À évaluer", null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, []);

        var markdown = MarkdownReportBuilder.BuildToolCard(card);

        Assert.DoesNotContain("url:", markdown);
    }

    [Fact]
    public void BuildItemExport_ClassifiedItem_IncludesSummary()
    {
        var item = new LibraryItemSummary(1, "Titre", "https://a.example", ["dotnet"], 10, new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero));

        var markdown = MarkdownReportBuilder.BuildItemExport(item, "Un résumé de l'article.");

        Assert.Contains("title: Titre", markdown);
        Assert.Contains("url: https://a.example", markdown);
        Assert.Contains("tags: [dotnet]", markdown);
        Assert.Contains("Un résumé de l'article.", markdown);
    }

    [Fact]
    public void BuildItemExport_NeverClassified_ExplainsMissingSummary()
    {
        var item = new LibraryItemSummary(1, "Titre", "https://a.example", [], null, DateTimeOffset.UnixEpoch);

        var markdown = MarkdownReportBuilder.BuildItemExport(item, summary: null);

        Assert.Contains("pas de résumé disponible", markdown);
    }
}
