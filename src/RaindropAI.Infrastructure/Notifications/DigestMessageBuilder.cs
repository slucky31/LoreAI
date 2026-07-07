using System.Net;
using System.Text;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Notifications;

/// <summary>Regroupe les articles par catégorie puis action — pure, testable sans envoi SMTP.</summary>
public static class DigestMessageBuilder
{
    public static string BuildSubject(int articleCount) =>
        $"RaindropAI — digest du jour ({articleCount} article{(articleCount > 1 ? "s" : string.Empty)})";

    public static string BuildHtmlBody(IReadOnlyList<ClassifiedArticle> articles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<html><body>");

        foreach (var categoryGroup in articles.GroupBy(a => a.Classification.Category).OrderBy(g => g.Key.ToString()))
        {
            builder.AppendLine($"<h2>{categoryGroup.Key}</h2>");

            foreach (var actionGroup in categoryGroup.GroupBy(a => a.Classification.Action).OrderBy(g => g.Key.ToString()))
            {
                builder.AppendLine($"<h3>{actionGroup.Key}</h3><ul>");

                foreach (var article in actionGroup.OrderByDescending(a => a.Classification.Priority))
                {
                    builder.AppendLine(
                        $"<li><a href=\"{article.Item.Link}\">{WebUtility.HtmlEncode(article.Item.Title)}</a> " +
                        $"— {article.Classification.Priority} — {WebUtility.HtmlEncode(article.Classification.Reason)}</li>");
                }

                builder.AppendLine("</ul>");
            }
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }
}
