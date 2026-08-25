using Microsoft.EntityFrameworkCore;
using LoreAI.Core.Interfaces;

namespace LoreAI.Infrastructure.Persistence;

public sealed class ToolRepository : IToolRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;

    public ToolRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
    }

    public async Task UpsertFromArticleAsync(string name, string? category, string? url, long articleId, DateTimeOffset seenAtUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.Tools.SingleOrDefaultAsync(t => EF.Functions.ILike(t.Name, name), cancellationToken);
        if (existing is null)
        {
            context.Tools.Add(new ToolEntity
            {
                Name = name,
                Category = category,
                Url = url,
                RelatedArticleIds = [articleId],
                FirstSeenAtUtc = seenAtUtc,
                LastSeenAtUtc = seenAtUtc,
            });
        }
        else
        {
            // Statut/verdict jamais touchés ici : champs manuels/futurs, cf. IToolRepository.
            existing.LastSeenAtUtc = seenAtUtc;
            if (!existing.RelatedArticleIds.Contains(articleId))
            {
                existing.RelatedArticleIds = [.. existing.RelatedArticleIds, articleId];
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
