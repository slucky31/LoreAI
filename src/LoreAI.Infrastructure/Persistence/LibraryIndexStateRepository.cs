using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

public sealed class LibraryIndexStateRepository : ILibraryIndexStateRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public LibraryIndexStateRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task<LibraryIndexState> GetAsync(SourceType sourceType, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await context.LibraryIndexStates.FindAsync([sourceType.ToString()], cancellationToken);
        if (entity is null)
        {
            return LibraryIndexState.Initial(sourceType);
        }

        return new LibraryIndexState(sourceType, entity.ResumePage, entity.LastFullPassStartedUtc, entity.LastFullPassCompletedUtc, entity.UpdatedAtUtc);
    }

    public async Task UpdateAsync(LibraryIndexState state, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var key = state.SourceType.ToString();
        var entity = await context.LibraryIndexStates.FindAsync([key], cancellationToken);
        if (entity is null)
        {
            entity = new LibraryIndexStateEntity { SourceType = key };
            context.LibraryIndexStates.Add(entity);
        }

        entity.ResumePage = state.ResumePage;
        entity.LastFullPassStartedUtc = state.LastFullPassStartedUtc;
        entity.LastFullPassCompletedUtc = state.LastFullPassCompletedUtc;
        entity.UpdatedAtUtc = state.UpdatedAtUtc;

        await context.SaveChangesAsync(cancellationToken);
    }
}
