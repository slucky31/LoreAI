using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// N4 (lot 6) : articles « À lire » jamais traités (<c>HumanHandledAtUtc</c> nul, L3) au-delà du seuil
/// de péremption. Proposition seule, jamais de suppression automatique. Pure — aucun appel réseau.
/// </summary>
public static class StaleArticlesAnalyzer
{
    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromDays(90);

    public static IReadOnlyList<StaleArticle> Detect(IReadOnlyList<TrackedArticle> articles, DateTimeOffset now)
    {
        return articles
            .Where(a => a.Action == RecommendedAction.ALire && a.HumanHandledAtUtc is null && now - a.CapturedAtUtc >= StalenessThreshold)
            .Select(a => new StaleArticle(a.Id, a.Title, a.Url, (int)(now - a.CapturedAtUtc).TotalDays))
            .OrderByDescending(a => a.DaysSinceCaptured)
            .ToList();
    }
}
