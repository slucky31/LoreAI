using RaindropAI.Core.Models;

namespace RaindropAI.Core.Interfaces;

public interface IArticleRepository
{
    /// <summary>Insère ou remplace un article classifié — idempotent sur RaindropItem.Id.</summary>
    Task UpsertAsync(RaindropItem item, ClassificationResult classification, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken);

    /// <summary>Enregistre le résultat de l'application (tags + déplacement éventuel) sur Raindrop.</summary>
    Task RecordWriteBackAsync(long articleId, bool success, bool moved, DateTimeOffset atUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClassifiedArticle>> GetUnsentDigestItemsAsync(CancellationToken cancellationToken);

    Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken);

    Task MarkDigestSentAsync(IReadOnlyCollection<long> articleIds, DateTimeOffset sentAtUtc, CancellationToken cancellationToken);
}
