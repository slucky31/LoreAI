using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;

namespace LoreAI.Infrastructure.Persistence;

public sealed class EmailExtractionLogRepository : IEmailExtractionLogRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public EmailExtractionLogRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task RecordAsync(string rawResponse, DateTimeOffset processedAtUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        context.EmailExtractionLogs.Add(new EmailExtractionLogEntity
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

        return await context.EmailExtractionLogs
            .Where(e => e.ProcessedAtUtc >= sinceUtc)
            .Select(e => e.RawResponse)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Même garde-fou que <c>ArticleRepository</c> : la colonne est un vrai jsonb, un corps de repli vide ou non-JSON ne doit pas faire échouer l'insertion.</summary>
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
