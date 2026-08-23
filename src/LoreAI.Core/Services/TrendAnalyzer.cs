using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// S3 : domaines et tags dominants. Pur SQL en amont selon la roadmap — ici, agrégation en mémoire sur
/// une liste déjà filtrée à la fenêtre voulue par l'appelant (<c>WeeklyInsightsJob</c>) ; volontairement
/// agnostique de la fenêtre pour rester testable sans horloge.
/// </summary>
public static class TrendAnalyzer
{
    private const int DefaultTop = 10;

    public static IReadOnlyList<DomainTrend> TopDomains(IReadOnlyList<LibraryItemSummary> items, int top = DefaultTop) =>
        items
            .Select(i => ExtractDomain(i.Url))
            .Where(d => d is not null)
            .GroupBy(d => d!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DomainTrend(g.Key, g.Count()))
            .OrderByDescending(d => d.Count)
            .ThenBy(d => d.Domain, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();

    public static IReadOnlyList<TagTrend> TopTags(IReadOnlyList<LibraryItemSummary> items, int top = DefaultTop) =>
        items
            .SelectMany(i => i.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TagTrend(g.First(), g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();

    private static string? ExtractDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }
}
