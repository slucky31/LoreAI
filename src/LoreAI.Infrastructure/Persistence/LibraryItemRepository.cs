using System.Globalization;
using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

public sealed class LibraryItemRepository : ILibraryItemRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public LibraryItemRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    /// <summary>
    /// Une requête <c>WHERE Id IN (...)</c> puis un seul <c>SaveChangesAsync</c> pour toute la page —
    /// pas un aller-retour par item (cf. <c>ArticleRepository.UpsertAsync</c>) : à l'échelle de plusieurs
    /// milliers d'items sur un Raspberry Pi (lot 1, #42), ce serait trop lent.
    /// </summary>
    public async Task UpsertPageAsync(IReadOnlyList<LibraryItem> items, DateTimeOffset indexedAtUtc, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var ids = items.Select(i => long.Parse(i.Item.SourceId, CultureInfo.InvariantCulture)).ToList();
        var existing = await context.LibraryItems
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        foreach (var libraryItem in items)
        {
            var id = long.Parse(libraryItem.Item.SourceId, CultureInfo.InvariantCulture);
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = new LibraryItemEntity { Id = id, SourceType = string.Empty, Title = string.Empty, Url = string.Empty, Origin = string.Empty };
                context.LibraryItems.Add(entity);
            }

            entity.SourceType = libraryItem.Item.SourceType.ToString();
            entity.Title = libraryItem.Item.Title;
            entity.Url = libraryItem.Item.Url;
            entity.Excerpt = libraryItem.Item.Excerpt;
            entity.Note = libraryItem.Item.Note;
            entity.Tags = [.. libraryItem.Item.Tags];
            entity.CapturedAtUtc = libraryItem.Item.CapturedAtUtc;
            entity.Origin = libraryItem.Origin.ToString();
            entity.RaindropCollectionId = libraryItem.RaindropCollectionId;
            entity.Broken = libraryItem.Broken;
            entity.Important = libraryItem.Important;
            entity.Cover = libraryItem.Cover;
            entity.HighlightsJson = libraryItem.HighlightsJson;
            entity.IndexedAtUtc = indexedAtUtc;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryItemSummary>> GetAllForInsightsAsync(CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.LibraryItems
            .Select(e => new LibraryItemSummary(e.Id, e.Title, e.Url, e.Tags, e.RaindropCollectionId, e.CapturedAtUtc))
            .ToListAsync(cancellationToken);
    }
}
