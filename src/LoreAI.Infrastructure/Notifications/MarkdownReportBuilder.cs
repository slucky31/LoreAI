using System.Globalization;
using System.Text;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>Formatte les rapports narratifs/fiches en Markdown — pur, testable sans envoi Discord. Le rapport hebdomadaire d'insights est passé aux embeds Discord natifs depuis O6 (#78, <see cref="DiscordWeeklyDigestNotifier"/>), plus à cette classe.</summary>
public static class MarkdownReportBuilder
{
    /// <summary>Revue mensuelle narrative (S4, lot 5) — un thème par section, narration puis articles source.</summary>
    public static string BuildMonthlyReview(MonthlyReviewReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Revue mensuelle LoreAI — {report.PeriodStartUtc:yyyy-MM}");
        builder.AppendLine();

        foreach (var theme in report.Themes)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"## {theme.Theme}");
            builder.AppendLine();
            builder.AppendLine(theme.Narrative);
            builder.AppendLine();

            foreach (var article in theme.Articles)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- [{article.Title}]({article.Url})");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Fiche Markdown d'un outil (S7, lot 5) — frontmatter YAML + articles liés. Régénérée à chaque appel,
    /// jamais éditée à la main dans Obsidian : voir « Le pont Obsidian » du roadmap. Les annotations
    /// humaines, si besoin, vivent dans un fichier voisin que cette projection ne touche jamais.
    /// </summary>
    public static string BuildToolCard(ToolCard card)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine(CultureInfo.InvariantCulture, $"name: {card.Name}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"category: {card.Category ?? "—"}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"status: {card.Status}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"verdict: {card.Verdict ?? "(à déterminer)"}");
        if (!string.IsNullOrEmpty(card.Url))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"url: {card.Url}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"first_seen: {card.FirstSeenAtUtc:yyyy-MM-dd}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"last_seen: {card.LastSeenAtUtc:yyyy-MM-dd}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# {card.Name}");
        builder.AppendLine();
        builder.AppendLine("## Articles liés");
        builder.AppendLine();

        if (card.RelatedArticles.Count == 0)
        {
            builder.AppendLine("Aucun.");
        }
        else
        {
            foreach (var article in card.RelatedArticles)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- [{article.Title}]({article.Url}){(string.IsNullOrEmpty(article.Summary) ? string.Empty : $" — {article.Summary}")}");
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Export Markdown d'un item du corpus (S8, lot 5) — frontmatter YAML + résumé, à la demande via l'outil
    /// MCP <c>export_item</c>. Même remarque « régénérée, jamais éditée » que <see cref="BuildToolCard"/>.
    /// </summary>
    public static string BuildItemExport(LibraryItemSummary item, string? summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine(CultureInfo.InvariantCulture, $"title: {item.Title}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"url: {item.Url}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"tags: [{string.Join(", ", item.Tags)}]");
        builder.AppendLine(CultureInfo.InvariantCulture, $"raindrop_collection_id: {item.RaindropCollectionId?.ToString(CultureInfo.InvariantCulture) ?? "—"}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"captured: {item.CapturedAtUtc:yyyy-MM-dd}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# {item.Title}");
        builder.AppendLine();
        builder.AppendLine("## Résumé");
        builder.AppendLine();
        builder.AppendLine(summary ?? "Jamais classifié — pas de résumé disponible.");

        return builder.ToString();
    }
}
