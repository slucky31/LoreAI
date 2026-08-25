using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface IArticleRepository
{
    /// <summary>Insère ou remplace un article classifié — idempotent sur Item.SourceId.</summary>
    Task UpsertAsync(Item item, ClassificationResult classification, ContentFetchResult content, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Enregistre le résultat de l'application (tags + déplacement éventuel) sur Raindrop.
    /// <paramref name="writeBackCollectionId"/> est l'id numérique réellement écrit (<c>null</c> si
    /// l'article est resté en « Non trié ») — la référence que <c>ReconciliationJob</c> (L3, lot 6)
    /// compare ensuite à l'état réel pour détecter un déplacement humain.
    /// </summary>
    Task RecordWriteBackAsync(long articleId, bool success, bool moved, long? writeBackCollectionId, DateTimeOffset atUtc, CancellationToken cancellationToken);

    Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Réponses Anthropic brutes des classifications réalisées depuis <paramref name="sinceUtc"/> — alimente
    /// S6 (coût LLM, #43). <c>ClassificationRawResponse</c> seul, pas de mapping vers <see cref="Item"/> :
    /// c'est tout ce dont <c>LlmUsageAnalyzer</c> a besoin.
    /// </summary>
    Task<IReadOnlyList<string>> GetClassificationRawResponsesSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Articles réellement classifiés (jamais un repli) dans <c>[startUtc, endUtc)</c> — alimente la revue
    /// mensuelle (S4, lot 5).
    /// </summary>
    Task<IReadOnlyList<MonthlyReviewArticle>> GetClassifiedBetweenAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Articles à réconcilier (L3, lot 6) : jamais <see cref="LinkStatus.Deleted"/> (tombstone
    /// définitif), les moins récemment vus en premier (<c>LastSeenAtUtc</c> nulls-first) pour répartir
    /// la charge sur des passes successives.
    /// </summary>
    Task<IReadOnlyList<ReconciliationCandidate>> GetReconciliationCandidatesAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Résultat d'une passe de réconciliation pour un article (L3/L4, lot 6).</summary>
    Task RecordReconciliationAsync(long articleId, DateTimeOffset lastSeenAtUtc, DateTimeOffset? humanHandledAtUtc, DateTimeOffset? remindedAtUtc, LinkStatus linkStatus, CancellationToken cancellationToken);

    /// <summary>Articles réellement classifiés (jamais un repli), pour les insights N3/N4/L1 (lot 6).</summary>
    Task<IReadOnlyList<TrackedArticle>> GetTrackedArticlesAsync(CancellationToken cancellationToken);
}
