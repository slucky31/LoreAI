using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface ILibraryItemRepository
{
    /// <summary>Insère ou remplace une page d'items indexés en une seule transaction — idempotent sur Item.SourceId.</summary>
    Task UpsertPageAsync(IReadOnlyList<LibraryItem> items, DateTimeOffset indexedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Toute la bibliothèque indexée, en projection allégée (<see cref="LibraryItemSummary"/>) — alimente
    /// les insights hebdomadaires (N1/N2/N5/S3, #43). Pas de pagination : la volumétrie visée (milliers
    /// d'items) tient en mémoire sur un Pi une fois débarrassée des champs lourds (highlights, note...).
    /// </summary>
    Task<IReadOnlyList<LibraryItemSummary>> GetAllForInsightsAsync(CancellationToken cancellationToken);
}
