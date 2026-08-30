using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Lit les nouvelles entrées d'une catégorie Miniflux donnée (lot 9, #50) — contrairement à
/// <see cref="ISourceIngester"/>, le curseur n'est pas propre à une <c>SourceType</c> partagée mais à un
/// sujet de veille (<see cref="WatchTopic.LastMinifluxEntryId"/>), géré par l'appelant
/// (<c>TopicWatchJob</c>/<see cref="IWatchTopicRepository"/>) plutôt que par cette classe elle-même.
/// </summary>
public interface IMinifluxCategoryReader
{
    /// <summary>Du plus ancien au plus récent. <paramref name="afterEntryId"/> ne doit jamais être <c>null</c> — un sujet fraîchement provisionné est seedé à <c>"0"</c> (catégorie vide, jamais de backfill à craindre).</summary>
    Task<(IReadOnlyList<Item> Items, string? LastEntryId)> GetNewEntriesAsync(int categoryId, string afterEntryId, CancellationToken cancellationToken);
}
