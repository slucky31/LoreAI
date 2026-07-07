using Coravel.Invocable;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;
using RaindropAI.Worker.Options;

namespace RaindropAI.Worker.Services;

/// <summary>
/// Cycle de polling : détecte les nouveaux raindrops, les classifie, les persiste,
/// déclenche l'alerte Discord immédiate si la politique de notification le juge pertinent.
/// </summary>
public sealed class RaindropPollingJob : IInvocable
{
    private readonly IRaindropClient _raindropClient;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly IArticleRepository _articleRepository;
    private readonly IClassifier _classifier;
    private readonly IImmediateNotifier _immediateNotifier;
    private readonly INotificationPolicy _notificationPolicy;
    private readonly WorkerOptions _options;
    private readonly ILogger<RaindropPollingJob> _logger;

    public RaindropPollingJob(
        IRaindropClient raindropClient,
        IPollingStateRepository pollingStateRepository,
        IArticleRepository articleRepository,
        IClassifier classifier,
        IImmediateNotifier immediateNotifier,
        INotificationPolicy notificationPolicy,
        IOptions<WorkerOptions> options,
        ILogger<RaindropPollingJob> logger)
    {
        _raindropClient = raindropClient;
        _pollingStateRepository = pollingStateRepository;
        _articleRepository = articleRepository;
        _classifier = classifier;
        _immediateNotifier = immediateNotifier;
        _notificationPolicy = notificationPolicy;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken.None;

        try
        {
            var lastState = await _pollingStateRepository.GetAsync(cancellationToken);
            var newItems = await _raindropClient.GetNewRaindropsAsync(lastState, cancellationToken);

            if (newItems.Count == 0)
            {
                _logger.LogInformation("Aucun nouvel article Raindrop.");
                return;
            }

            _logger.LogInformation("{Count} nouveaux articles Raindrop détectés.", newItems.Count);

            var notifiedCount = 0;
            foreach (var item in newItems)
            {
                var classification = await _classifier.ClassifyAsync(item, cancellationToken);
                await _articleRepository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, cancellationToken);

                if (_options.WriteBackToRaindrop)
                {
                    await TryWriteBackAsync(item, classification, cancellationToken);
                }

                if (_notificationPolicy.ShouldNotifyImmediately(classification))
                {
                    await _immediateNotifier.NotifyAsync(item, classification, cancellationToken);
                    await _articleRepository.MarkDiscordNotifiedAsync(item.Id, DateTimeOffset.UtcNow, cancellationToken);
                    notifiedCount++;
                }
            }

            var latest = newItems[^1];
            await _pollingStateRepository.UpdateAsync(
                new PollingState(latest.Id, latest.CreatedUtc, DateTimeOffset.UtcNow),
                cancellationToken);

            _logger.LogInformation(
                "Cycle de polling terminé : {NewCount} nouveaux articles, {NotifiedCount} notifiés immédiatement.",
                newItems.Count,
                notifiedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du cycle de polling Raindrop.");
        }
    }

    private async Task TryWriteBackAsync(RaindropItem item, ClassificationResult classification, CancellationToken cancellationToken)
    {
        try
        {
            var tag = $"raindropai-{classification.Category}".ToLowerInvariant();
            var tags = item.Tags.Append(tag).Distinct().ToList();
            var note = $"[RaindropAI] {classification.Action} — {classification.Priority} — {classification.Reason}";
            await _raindropClient.UpdateRaindropAsync(item.Id, tags, note, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'écriture en retour vers Raindrop pour l'item {RaindropId}", item.Id);
        }
    }
}
