using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

public sealed class PollingStateRepository : IPollingStateRepository
{
    private const int SingleRowId = 1;

    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public PollingStateRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task<PollingState> GetAsync(CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.PollingStates.FindAsync([SingleRowId], cancellationToken);
        if (entity is null)
        {
            return PollingState.Initial;
        }

        return new PollingState(entity.LastRaindropId, entity.LastCreatedUtc, entity.UpdatedAtUtc);
    }

    public async Task UpdateAsync(PollingState state, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.PollingStates.FindAsync([SingleRowId], cancellationToken);
        if (entity is null)
        {
            entity = new PollingStateEntity { Id = SingleRowId };
            context.PollingStates.Add(entity);
        }

        entity.LastRaindropId = state.LastRaindropId;
        entity.LastCreatedUtc = state.LastCreatedUtc;
        entity.UpdatedAtUtc = state.UpdatedAtUtc;

        await context.SaveChangesAsync(cancellationToken);
    }
}
