using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// N'appelle jamais <see cref="PostgresSchemaGuard"/> (voir <see cref="ICorpusQueryRepository"/>) : conçu
/// pour tourner avec le rôle <c>loreai_ro</c> (ADR 0009, ADR 0014), qui n'a pas les privilèges de
/// migration. Le schéma est garanti à jour par le Worker, seul propriétaire de la base.
/// </summary>
public sealed class CorpusQueryRepository : ICorpusQueryRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;

    public CorpusQueryRepository(IDbContextFactory<LoreAiDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<LibraryItemSummary?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.LibraryItems
            .Where(i => i.Id == id)
            .Select(ToSummary)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryItemSummary>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.LibraryItems
            .OrderByDescending(i => i.CapturedAtUtc)
            .Take(count)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// <c>ILIKE '%terme%'</c> plutôt qu'un <c>tsvector</c>/index GIN (Q2 de la roadmap) : le volume actuel
    /// ne justifie pas encore ce changement de schéma, et ce n'est pas un prérequis du squelette du lot 3.
    /// </summary>
    public async Task<IReadOnlyList<LibraryItemSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var pattern = $"%{query}%";
        return await context.LibraryItems
            .Where(i => EF.Functions.ILike(i.Title, pattern) || EF.Functions.ILike(i.Url, pattern))
            .OrderByDescending(i => i.CapturedAtUtc)
            .Take(limit)
            .Select(ToSummary)
            .ToListAsync(cancellationToken);
    }

    public async Task<CorpusStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var totalItems = await context.LibraryItems.CountAsync(cancellationToken);
        var importantItems = await context.LibraryItems.CountAsync(i => i.Important, cancellationToken);
        var brokenItems = await context.LibraryItems.CountAsync(i => i.Broken, cancellationToken);
        var lastIndexedAtUtc = await context.LibraryItems.MaxAsync(i => (DateTimeOffset?)i.IndexedAtUtc, cancellationToken);

        return new CorpusStats(totalItems, importantItems, brokenItems, lastIndexedAtUtc);
    }

    private static readonly System.Linq.Expressions.Expression<Func<LibraryItemEntity, LibraryItemSummary>> ToSummary =
        i => new LibraryItemSummary(i.Id, i.Title, i.Url, i.Tags, i.RaindropCollectionId, i.CapturedAtUtc);
}
