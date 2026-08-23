using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

public sealed class PollingStateRepository : IPollingStateRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public PollingStateRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task<PollingState> GetAsync(SourceType sourceType, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.PollingStates.FindAsync([sourceType.ToString()], cancellationToken);
        if (entity is null)
        {
            return PollingState.Initial(sourceType);
        }

        return new PollingState(sourceType, entity.LastSourceItemId, entity.LastCreatedUtc, entity.UpdatedAtUtc);
    }

    public async Task UpdateAsync(PollingState state, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var key = state.SourceType.ToString();
        var entity = await context.PollingStates.FindAsync([key], cancellationToken);
        if (entity is null)
        {
            entity = new PollingStateEntity { SourceType = key };
            context.PollingStates.Add(entity);
        }

        entity.LastSourceItemId = state.LastSourceItemId;
        entity.LastCreatedUtc = state.LastCreatedUtc;
        entity.UpdatedAtUtc = state.UpdatedAtUtc;

        await context.SaveChangesAsync(cancellationToken);
    }
}
