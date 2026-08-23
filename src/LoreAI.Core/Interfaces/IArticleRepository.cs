using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

public interface IArticleRepository
{
    /// <summary>Insère ou remplace un article classifié — idempotent sur Item.SourceId.</summary>
    Task UpsertAsync(Item item, ClassificationResult classification, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken);

    /// <summary>Enregistre le résultat de l'application (tags + déplacement éventuel) sur Raindrop.</summary>
    Task RecordWriteBackAsync(long articleId, bool success, bool moved, DateTimeOffset atUtc, CancellationToken cancellationToken);

    Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken);
}
