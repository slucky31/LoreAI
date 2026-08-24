using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace LoreAI.Infrastructure.Content;

/// <summary>
/// Extraction best-effort du texte principal d'une page HTML — pure, sans réseau, testable directement
/// avec du HTML en dur. Heuristique volontairement simple (pas de port Readability, cf. choix du lot 4) :
/// retire le bruit habituel, préfère &lt;article&gt; puis &lt;main&gt;, replie sur &lt;body&gt;.
/// </summary>
public static class ArticleTextExtractor
{
    private const int MaxStoredContentLength = 20_000;

    /// <summary>
    /// En dessous de ce nombre de mots, le texte extrait est considéré inexploitable (page JS-only,
    /// paywall qui ne laisse filtrer qu'un bandeau...) — un proxy heuristique, pas une détection fiable.
    /// </summary>
    private const int MinWordsForSuccess = 30;

    private static readonly string[] NoiseSelectors = ["script", "style", "nav", "header", "footer", "aside", "noscript"];

    public static (string? Text, int? WordCount) Extract(string html)
    {
        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);

        foreach (var selector in NoiseSelectors)
        {
            foreach (var element in document.QuerySelectorAll(selector).ToList())
            {
                element.Remove();
            }
        }

        var root = document.QuerySelector("article") ?? document.QuerySelector("main") ?? document.Body;
        var text = Normalize(root?.TextContent);
        var wordCount = text.Length == 0 ? 0 : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        return wordCount < MinWordsForSuccess
            ? (null, null)
            : (Truncate(text, MaxStoredContentLength), wordCount);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
