using System.Globalization;
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

    public async Task UpsertAsync(Item item, ClassificationResult classification, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var id = long.Parse(item.SourceId, CultureInfo.InvariantCulture);
        var entity = await context.Articles.FindAsync([id], cancellationToken);
        if (entity is null)
        {
            entity = new ArticleEntity { Id = id, Title = item.Title, Url = item.Url };
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
        entity.ClassificationModel = classification.Model;
        entity.ClassificationRawResponse = NormalizeToJson(classification.RawResponse);
        entity.ClassifiedAtUtc = classifiedAtUtc;

        await context.SaveChangesAsync(cancellationToken);
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

    public async Task RecordWriteBackAsync(long articleId, bool success, bool moved, DateTimeOffset atUtc, CancellationToken cancellationToken)
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
                    .SetProperty(a => a.WriteBackAtUtc, atUtc),
                cancellationToken);

        if (affected == 0)
        {
            _logger.LogWarning("Write-back non enregistré : aucun article {ArticleId} en base.", articleId);
        }
    }

    public async Task<IReadOnlyList<ClassifiedArticle>> GetUnsentDigestItemsAsync(CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await context.Articles
            .Where(a => a.EmailDigestSentAtUtc == null)
            .OrderBy(a => a.CapturedAtUtc)
            .ToListAsync(cancellationToken);

        // Pas de log du nombre ici : DigestNotificationJob le journalise déjà côté appelant.
        return entities.Select(MapToClassifiedArticle).ToList();
    }

    public async Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken)
    {
        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Articles
            .Where(a => a.Id == articleId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.DiscordNotifiedAtUtc, notifiedAtUtc), cancellationToken);
    }

    public async Task MarkDigestSentAsync(IReadOnlyCollection<long> articleIds, DateTimeOffset sentAtUtc, CancellationToken cancellationToken)
    {
        if (articleIds.Count == 0)
        {
            return;
        }

        await _schemaGuard.EnsureMigratedAsync(cancellationToken);
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // ExecuteUpdateAsync traduit le IN en une seule requête SQL : contrairement à SQLite, Postgres
        // n'impose pas de limite de variables qui obligerait à découper articleIds en lots.
        await context.Articles
            .Where(a => articleIds.Contains(a.Id))
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.EmailDigestSentAtUtc, sentAtUtc), cancellationToken);
    }

    private static ClassifiedArticle MapToClassifiedArticle(ArticleEntity entity)
    {
        // SourceType.Raindrop en dur : Raindrop est l'unique source ingérée à ce stade (ADR 0012),
        // l'entité elle-même ne porte pas encore ce champ.
        var item = new Item(
            SourceType.Raindrop,
            entity.Id.ToString(CultureInfo.InvariantCulture),
            entity.Url,
            entity.Title,
            entity.Excerpt,
            entity.Note,
            entity.OriginalTags,
            entity.CapturedAtUtc);

        var classification = new ClassificationResult(
            entity.SuggestedCollection,
            entity.SuggestedTags,
            entity.RecommendedAction,
            entity.Priority,
            entity.Reason ?? string.Empty,
            entity.ClassificationModel ?? string.Empty,
            entity.ClassificationRawResponse ?? string.Empty);

        return new ClassifiedArticle(
            item,
            classification,
            entity.ClassifiedAtUtc ?? DateTimeOffset.UtcNow,
            entity.Moved,
            entity.DiscordNotifiedAtUtc,
            entity.EmailDigestSentAtUtc,
            entity.FetchedAtUtc,
            entity.WriteBackStatus);
    }
}
