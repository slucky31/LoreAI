using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>
/// Remplace la pièce jointe Markdown du rapport hebdomadaire par deux messages Discord natifs — embeds
/// (O6, #78) : constaté peu pratique sur mobile (ouverture/téléchargement du fichier requis), et une
/// section à fort volume (ex. articles périmés) décourageait plus qu'elle n'encourageait à agir. Chaque
/// section est plafonnée à <see cref="MaxEntriesPerField"/> entrées + un total, réparties en deux messages :
/// actionnable (file de lecture, liens morts, articles périmés) puis hygiène/stats (doublons, tags,
/// collections déséquilibrées, tendances, coût LLM). Même philosophie « n'échoue jamais bruyamment » que
/// <see cref="DiscordReportNotifier"/>/<see cref="DiscordCycleReportNotifier"/>.
/// </summary>
public sealed class DiscordWeeklyDigestNotifier : IWeeklyDigestNotifier
{
    private const int MaxEntriesPerField = 10;
    private const int ActionableColor = 0x2ECC71;
    private const int HygieneColor = 0x3498DB;

    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordWeeklyDigestNotifier> _logger;

    public DiscordWeeklyDigestNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordWeeklyDigestNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendDigestAsync(WeeklyInsightsReport report, CancellationToken cancellationToken)
    {
        await SendEmbedAsync(BuildActionableEmbed(report), cancellationToken);
        await SendEmbedAsync(BuildHygieneEmbed(report), cancellationToken);
    }

    private async Task SendEmbedAsync(object embed, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, new { embeds = new[] { embed } }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Échec de l'envoi du digest hebdomadaire sur Discord.");
        }
    }

    private static object BuildActionableEmbed(WeeklyInsightsReport report) => new
    {
        title = $"Rapport hebdomadaire LoreAI — actionnable ({report.GeneratedAtUtc:yyyy-MM-dd})",
        color = ActionableColor,
        fields = new[]
        {
            new { name = "File de lecture (L1)", value = BuildSection(report.ReadingQueue, FormatReadingQueueEntry) },
            new { name = "Liens morts (N3)", value = BuildSection(report.BrokenTrackedArticles, FormatBrokenArticle) },
            new { name = "Articles périmés (N4)", value = BuildSection(report.StaleArticles, FormatStaleArticle) },
        },
    };

    private static object BuildHygieneEmbed(WeeklyInsightsReport report) => new
    {
        title = $"Rapport hebdomadaire LoreAI — hygiène/stats ({report.GeneratedAtUtc:yyyy-MM-dd})",
        color = HygieneColor,
        fields = new[]
        {
            new { name = "Doublons d'URL (N1)", value = BuildSection(report.DuplicateUrls, FormatDuplicateGroup) },
            new { name = "Hygiène des tags (N2)", value = BuildTagHygieneSection(report.TagHygiene) },
            new { name = "Collections déséquilibrées (N5)", value = BuildSection(report.UnbalancedCollections, FormatUnbalancedCollection) },
            new { name = "Tendances (S3)", value = BuildTrendsSection(report.TopDomains, report.TopTags) },
            new { name = "Coût LLM (S6)", value = FormatLlmUsage(report.LlmUsage) },
        },
    };

    private static string BuildSection<T>(IReadOnlyList<T> items, Func<T, string> format)
    {
        if (items.Count == 0)
        {
            return "Aucun.";
        }

        var lines = items.Take(MaxEntriesPerField).Select(format);
        var text = string.Join("\n", lines);

        return items.Count > MaxEntriesPerField
            ? $"{text}\n_et {(items.Count - MaxEntriesPerField).ToString(CultureInfo.InvariantCulture)} de plus_"
            : text;
    }

    private static string FormatReadingQueueEntry(ReadingQueueEntry entry)
    {
        var readingTime = entry.EstimatedMinutes is int minutes ? $"{minutes.ToString(CultureInfo.InvariantCulture)} min" : "durée inconnue";
        return $"[{entry.Title}]({entry.Url}) — {entry.Priority}, {readingTime}";
    }

    private static string FormatBrokenArticle(BrokenTrackedArticle article) => $"[{article.Title}]({article.Url}) — {article.LinkStatus}";

    private static string FormatStaleArticle(StaleArticle article) =>
        $"[{article.Title}]({article.Url}) — {article.DaysSinceCaptured.ToString(CultureInfo.InvariantCulture)} jours sans traitement";

    private static string FormatDuplicateGroup(DuplicateUrlGroup group) =>
        string.Join(", ", group.Items.Select(i => $"[{i.Title}]({i.Url})"));

    private static string FormatUnbalancedCollection(UnbalancedCollection collection) =>
        $"{collection.Title} — {collection.ItemCount.ToString(CultureInfo.InvariantCulture)} item{(collection.ItemCount > 1 ? "s" : string.Empty)}";

    private static string BuildTagHygieneSection(TagHygieneResult hygiene)
    {
        if (hygiene.Clusters.Count == 0 && hygiene.SingleUseTags.Count == 0)
        {
            return "Aucun.";
        }

        var clusterLines = hygiene.Clusters
            .Take(MaxEntriesPerField)
            .Select(cluster => string.Join(" / ", cluster.Tags));
        var text = string.Join("\n", clusterLines);

        if (hygiene.Clusters.Count > MaxEntriesPerField)
        {
            text += $"\n_et {(hygiene.Clusters.Count - MaxEntriesPerField).ToString(CultureInfo.InvariantCulture)} grappes de plus_";
        }

        if (hygiene.SingleUseTags.Count > 0)
        {
            var singleUsePreview = string.Join(", ", hygiene.SingleUseTags.Take(MaxEntriesPerField));
            var suffix = hygiene.SingleUseTags.Count > MaxEntriesPerField
                ? $" (+{(hygiene.SingleUseTags.Count - MaxEntriesPerField).ToString(CultureInfo.InvariantCulture)} de plus)"
                : string.Empty;
            var singleUseLine = $"Tags à usage unique : {singleUsePreview}{suffix}";
            text = text.Length == 0 ? singleUseLine : $"{text}\n{singleUseLine}";
        }

        return text;
    }

    private static string BuildTrendsSection(IReadOnlyList<DomainTrend> domains, IReadOnlyList<TagTrend> tags)
    {
        if (domains.Count == 0 && tags.Count == 0)
        {
            return "Aucune donnée sur la période.";
        }

        const int MaxPerTrendKind = 5;
        var domainLines = domains.Take(MaxPerTrendKind).Select(d => $"{d.Domain} — {d.Count.ToString(CultureInfo.InvariantCulture)}");
        var tagLines = tags.Take(MaxPerTrendKind).Select(t => $"{t.Tag} — {t.Count.ToString(CultureInfo.InvariantCulture)}");

        var parts = new List<string>();
        if (domains.Count > 0)
        {
            parts.Add($"**Domaines** : {string.Join(", ", domainLines)}");
        }

        if (tags.Count > 0)
        {
            parts.Add($"**Tags** : {string.Join(", ", tagLines)}");
        }

        return string.Join("\n", parts);
    }

    private static string FormatLlmUsage(LlmUsageSummary usage) =>
        $"Classifications ce mois-ci : {usage.ClassificationCount.ToString(CultureInfo.InvariantCulture)}\n" +
        $"Tokens entrée/sortie : {usage.InputTokens.ToString(CultureInfo.InvariantCulture)} / {usage.OutputTokens.ToString(CultureInfo.InvariantCulture)}\n" +
        $"Coût estimé : ~{usage.EstimatedCostUsd.ToString("0.####", CultureInfo.InvariantCulture)} $";
}
