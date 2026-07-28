using System.Net;
using System.Text;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Notifications;

/// <summary>Regroupe les articles par collection suggérée puis action — pure, testable sans envoi SMTP.</summary>
public static class DigestMessageBuilder
{
    private const string NoCollectionLabel = "Non déplacé (Non trié)";

    public static string BuildSubject(int articleCount) =>
        $"RaindropAI — digest du jour ({articleCount} article{(articleCount > 1 ? "s" : string.Empty)})";

    public static string BuildHtmlBody(IReadOnlyList<ClassifiedArticle> articles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<html><body>");

        foreach (var collectionGroup in articles
                     .GroupBy(a => a.Classification.SuggestedCollection ?? NoCollectionLabel)
                     .OrderBy(g => g.Key))
        {
            builder.AppendLine($"<h2>{WebUtility.HtmlEncode(collectionGroup.Key)}</h2>");

            foreach (var actionGroup in collectionGroup.GroupBy(a => a.Classification.Action).OrderBy(g => g.Key.ToString()))
            {
                builder.AppendLine($"<h3>{actionGroup.Key}</h3><ul>");

                foreach (var article in actionGroup.OrderByDescending(a => a.Classification.Priority))
                {
                    var tags = article.Classification.Tags.Count > 0
                        ? string.Join(", ", article.Classification.Tags)
                        : "(aucun)";

                    builder.AppendLine(
                        $"<li><a href=\"{article.Item.Link}\">{WebUtility.HtmlEncode(article.Item.Title)}</a> " +
                        $"— {article.Classification.Priority} — tags : {WebUtility.HtmlEncode(tags)} — {WebUtility.HtmlEncode(article.Classification.Reason)}</li>");
                }

                builder.AppendLine("</ul>");
            }
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }
}
