using System.Globalization;
using System.Net;
using System.Text;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>Regroupe les articles par collection suggérée puis action — pure, testable sans envoi SMTP.</summary>
public static class DigestMessageBuilder
{
    private const string NoCollectionLabel = "Non déplacé (Non trié)";

    public static string BuildSubject(int articleCount) =>
        $"LoreAI — digest du jour ({articleCount} article{(articleCount > 1 ? "s" : string.Empty)})";

    public static string BuildHtmlBody(IReadOnlyList<ClassifiedArticle> articles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<html><body>");

        foreach (var collectionGroup in articles
                     .GroupBy(a => a.Classification.SuggestedCollection ?? NoCollectionLabel)
                     .OrderBy(g => g.Key))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"<h2>{WebUtility.HtmlEncode(collectionGroup.Key)}</h2>");

            foreach (var actionGroup in collectionGroup.GroupBy(a => a.Classification.Action).OrderBy(g => g.Key.ToString()))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"<h3>{actionGroup.Key}</h3><ul>");

                foreach (var article in actionGroup.OrderByDescending(a => a.Classification.Priority))
                {
                    var tags = article.Classification.Tags.Count > 0
                        ? string.Join(", ", article.Classification.Tags)
                        : "(aucun)";

                    builder.AppendLine(
                        CultureInfo.InvariantCulture,
                        $"<li>{BuildTitleHtml(article.Item.Title, article.Item.Link)} " +
                        $"— {article.Classification.Priority} — tags : {WebUtility.HtmlEncode(tags)} — {WebUtility.HtmlEncode(article.Classification.Reason)}</li>");
                }

                builder.AppendLine("</ul>");
            }
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    /// <summary>
    /// Le lien vient d'une page bookmarkée, donc d'une source non maîtrisée : il est encodé comme le reste
    /// (un <c>"</c> non échappé fermerait l'attribut <c>href</c> et permettrait d'injecter du balisage dans
    /// l'email), et seul un schéma http(s) donne droit à une ancre cliquable. Un lien d'un autre schéma
    /// (<c>javascript:</c>, <c>data:</c>…) reste affiché en texte pour ne rien perdre, mais sans ancre.
    /// </summary>
    private static string BuildTitleHtml(string title, string link)
    {
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedLink = WebUtility.HtmlEncode(link);

        return IsHttpLink(link)
            ? $"<a href=\"{encodedLink}\">{encodedTitle}</a>"
            : $"{encodedTitle} [{encodedLink}]";
    }

    private static bool IsHttpLink(string link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
