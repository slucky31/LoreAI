using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

public sealed class WatchTopicRepository : IWatchTopicRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public WatchTopicRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task<IReadOnlyList<WatchTopic>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.WatchTopics
            .Select(t => new WatchTopic(t.Id, t.Name, t.Description, t.MinifluxCategoryId, t.RaindropCollectionId, t.LastMinifluxEntryId, t.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<long> AddAsync(WatchTopic topic, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new WatchTopicEntity
        {
            Name = topic.Name,
            Description = topic.Description,
            MinifluxCategoryId = topic.MinifluxCategoryId,
            RaindropCollectionId = topic.RaindropCollectionId,
            LastMinifluxEntryId = topic.LastMinifluxEntryId,
            CreatedAtUtc = topic.CreatedAtUtc,
        };

        context.WatchTopics.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task UpdateCursorAsync(long topicId, string lastMinifluxEntryId, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.WatchTopics.SingleOrDefaultAsync(t => t.Id == topicId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.LastMinifluxEntryId = lastMinifluxEntryId;
        await context.SaveChangesAsync(cancellationToken);
    }
}
