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
    public void BuildHtmlBody_GroupsBySuggestedCollectionThenAction()
    {
        var articles = new[]
        {
            CreateArticle("Article DotNet a lire", "DotNet", RecommendedAction.ALire, Priority.Basse, []),
            CreateArticle("Outil DotNet a tester", "DotNet", RecommendedAction.ATester, Priority.Haute, ["dotnet", "outil"]),
            CreateArticle("Formation a suivre", "Formations", RecommendedAction.ALire, Priority.Moyenne, []),
        };

        var html = DigestMessageBuilder.BuildHtmlBody(articles);

        Assert.Contains("<h2>DotNet</h2>", html);
        Assert.Contains("<h2>Formations</h2>", html);
        Assert.Contains("<h3>ALire</h3>", html);
        Assert.Contains("<h3>ATester</h3>", html);
        Assert.Contains("Article DotNet a lire", html);
        Assert.Contains("Outil DotNet a tester", html);
        Assert.Contains("Formation a suivre", html);
        Assert.Contains("dotnet, outil", html);
    }

    [Fact]
    public void BuildHtmlBody_GroupsUnmatchedArticlesUnderNoCollectionLabel()
    {
        var article = CreateArticle("Article non déplacé", null, RecommendedAction.Reference, Priority.Basse, []);

        var html = DigestMessageBuilder.BuildHtmlBody([article]);

        Assert.Contains(System.Net.WebUtility.HtmlEncode("Non déplacé (Non trié)"), html);
    }

    [Fact]
    public void BuildHtmlBody_HtmlEncodesTitleAndReason()
    {
        var article = CreateArticle("Titre <script>alert(1)</script>", "Autre", RecommendedAction.Reference, Priority.Basse, []);

        var html = DigestMessageBuilder.BuildHtmlBody([article]);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    private static ClassifiedArticle CreateArticle(
        string title,
        string? suggestedCollection,
        RecommendedAction action,
        Priority priority,
        IReadOnlyList<string> tags)
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

        var classification = new ClassificationResult(suggestedCollection, tags, action, priority, "raison", "model", "raw");

        return new ClassifiedArticle(item, classification, DateTimeOffset.UtcNow, Moved: suggestedCollection is not null, null, null);
    }
}
