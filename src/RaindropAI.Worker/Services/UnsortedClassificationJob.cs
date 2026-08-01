using Coravel.Invocable;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;
using RaindropAI.Worker.Options;

namespace RaindropAI.Worker.Services;

/// <summary>
/// Traitement principal : détecte les nouveaux articles dans "Non trié", apprend la taxonomie réelle
/// (collections + tags existants), classifie via le LLM, puis applique directement le résultat
/// (tags fusionnés + déplacement de collection si une correspondance existe) — sans étape de validation.
/// Tout ce qui est en dehors de "Non trié" est considéré comme déjà classé et n'est jamais retouché.
/// </summary>
public sealed class UnsortedClassificationJob : IInvocable
{
    private readonly IRaindropClient _raindropClient;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly IArticleRepository _articleRepository;
    private readonly IClassifier _classifier;
    private readonly IImmediateNotifier _immediateNotifier;
    private readonly INotificationPolicy _notificationPolicy;
    private readonly WorkerOptions _options;
    private readonly ILogger<UnsortedClassificationJob> _logger;

    public UnsortedClassificationJob(
        IRaindropClient raindropClient,
        IPollingStateRepository pollingStateRepository,
        IArticleRepository articleRepository,
        IClassifier classifier,
        IImmediateNotifier immediateNotifier,
        INotificationPolicy notificationPolicy,
        IOptions<WorkerOptions> options,
        ILogger<UnsortedClassificationJob> logger)
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
                _logger.LogInformation("Aucun nouvel article dans \"Non trié\".");
                return;
            }

            var taxonomy = await _raindropClient.GetTaxonomyAsync(cancellationToken);
            _logger.LogInformation(
                "{Count} nouveaux articles à trier ({CollectionCount} collections et {TagCount} tags connus).",
                newItems.Count,
                taxonomy.Collections.Count,
                taxonomy.Tags.Count);

            var notifiedCount = 0;
            var movedCount = 0;
            var processedCount = 0;
            RaindropItem? lastProcessed = null;

            foreach (var item in newItems)
            {
                try
                {
                    var classification = await _classifier.ClassifyAsync(item, taxonomy, cancellationToken);
                    await _articleRepository.UpsertAsync(item, classification, DateTimeOffset.UtcNow, cancellationToken);

                    if (classification.IsFallback)
                    {
                        // Le repli n'est pas une décision du modèle : l'appliquer écrirait une trace d'erreur
                        // dans le raindrop réel. On interrompt le cycle sans dépasser cet article, pour qu'il
                        // soit repris au prochain passage au lieu d'être perdu définitivement par le high-water mark.
                        _logger.LogWarning(
                            "Classification en repli pour le raindrop {RaindropId} ({Reason}) — rien n'est appliqué, cycle interrompu, reprise au prochain passage.",
                            item.Id,
                            classification.Reason);
                        break;
                    }

                    var matchedCollection = classification.SuggestedCollection is not null
                        ? taxonomy.Collections.FirstOrDefault(c => c.Title == classification.SuggestedCollection)
                        : null;

                    if (_options.WriteBackToRaindrop)
                    {
                        var moved = await ApplyClassificationAsync(item, classification, matchedCollection, cancellationToken);
                        if (moved)
                        {
                            movedCount++;
                        }
                    }

                    if (_notificationPolicy.ShouldNotifyImmediately(classification))
                    {
                        await _immediateNotifier.NotifyAsync(item, classification, cancellationToken);
                        await _articleRepository.MarkDiscordNotifiedAsync(item.Id, DateTimeOffset.UtcNow, cancellationToken);
                        notifiedCount++;
                    }

                    processedCount++;
                    lastProcessed = item;
                }
                catch (Exception ex)
                {
                    // Sans ce filet, l'exception remontait au catch du cycle et court-circuitait l'avancement
                    // du high-water mark : les articles déjà traités et écrits dans Raindrop étaient rejoués
                    // au cycle suivant. On s'arrête ici en conservant la progression acquise.
                    _logger.LogError(
                        ex,
                        "Échec du traitement du raindrop {RaindropId} — cycle interrompu, la progression acquise est conservée.",
                        item.Id);
                    break;
                }
            }

            if (lastProcessed is not null)
            {
                await _pollingStateRepository.UpdateAsync(
                    new PollingState(lastProcessed.Id, lastProcessed.CreatedUtc, DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            _logger.LogInformation(
                "Cycle terminé : {ProcessedCount}/{NewCount} articles traités, {MovedCount} déplacés, {NotifiedCount} notifiés immédiatement.",
                processedCount,
                newItems.Count,
                movedCount,
                notifiedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du cycle de classification de \"Non trié\".");
        }
    }

    /// <summary>
    /// Applique toujours les tags (fusionnés, jamais de perte) ; ne déplace la collection que si une
    /// correspondance existante a été trouvée. La note existante est complétée, jamais écrasée.
    /// </summary>
    private async Task<bool> ApplyClassificationAsync(
        RaindropItem item,
        ClassificationResult classification,
        RaindropCollection? matchedCollection,
        CancellationToken cancellationToken)
    {
        try
        {
            var mergedTags = item.Tags
                .Concat(classification.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var classificationNote = $"[RaindropAI] {classification.Action} — {classification.Priority} — {classification.Reason}";
            var mergedNote = string.IsNullOrWhiteSpace(item.Note)
                ? classificationNote
                : $"{item.Note}\n\n{classificationNote}";

            await _raindropClient.UpdateRaindropAsync(item.Id, mergedTags, mergedNote, matchedCollection?.Id, cancellationToken);
            await _articleRepository.RecordWriteBackAsync(item.Id, success: true, moved: matchedCollection is not null, DateTimeOffset.UtcNow, cancellationToken);

            return matchedCollection is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'application des tags/déplacement pour l'item {RaindropId}", item.Id);
            await _articleRepository.RecordWriteBackAsync(item.Id, success: false, moved: false, DateTimeOffset.UtcNow, cancellationToken);
            return false;
        }
    }
}
