using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Persistence;

public sealed class ArticleRepository : IArticleRepository
{
    private readonly IDbContextFactory<LoreAiDbContext> _contextFactory;
    private readonly PostgresSchemaGuard _schemaGuard;
    private readonly ILogger<ArticleRepository> _logger;

    public ArticleRepository(IDbContextFactory<LoreAiDbContext> contextFactory, PostgresSchemaGuard schemaGuard, ILogger<ArticleRepository> logger)
    {
        _contextFactory = contextFactory;
        _schemaGuard = schemaGuard;
        _logger = logger;
    }

    public async Task<long> UpsertAsync(Item item, ClassificationResult classification, ContentFetchResult content, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Clé applicative (SourceType, SourceId) depuis le lot 8 (#49) : Id est généré par la base, un lien
        // Newsletter n'ayant pas d'id Raindrop numérique à réutiliser tel quel.
        var entity = await context.Articles
            .SingleOrDefaultAsync(a => a.SourceType == item.SourceType && a.SourceId == item.SourceId, cancellationToken);
        if (entity is null)
        {
            entity = new ArticleEntity { SourceType = item.SourceType, SourceId = item.SourceId, Title = item.Title, Url = item.Url };
            context.Articles.Add(entity);
        }

        entity.Title = item.Title;
        entity.Url = item.Url;
        entity.Excerpt = item.Excerpt;
        entity.Note = item.Note;
        entity.OriginalTags = [.. item.Tags];
        entity.CapturedAtUtc = item.CapturedAtUtc;
        entity.FetchedAtUtc = DateTimeOffset.UtcNow;
        entity.SuggestedCollection = classification.SuggestedCollection;
        entity.SuggestedTags = [.. classification.Tags];
        entity.RecommendedAction = classification.Action;
        entity.Priority = classification.Priority;
        entity.Reason = classification.Reason;
        entity.Summary = classification.Summary;
        entity.IsFallback = classification.IsFallback;
        entity.ToolName = classification.ToolName;
        entity.ToolCategory = classification.ToolCategory;
        entity.ToolUrl = classification.ToolUrl;
        entity.ClassificationModel = classification.Model;
        entity.ClassificationRawResponse = NormalizeToJson(classification.RawResponse);
        entity.ClassifiedAtUtc = classifiedAtUtc;

        entity.ContentText = content.Text;
        entity.ContentStatus = content.Status;
        entity.WordCount = content.WordCount;
        entity.ContentFetchedAtUtc = content.Status == ContentFetchStatus.Skipped ? null : DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    /// <summary>
    /// La colonne est un vrai jsonb (ADR 0011), et un repli de classification peut porter un corps de
    /// réponse vide ou non-JSON (ex. panne de transport avant toute réponse HTTP, cf.
    /// <c>AnthropicClassifier</c>) — l'insertion échouerait sinon avec « invalid input syntax for type
    /// json », exactement le genre de perte silencieuse que <c>ClassificationResult.Fallback</c> existe
    /// pour éviter. Un corps déjà JSON valide (le cas normal) traverse inchangé.
    /// </summary>
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

    public async Task RecordWriteBackAsync(long articleId, bool success, bool moved, long? writeBackCollectionId, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Zéro ligne touchée signalerait un article jamais persisté : l'UPDATE serait sinon parfaitement
        // silencieux, et l'audit du write-back perdrait sa trace sans que rien ne l'indique.
        var affected = await context.Articles
            .Where(a => a.Id == articleId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(a => a.WriteBackStatus, success ? "Done" : "Failed")
                    .SetProperty(a => a.Moved, moved)
                    .SetProperty(a => a.WriteBackCollectionId, writeBackCollectionId)
                    .SetProperty(a => a.WriteBackAtUtc, atUtc),
                cancellationToken);

        if (affected == 0)
        {
            _logger.LogWarning("Write-back non enregistré : aucun article {ArticleId} en base.", articleId);
        }
    }

    public async Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Articles
            .Where(a => a.Id == articleId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.DiscordNotifiedAtUtc, notifiedAtUtc), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetClassificationRawResponsesSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Le "?? string.Empty" est fait après matérialisation, pas traduit en SQL : un COALESCE(jsonb, '')
        // échouerait côté Postgres, la chaîne vide n'étant pas un JSON valide vers lequel caster.
        var rawResponses = await context.Articles
            .Where(a => a.ClassifiedAtUtc != null && a.ClassifiedAtUtc >= sinceUtc)
            .Select(a => a.ClassificationRawResponse)
            .ToListAsync(cancellationToken);

        return rawResponses.Select(r => r ?? string.Empty).ToList();
    }

    public async Task<IReadOnlyList<MonthlyReviewArticle>> GetClassifiedBetweenAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Articles
            .Where(a => !a.IsFallback && a.ClassifiedAtUtc != null && a.ClassifiedAtUtc >= startUtc && a.ClassifiedAtUtc < endUtc)
            .Select(a => new MonthlyReviewArticle(a.Id, a.Title, a.Url, a.SuggestedCollection, a.SuggestedTags, a.Summary, a.Reason, a.Priority))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationCandidate>> GetReconciliationCandidatesAsync(int limit, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Articles
            .Where(a => a.LinkStatus != LinkStatus.Deleted)
            .OrderBy(a => a.LastSeenAtUtc)
            .Take(limit)
            .Select(a => new ReconciliationCandidate(
                a.Id, a.Title, a.Url, a.OriginalTags, a.SuggestedTags, a.WriteBackCollectionId,
                a.RecommendedAction, a.Priority, a.ClassifiedAtUtc, a.HumanHandledAtUtc, a.RemindedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task RecordReconciliationAsync(long articleId, DateTimeOffset lastSeenAtUtc, DateTimeOffset? humanHandledAtUtc, DateTimeOffset? remindedAtUtc, LinkStatus linkStatus, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var affected = await context.Articles
            .Where(a => a.Id == articleId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(a => a.LastSeenAtUtc, lastSeenAtUtc)
                    .SetProperty(a => a.HumanHandledAtUtc, humanHandledAtUtc)
                    .SetProperty(a => a.RemindedAtUtc, remindedAtUtc)
                    .SetProperty(a => a.LinkStatus, linkStatus),
                cancellationToken);

        if (affected == 0)
        {
            _logger.LogWarning("Réconciliation non enregistrée : aucun article {ArticleId} en base.", articleId);
        }
    }

    public async Task<IReadOnlyList<TrackedArticle>> GetTrackedArticlesAsync(CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Articles
            .Where(a => !a.IsFallback)
            .Select(a => new TrackedArticle(
                a.Id, a.Title, a.Url, a.RecommendedAction, a.Priority, a.CapturedAtUtc,
                a.ClassifiedAtUtc, a.WordCount, a.HumanHandledAtUtc, a.LinkStatus))
            .ToListAsync(cancellationToken);
    }
}
