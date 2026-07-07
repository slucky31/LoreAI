using RaindropAI.Core.Enums;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Notifications;

namespace RaindropAI.Infrastructure.Tests.Notifications;

public class DigestMessageBuilderTests
{
    [Fact]
    public void BuildSubject_SingularForOneArticle()
    {
        Assert.Equal("RaindropAI — digest du jour (1 article)", DigestMessageBuilder.BuildSubject(1));
    }

    [Fact]
    public void BuildSubject_PluralForMultipleArticles()
    {
        Assert.Equal("RaindropAI — digest du jour (3 articles)", DigestMessageBuilder.BuildSubject(3));
    }

    [Fact]
    public void BuildHtmlBody_GroupsByCategoryThenAction()
    {
        var articles = new[]
        {
            CreateArticle("Article DotNet a lire", Category.DotNet, RecommendedAction.ALire, Priority.Basse),
            CreateArticle("Outil DotNet a tester", Category.DotNet, RecommendedAction.ATester, Priority.Haute),
            CreateArticle("Formation a suivre", Category.Formation, RecommendedAction.ALire, Priority.Moyenne),
        };

        var html = DigestMessageBuilder.BuildHtmlBody(articles);

        Assert.Contains("<h2>DotNet</h2>", html);
        Assert.Contains("<h2>Formation</h2>", html);
        Assert.Contains("<h3>ALire</h3>", html);
        Assert.Contains("<h3>ATester</h3>", html);
        Assert.Contains("Article DotNet a lire", html);
        Assert.Contains("Outil DotNet a tester", html);
        Assert.Contains("Formation a suivre", html);
    }

    [Fact]
    public void BuildHtmlBody_HtmlEncodesTitleAndReason()
    {
        var article = CreateArticle("Titre <script>alert(1)</script>", Category.Autre, RecommendedAction.Reference, Priority.Basse);

        var html = DigestMessageBuilder.BuildHtmlBody([article]);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    private static ClassifiedArticle CreateArticle(string title, Category category, RecommendedAction action, Priority priority)
    {
        var item = new RaindropItem(
            Id: Random.Shared.NextInt64(),
            Title: title,
            Link: "https://example.com",
            Excerpt: null,
            Note: null,
            Tags: [],
            CollectionId: null,
            Domain: "example.com",
            RaindropType: "article",
            CreatedUtc: DateTimeOffset.UtcNow,
            LastUpdateUtc: null);

        var classification = new ClassificationResult(category, action, priority, "raison", "model", "raw");

        return new ClassifiedArticle(item, classification, DateTimeOffset.UtcNow, null, null);
    }
}
