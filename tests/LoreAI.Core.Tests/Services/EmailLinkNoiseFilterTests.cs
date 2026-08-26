using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class EmailLinkNoiseFilterTests
{
    [Fact]
    public void Filter_ExactDuplicateHrefs_KeepsOnlyOne()
    {
        var urls = new[]
        {
            "https://example.com/article",
            "https://example.com/article",
        };

        var kept = EmailLinkNoiseFilter.Filter(urls);

        Assert.Single(kept);
    }

    [Theory]
    [InlineData("https://newsletter.example.com/unsubscribe?id=42")]
    [InlineData("https://newsletter.example.com/preferences")]
    [InlineData("https://www.linkedin.com/in/someone")]
    [InlineData("https://www.youtube.com/@somechannel")]
    public void Filter_TrivialPatterns_AreExcluded(string url)
    {
        var kept = EmailLinkNoiseFilter.Filter([url]);

        Assert.Empty(kept);
    }

    [Fact]
    public void Filter_YoutubeWatchLink_IsKept()
    {
        var kept = EmailLinkNoiseFilter.Filter(["https://www.youtube.com/watch?v=abc123"]);

        Assert.Single(kept);
    }

    /// <summary>
    /// Newsletter mono-article réelle (.NET Weekly, Anton DevTips, cf. roadmap lot 8) : ~15 liens de
    /// bruit noient 1 vrai article. Le filtre heuristique n'élimine que les patterns triviaux connus
    /// (désinscription, préférences, profils sociaux) ; un lien de partage social générique (ex. Twitter
    /// intent) n'est pas reconnu ici — trancher le reste est le rôle de <c>IEmailLinkExtractor</c>.
    /// </summary>
    [Fact]
    public void Filter_MonoArticleNewsletter_RemovesOnlyKnownTrivialPatterns()
    {
        var urls = new[]
        {
            "https://blog.example.com/real-article-about-dotnet",
            "https://newsletter.example.com/unsubscribe?id=42",
            "https://newsletter.example.com/preferences",
            "https://www.linkedin.com/in/sponsor-author",
            "https://twitter.com/intent/tweet?url=...",
            "https://www.youtube.com/@sponsorchannel",
        };

        var kept = EmailLinkNoiseFilter.Filter(urls);

        Assert.Contains("https://blog.example.com/real-article-about-dotnet", kept);
        Assert.Contains("https://twitter.com/intent/tweet?url=...", kept);
        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void Filter_NoCandidateUrls_ReturnsEmptyList()
    {
        var kept = EmailLinkNoiseFilter.Filter([]);

        Assert.Empty(kept);
    }
}
