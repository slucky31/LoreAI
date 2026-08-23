using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface ILibraryItemRepository
{
    /// <summary>Insère ou remplace une page d'items indexés en une seule transaction — idempotent sur Item.SourceId.</summary>
    Task UpsertPageAsync(IReadOnlyList<LibraryItem> items, DateTimeOffset indexedAtUtc, CancellationToken cancellationToken);
}
