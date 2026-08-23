using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// N5 : collections à 1 ou 2 items. Pure — les titres sont résolus par l'appelant (<c>WeeklyInsightsJob</c>,
/// via la taxonomie apprise) et fournis ici en dictionnaire, pas via un appel réseau.
/// <c>collectionTitles</c> sert aussi de filtre implicite : « Non trié » (id -1) et tout id sans
/// correspondance dans la taxonomie réelle (item non classé dans une collection nommée) sont ignorés,
/// ce ne sont pas des collections déséquilibrées au sens de ce scénario.
/// </summary>
public static class CollectionBalanceAnalyzer
{
    private const int MaxUnbalancedItemCount = 2;

    public static IReadOnlyList<UnbalancedCollection> Detect(
        IReadOnlyList<LibraryItemSummary> items,
        IReadOnlyDictionary<long, string> collectionTitles)
    {
        return items
            .Where(i => i.RaindropCollectionId.HasValue && collectionTitles.ContainsKey(i.RaindropCollectionId.Value))
            .GroupBy(i => i.RaindropCollectionId!.Value)
            .Where(g => g.Count() <= MaxUnbalancedItemCount)
            .Select(g => new UnbalancedCollection(collectionTitles[g.Key], g.Count()))
            .OrderBy(c => c.ItemCount)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
