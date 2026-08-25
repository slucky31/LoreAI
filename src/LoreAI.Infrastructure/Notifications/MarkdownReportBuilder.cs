using System.Globalization;
using System.Text;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>Met en forme un <see cref="WeeklyInsightsReport"/> en Markdown — pur, testable sans envoi Discord.</summary>
public static class MarkdownReportBuilder
{
    public static string Build(WeeklyInsightsReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Rapport hebdomadaire LoreAI — {report.GeneratedAtUtc:yyyy-MM-dd}");
        builder.AppendLine();

        AppendDuplicates(builder, report.DuplicateUrls);
        AppendTagHygiene(builder, report.TagHygiene);
        AppendUnbalancedCollections(builder, report.UnbalancedCollections);
        AppendTrends(builder, report.TopDomains, report.TopTags);
        AppendLlmUsage(builder, report.LlmUsage);
        AppendBrokenTrackedArticles(builder, report.BrokenTrackedArticles);
        AppendStaleArticles(builder, report.StaleArticles);
        AppendReadingQueue(builder, report.ReadingQueue);

        return builder.ToString();
    }

    private static void AppendBrokenTrackedArticles(StringBuilder builder, IReadOnlyList<BrokenTrackedArticle> articles)
    {
        builder.AppendLine("## Liens morts parmi les articles suivis (N3)");
        builder.AppendLine();

        if (articles.Count == 0)
        {
            builder.AppendLine("Aucun.");
            builder.AppendLine();
            return;
        }

        foreach (var article in articles)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- [{article.Title}]({article.Url}) — {article.LinkStatus}");
        }

        builder.AppendLine();
    }

    private static void AppendStaleArticles(StringBuilder builder, IReadOnlyList<StaleArticle> articles)
    {
        builder.AppendLine("## Articles périmés — proposition de purge (N4)");
        builder.AppendLine();

        if (articles.Count == 0)
        {
            builder.AppendLine("Aucun.");
            builder.AppendLine();
            return;
        }

        foreach (var article in articles)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- [{article.Title}]({article.Url}) — {article.DaysSinceCaptured} jours sans traitement");
        }

        builder.AppendLine();
    }

    private static void AppendReadingQueue(StringBuilder builder, IReadOnlyList<ReadingQueueEntry> entries)
    {
        builder.AppendLine("## File de lecture (L1)");
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("Rien à lire cette semaine.");
            builder.AppendLine();
            return;
        }

        foreach (var entry in entries)
        {
            var readingTime = entry.EstimatedMinutes is int minutes ? $"{minutes} min" : "durée inconnue";
            builder.AppendLine(CultureInfo.InvariantCulture, $"- [{entry.Title}]({entry.Url}) — {entry.Priority}, {readingTime}");
        }

        builder.AppendLine();
    }

    private static void AppendDuplicates(StringBuilder builder, IReadOnlyList<DuplicateUrlGroup> groups)
    {
        builder.AppendLine("## Doublons d'URL (N1)");
        builder.AppendLine();

        if (groups.Count == 0)
        {
            builder.AppendLine("Aucun doublon détecté.");
            builder.AppendLine();
            return;
        }

        foreach (var group in groups)
        {
            var links = string.Join(", ", group.Items.Select(i => $"[{i.Title}]({i.Url})"));
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {links}");
        }

        builder.AppendLine();
    }

    private static void AppendTagHygiene(StringBuilder builder, TagHygieneResult hygiene)
    {
        builder.AppendLine("## Hygiène des tags (N2)");
        builder.AppendLine();

        builder.AppendLine("### Grappes de tags proches");
        if (hygiene.Clusters.Count == 0)
        {
            builder.AppendLine("Aucune grappe détectée.");
        }
        else
        {
            foreach (var cluster in hygiene.Clusters)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {string.Join(" / ", cluster.Tags)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("### Tags utilisés une seule fois");
        builder.AppendLine(hygiene.SingleUseTags.Count == 0
            ? "Aucun."
            : string.Join(", ", hygiene.SingleUseTags));
        builder.AppendLine();
    }

    private static void AppendUnbalancedCollections(StringBuilder builder, IReadOnlyList<UnbalancedCollection> collections)
    {
        builder.AppendLine("## Collections déséquilibrées (N5)");
        builder.AppendLine();

        if (collections.Count == 0)
        {
            builder.AppendLine("Aucune collection à 1-2 items.");
            builder.AppendLine();
            return;
        }

        foreach (var collection in collections)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {collection.Title} — {collection.ItemCount} item{(collection.ItemCount > 1 ? "s" : string.Empty)}");
        }

        builder.AppendLine();
    }

    private static void AppendTrends(StringBuilder builder, IReadOnlyList<DomainTrend> domains, IReadOnlyList<TagTrend> tags)
    {
        builder.AppendLine("## Tendances (S3)");
        builder.AppendLine();

        builder.AppendLine("### Domaines dominants");
        builder.AppendLine(domains.Count == 0
            ? "Aucune donnée sur la période."
            : string.Join('\n', domains.Select(d => FormattableString.Invariant($"- {d.Domain} — {d.Count}"))));

        builder.AppendLine();
        builder.AppendLine("### Tags dominants");
        builder.AppendLine(tags.Count == 0
            ? "Aucune donnée sur la période."
            : string.Join('\n', tags.Select(t => FormattableString.Invariant($"- {t.Tag} — {t.Count}"))));
        builder.AppendLine();
    }

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

    private static void AppendLlmUsage(StringBuilder builder, LlmUsageSummary usage)
    {
        builder.AppendLine("## Coût LLM (S6)");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Classifications ce mois-ci : {usage.ClassificationCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Tokens entrée / sortie : {usage.InputTokens} / {usage.OutputTokens}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Tokens cache (création / lecture) : {usage.CacheCreationInputTokens} / {usage.CacheReadInputTokens}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- Coût estimé (entrée/sortie, tarifs Claude Haiku 4.5) : ~{usage.EstimatedCostUsd.ToString("0.####", CultureInfo.InvariantCulture)} $");
    }
}
