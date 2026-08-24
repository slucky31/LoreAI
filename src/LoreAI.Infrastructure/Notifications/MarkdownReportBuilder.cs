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

        return builder.ToString();
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
