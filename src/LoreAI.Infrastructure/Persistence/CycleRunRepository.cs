using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

public sealed class CycleRunRepository : ICycleRunRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public CycleRunRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task RecordAsync(CycleRun run, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.CycleRuns.Add(new CycleRunEntity
        {
            StartedUtc = run.StartedUtc,
            CompletedUtc = run.CompletedUtc,
            Outcome = run.Outcome,
            ItemsSeen = run.ItemsSeen,
            ItemsProcessed = run.ItemsProcessed,
            Moved = run.Moved,
            TagsApplied = run.TagsApplied,
            Notified = run.Notified,
            FailureReason = run.FailureReason,
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CycleRun>> GetRecentAsync(int count, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.CycleRuns
            .OrderByDescending(c => c.CompletedUtc)
            .Take(count)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new CycleRun(
            e.StartedUtc,
            e.CompletedUtc,
            e.Outcome,
            e.ItemsSeen,
            e.ItemsProcessed,
            e.Moved,
            e.TagsApplied,
            e.Notified,
            e.FailureReason)).ToList();
    }
}
