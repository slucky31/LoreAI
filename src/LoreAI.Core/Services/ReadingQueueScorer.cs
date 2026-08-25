using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// L1 (lot 6) : file de lecture scorée sur des données enfin complètes — priorité × fraîcheur × temps
/// de lecture, filtrée aux articles non traités (<c>HumanHandledAtUtc</c> nul, L3) et pas supprimés.
/// Remplace le filet perdu à la suppression du digest email (lot 2, D3) : « lis ça » plutôt que « voici
/// tout ». Pure — aucun appel réseau.
/// </summary>
public static class ReadingQueueScorer
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromDays(90);

    public static IReadOnlyList<ReadingQueueEntry> Score(IReadOnlyList<TrackedArticle> articles, DateTimeOffset now, int limit)
    {
        return articles
            .Where(a => a.HumanHandledAtUtc is null && a.LinkStatus != LinkStatus.Deleted)
            .Select(a => ToEntry(a, now))
            .OrderByDescending(e => e.Score)
            .Take(limit)
            .ToList();
    }

    private static ReadingQueueEntry ToEntry(TrackedArticle article, DateTimeOffset now)
    {
        var estimatedMinutes = ReadingTimeEstimator.EstimateMinutes(article.WordCount);
        var score = ComputeScore(article, now, estimatedMinutes);
        return new ReadingQueueEntry(article.Id, article.Title, article.Url, score, estimatedMinutes, article.Priority, article.CapturedAtUtc);
    }

    private static double ComputeScore(TrackedArticle article, DateTimeOffset now, int? estimatedMinutes)
    {
        var priorityWeight = article.Priority switch
        {
            Priority.Haute => 3.0,
            Priority.Moyenne => 2.0,
            _ => 1.0,
        };

        var daysSinceCaptured = (now - article.CapturedAtUtc).TotalDays;
        var freshnessFactor = Math.Max(0.0, 1.0 - daysSinceCaptured / FreshnessWindow.TotalDays);

        // Lecture courte légèrement favorisée ; temps de lecture inconnu ni pénalisé ni avantagé.
        var readingTimeFactor = estimatedMinutes is int minutes ? 30.0 / (30.0 + minutes) : 0.7;

        return priorityWeight * freshnessFactor * readingTimeFactor;
    }
}
