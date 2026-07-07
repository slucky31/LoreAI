using Coravel.Invocable;
using RaindropAI.Core.Interfaces;

namespace RaindropAI.Worker.Services;

/// <summary>Digest quotidien exhaustif : tout ce qui n'a pas encore été inclus dans un envoi précédent.</summary>
public sealed class DigestNotificationJob : IInvocable
{
    private readonly IArticleRepository _articleRepository;
    private readonly IDigestNotifier _digestNotifier;
    private readonly ILogger<DigestNotificationJob> _logger;

    public DigestNotificationJob(IArticleRepository articleRepository, IDigestNotifier digestNotifier, ILogger<DigestNotificationJob> logger)
    {
        _articleRepository = articleRepository;
        _digestNotifier = digestNotifier;
        _logger = logger;
    }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken.None;

        try
        {
            var pendingArticles = await _articleRepository.GetUnsentDigestItemsAsync(cancellationToken);
            if (pendingArticles.Count == 0)
            {
                _logger.LogInformation("Digest : aucun article en attente, envoi ignoré.");
                return;
            }

            await _digestNotifier.SendDigestAsync(pendingArticles, cancellationToken);
            await _articleRepository.MarkDigestSentAsync(
                pendingArticles.Select(a => a.Item.Id).ToList(),
                DateTimeOffset.UtcNow,
                cancellationToken);

            _logger.LogInformation("Digest envoyé avec {Count} articles.", pendingArticles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'envoi du digest quotidien.");
        }
    }
}
