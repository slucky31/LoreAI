using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// N3 (lot 6) : liens morts ou supprimés parmi les articles suivis par le pipeline — une vue plus
/// étroite et actionnable que <c>LibraryItems.Broken</c> (toute la bibliothèque), alimentée par
/// <c>LinkStatus</c> (L3). Pure — aucun appel réseau.
/// </summary>
public static class BrokenTrackedArticlesAnalyzer
{
    public static IReadOnlyList<BrokenTrackedArticle> Detect(IReadOnlyList<TrackedArticle> articles)
    {
        return articles
            .Where(a => a.LinkStatus is LinkStatus.Broken or LinkStatus.Deleted)
            .Select(a => new BrokenTrackedArticle(a.Id, a.Title, a.Url, a.LinkStatus!.Value))
            .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
