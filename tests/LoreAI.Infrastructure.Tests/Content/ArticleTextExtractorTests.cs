using LoreAI.Infrastructure.Content;

namespace LoreAI.Infrastructure.Tests.Content;

public class ArticleTextExtractorTests
{
    private static string LongParagraph(string prefix) =>
        prefix + " " + string.Join(' ', Enumerable.Repeat("mot", 40));

    [Fact]
    public void Extract_PrefersArticleElement()
    {
        var html = $"""
            <html><body>
              <nav>Menu du site</nav>
              <article><p>{LongParagraph("Contenu principal")}</p></article>
              <footer>Copyright</footer>
            </body></html>
            """;

        var (text, wordCount) = ArticleTextExtractor.Extract(html);

        Assert.NotNull(text);
        Assert.Contains("Contenu principal", text);
        Assert.DoesNotContain("Menu du site", text);
        Assert.DoesNotContain("Copyright", text);
        Assert.True(wordCount > 0);
    }

    [Fact]
    public void Extract_NoArticleElement_FallsBackToMain()
    {
        var html = $"""
            <html><body>
              <header>Entête</header>
              <main><p>{LongParagraph("Texte principal via main")}</p></main>
            </body></html>
            """;

        var (text, _) = ArticleTextExtractor.Extract(html);

        Assert.NotNull(text);
        Assert.Contains("Texte principal via main", text);
        Assert.DoesNotContain("Entête", text);
    }

    [Fact]
    public void Extract_NoArticleOrMain_FallsBackToBody()
    {
        var html = $"<html><body><p>{LongParagraph("Juste du body")}</p></body></html>";

        var (text, _) = ArticleTextExtractor.Extract(html);

        Assert.NotNull(text);
        Assert.Contains("Juste du body", text);
    }

    [Fact]
    public void Extract_RemovesScriptAndStyleContent()
    {
        var html = """
            <html><body>
              <article>
                <script>alert('bruit javascript');</script>
                <style>.bruit-css { color: red; }</style>
                <p>PLACEHOLDER</p>
              </article>
            </body></html>
            """.Replace("PLACEHOLDER", LongParagraph("Contenu propre"), StringComparison.Ordinal);

        var (text, _) = ArticleTextExtractor.Extract(html);

        Assert.NotNull(text);
        Assert.DoesNotContain("bruit javascript", text);
        Assert.DoesNotContain("bruit-css", text);
    }

    /// <summary>Proxy heuristique pour une page JS-only/paywall : trop peu de mots pour être exploitable.</summary>
    [Fact]
    public void Extract_TooFewWords_ReturnsNull()
    {
        var html = "<html><body><article><p>Chargement…</p></article></body></html>";

        var (text, wordCount) = ArticleTextExtractor.Extract(html);

        Assert.Null(text);
        Assert.Null(wordCount);
    }

    [Fact]
    public void Extract_CountsWordsInExtractedText()
    {
        var html = $"<html><body><article><p>{LongParagraph("Un deux trois")}</p></article></body></html>";

        var (_, wordCount) = ArticleTextExtractor.Extract(html);

        Assert.NotNull(wordCount);
        Assert.True(wordCount >= 40);
    }
}
