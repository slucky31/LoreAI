using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface IArticleRepository
{
    /// <summary>
    /// Insère ou remplace un article classifié — idempotent sur <c>(Item.SourceType, Item.SourceId)</c>.
    /// Retourne l'id généré par la base (lot 8, #49) : la clé applicative n'est plus l'id Raindrop
    /// numérique, un lien Newsletter n'en ayant pas.
    /// </summary>
    Task<long> UpsertAsync(Item item, ClassificationResult classification, ContentFetchResult content, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken);

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

    /// <summary>Articles actuellement suivis comme tagués « cette-semaine » (L5, lot 8) — toujours <see cref="SourceType.Raindrop"/>, seule source qu'on peut tagger.</summary>
    Task<IReadOnlyList<ReadingQueueTaggedArticle>> GetReadingQueueTaggedAsync(CancellationToken cancellationToken);

    /// <summary>Pose (<paramref name="taggedAtUtc"/> non nul) ou efface (<c>null</c>) le suivi du tag L5 pour un article.</summary>
    Task SetReadingQueueTagAsync(long articleId, DateTimeOffset? taggedAtUtc, CancellationToken cancellationToken);
}
