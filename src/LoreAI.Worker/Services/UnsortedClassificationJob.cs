using Coravel.Invocable;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Core.Services;
using LoreAI.Worker.Options;

namespace LoreAI.Worker.Services;

/// <summary>
/// Traitement principal : détecte les nouveaux articles dans "Non trié", apprend la taxonomie réelle
/// (collections + tags existants), classifie via le LLM, puis applique directement le résultat
/// (tags fusionnés + déplacement de collection si une correspondance existe) — sans étape de validation.
/// Tout ce qui est en dehors de "Non trié" est considéré comme déjà classé et n'est jamais retouché.
/// </summary>
public sealed class UnsortedClassificationJob : IInvocable, ICancellableInvocable
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

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;

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
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "{Count} nouveaux articles à trier ({CollectionCount} collections et {TagCount} tags connus).",
                    newItems.Count,
                    taxonomy.Collections.Count,
                    taxonomy.Tags.Count);
            }

            var notifiedCount = 0;
            var movedCount = 0;
            var processedCount = 0;
            RaindropItem? lastProcessed = null;

            foreach (var item in newItems)
            {
                try
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Traitement du signet {RaindropId} en cours.", item.Id);
                    }

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

                    var matchedCollection = ResolveTargetCollection(classification, taxonomy);

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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation(
                            "Arrêt de l'application demandé — cycle interrompu proprement avant le raindrop {RaindropId}.",
                            item.Id);
                    }
                    break;
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
                // Volontairement non annulable : à ce stade des raindrops réels ont déjà été modifiés.
                // Utiliser le token d'arrêt ferait échouer cette écriture pendant un shutdown et rejouerait
                // tout le batch au redémarrage. C'est une écriture SQLite locale, elle ne retarde pas l'arrêt.
                await _pollingStateRepository.UpdateAsync(
                    new PollingState(lastProcessed.Id, lastProcessed.CreatedUtc, DateTimeOffset.UtcNow),
                    CancellationToken.None);
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Cycle terminé : {ProcessedCount}/{NewCount} articles traités, {MovedCount} déplacés, {NotifiedCount} notifiés immédiatement.",
                    processedCount,
                    newItems.Count,
                    movedCount,
                    notifiedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Cycle de classification interrompu par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du cycle de classification de \"Non trié\".");
        }
    }

    /// <summary>
    /// Ne renvoie une collection que si le titre suggéré désigne une cible <b>sans ambiguïté</b>. Deux
    /// collections homonymes (sous des parents différents) ne permettent pas de trancher : on préfère
    /// laisser l'article dans « Non trié » avec ses tags plutôt que de le ranger au mauvais endroit.
    /// </summary>
    private RaindropCollection? ResolveTargetCollection(ClassificationResult classification, RaindropTaxonomy taxonomy)
    {
        if (classification.SuggestedCollection is null)
        {
            return null;
        }

        var matches = taxonomy.Collections
            .Where(c => c.Title == classification.SuggestedCollection)
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "Titre de collection ambigu « {Title} » ({Count} collections homonymes) : le raindrop n'est pas déplacé.",
                classification.SuggestedCollection,
                matches.Count);
        }

        return null;
    }

    /// <summary>
    /// Applique toujours les tags (fusionnés, jamais de perte) ; ne déplace la collection que si une
    /// correspondance existante a été trouvée. La note rédigée par l'utilisateur est préservée ; seul le
    /// bloc [LoreAI] d'un passage précédent est remplacé (cf. <see cref="ClassificationNoteBuilder"/>).
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

            var mergedNote = ClassificationNoteBuilder.Build(item.Note, classification);

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
