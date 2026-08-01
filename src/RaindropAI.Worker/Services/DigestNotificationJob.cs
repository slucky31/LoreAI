using Coravel.Invocable;
using RaindropAI.Core.Interfaces;

namespace RaindropAI.Worker.Services;

/// <summary>Digest quotidien exhaustif : tout ce qui n'a pas encore été inclus dans un envoi précédent.</summary>
public sealed class DigestNotificationJob : IInvocable, ICancellableInvocable
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

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;

        try
        {
            var pendingArticles = await _articleRepository.GetUnsentDigestItemsAsync(cancellationToken);
            if (pendingArticles.Count == 0)
            {
                _logger.LogInformation("Digest : aucun article en attente, envoi ignoré.");
                return;
            }

            await _digestNotifier.SendDigestAsync(pendingArticles, cancellationToken);

            // Volontairement non annulable : l'email est parti, ne pas enregistrer ce fait le ferait
            // renvoyer au prochain digest. Une seule écriture SQLite locale, elle ne retarde pas l'arrêt.
            await _articleRepository.MarkDigestSentAsync(
                pendingArticles.Select(a => a.Item.Id).ToList(),
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Digest envoyé avec {Count} articles.", pendingArticles.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Envoi du digest interrompu par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'envoi du digest quotidien.");
        }
    }
}
