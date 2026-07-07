using RaindropAI.Core.Models;

namespace RaindropAI.Core.Interfaces;

public interface IArticleRepository
{
    /// <summary>Insère ou remplace un article classifié — idempotent sur RaindropItem.Id.</summary>
    Task UpsertAsync(RaindropItem item, ClassificationResult classification, DateTimeOffset classifiedAtUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClassifiedArticle>> GetUnsentDigestItemsAsync(CancellationToken cancellationToken);

    Task MarkDiscordNotifiedAsync(long articleId, DateTimeOffset notifiedAtUtc, CancellationToken cancellationToken);

    Task MarkDigestSentAsync(IReadOnlyCollection<long> articleIds, DateTimeOffset sentAtUtc, CancellationToken cancellationToken);
}
