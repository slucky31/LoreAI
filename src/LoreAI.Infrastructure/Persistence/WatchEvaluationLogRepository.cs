using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;

namespace LoreAI.Infrastructure.Persistence;

public sealed class WatchEvaluationLogRepository : IWatchEvaluationLogRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public WatchEvaluationLogRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task RecordAsync(string rawResponse, DateTimeOffset processedAtUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.WatchEvaluationLogs.Add(new WatchEvaluationLogEntity
        {
            ProcessedAtUtc = processedAtUtc,
            RawResponse = NormalizeToJson(rawResponse),
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetRawResponsesSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.WatchEvaluationLogs
            .Where(e => e.ProcessedAtUtc >= sinceUtc)
            .Select(e => e.RawResponse)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Même garde-fou que <c>EmailExtractionLogRepository</c> : la colonne est un vrai jsonb, un corps de repli vide ou non-JSON ne doit pas faire échouer l'insertion.</summary>
    private static string NormalizeToJson(string rawResponse)
    {
        try
        {
            using var _ = JsonDocument.Parse(rawResponse);
            return rawResponse;
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(rawResponse);
        }
    }
}
