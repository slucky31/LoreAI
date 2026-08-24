using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface IArticleRepository
{
    /// <summary>Insère ou remplace un article classifié — idempotent sur Item.SourceId.</summary>
    Task UpsertAsync(Item item, ClassificationResult classification, ContentFetchResult content, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken);

    /// <summary>Enregistre le résultat de l'application (tags + déplacement éventuel) sur Raindrop.</summary>
    Task RecordWriteBackAsync(long articleId, bool success, bool moved, DateTimeOffset atUtc, CancellationToken cancellationToken);

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
}
