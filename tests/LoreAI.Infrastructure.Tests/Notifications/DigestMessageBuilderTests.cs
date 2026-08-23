using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Notifications;

namespace LoreAI.Infrastructure.Tests.Notifications;

public class DigestMessageBuilderTests
{
    [Fact]
    public void BuildSubject_SingularForOneArticle()
    {
        Assert.Equal("LoreAI — digest du jour (1 article)", DigestMessageBuilder.BuildSubject(1));
    }

    [Fact]
    public void BuildSubject_PluralForMultipleArticles()
    {
        Assert.Equal("LoreAI — digest du jour (3 articles)", DigestMessageBuilder.BuildSubject(3));
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
    public void BuildHtmlBody_HttpLink_IsRenderedAsAnAnchor()
    {
        var article = CreateArticle("Un article", "Autre", RecommendedAction.ALire, Priority.Basse, [])
            with { Item = CreateItem("Un article", "https://example.com/a?x=1&y=2") };

        var html = DigestMessageBuilder.BuildHtmlBody([article]);

        // Le & doit être encodé en &amp; : c'est du HTML, pas de l'URL brute.
        Assert.Contains("<a href=\"https://example.com/a?x=1&amp;y=2\">Un article</a>", html);
    }

    /// <summary>
    /// Le lien vient d'une page bookmarkée : un guillemet non échappé fermerait l'attribut href
    /// et permettrait d'injecter du balisage arbitraire dans l'email. C'est le finding F-08.
    /// </summary>
    [Fact]
    public void BuildHtmlBody_LinkContainingAQuote_CannotBreakOutOfTheHrefAttribute()
    {
        var malicious = "https://example.com/\" onmouseover=\"alert(1)";
        var article = CreateArticle("Un article", "Autre", RecommendedAction.ALire, Priority.Basse, [])
            with { Item = CreateItem("Un article", malicious) };

        var html = DigestMessageBuilder.BuildHtmlBody([article]);

        Assert.DoesNotContain("onmouseover=\"", html);
        Assert.Contains("&quot;", html);
    }

    [Fact]
    public void BuildHtmlBody_NonHttpScheme_IsNotRenderedAsAClickableAnchor()
    {
        var article = CreateArticle("Un article", "Autre", RecommendedAction.ALire, Priority.Basse, [])
            with { Item = CreateItem("Un article", "javascript:alert(1)") };

        var html = DigestMessageBuilder.BuildHtmlBody([article]);

        Assert.DoesNotContain("<a href", html);
        // Le lien reste visible en texte : on ne perd pas l'information.
        Assert.Contains("javascript:alert(1)", html);
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
        var item = CreateItem(title, "https://example.com");
        var classification = new ClassificationResult(suggestedCollection, tags, action, priority, "raison", "model", "raw");

        return new ClassifiedArticle(item, classification, DateTimeOffset.UtcNow, Moved: suggestedCollection is not null, null, null, DateTimeOffset.UtcNow, null);
    }

    private static Item CreateItem(string title, string link) => new(
        SourceType: SourceType.Raindrop,
        SourceId: Random.Shared.NextInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
        Url: link,
        Title: title,
        Excerpt: null,
        Note: null,
        Tags: [],
        CapturedAtUtc: DateTimeOffset.UtcNow);
}
