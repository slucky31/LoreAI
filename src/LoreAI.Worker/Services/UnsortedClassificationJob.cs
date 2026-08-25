using System.Globalization;
using Coravel.Invocable;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
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
    private readonly ICycleRunRepository _cycleRunRepository;
    private readonly ICycleReportNotifier _cycleReportNotifier;
    private readonly IClassifier _classifier;
    private readonly IContentFetcher _contentFetcher;
    private readonly IImmediateNotifier _immediateNotifier;
    private readonly INotificationPolicy _notificationPolicy;
    private readonly IToolRepository _toolRepository;
    private readonly WorkerOptions _options;
    private readonly ILogger<UnsortedClassificationJob> _logger;

    public UnsortedClassificationJob(
        IRaindropClient raindropClient,
        IPollingStateRepository pollingStateRepository,
        IArticleRepository articleRepository,
        ICycleRunRepository cycleRunRepository,
        ICycleReportNotifier cycleReportNotifier,
        IClassifier classifier,
        IContentFetcher contentFetcher,
        IImmediateNotifier immediateNotifier,
        INotificationPolicy notificationPolicy,
        IToolRepository toolRepository,
        IOptions<WorkerOptions> options,
        ILogger<UnsortedClassificationJob> logger)
    {
        _raindropClient = raindropClient;
        _pollingStateRepository = pollingStateRepository;
        _articleRepository = articleRepository;
        _cycleRunRepository = cycleRunRepository;
        _cycleReportNotifier = cycleReportNotifier;
        _classifier = classifier;
        _contentFetcher = contentFetcher;
        _immediateNotifier = immediateNotifier;
        _notificationPolicy = notificationPolicy;
        _toolRepository = toolRepository;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;
        var startedUtc = DateTimeOffset.UtcNow;

        var itemsSeen = 0;
        var processedCount = 0;
        var movedCount = 0;
        var tagsAppliedCount = 0;
        var notifiedCount = 0;
        string? failureReason = null;
        CycleOutcome? forcedOutcome = null;

        try
        {
            var lastState = await _pollingStateRepository.GetAsync(SourceType.Raindrop, cancellationToken);
            var newItems = await _raindropClient.GetNewItemsAsync(lastState, cancellationToken);
            itemsSeen = newItems.Count;

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

            Item? lastProcessed = null;

            foreach (var item in newItems)
            {
                try
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Traitement du signet {SourceId} en cours.", item.SourceId);
                    }

                    var content = _options.FetchArticleContent
                        ? await _contentFetcher.FetchAsync(item.Url, cancellationToken)
                        : ContentFetchResult.Skipped;

                    var classification = await _classifier.ClassifyAsync(item, taxonomy, content.Text, cancellationToken);
                    await _articleRepository.UpsertAsync(item, classification, content, DateTimeOffset.UtcNow, cancellationToken);
                    await UpsertToolAsync(item, classification, cancellationToken);

                    if (classification.IsFallback)
                    {
                        // Le repli n'est pas une décision du modèle : l'appliquer écrirait une trace d'erreur
                        // dans le raindrop réel. On interrompt le cycle sans dépasser cet article, pour qu'il
                        // soit repris au prochain passage au lieu d'être perdu définitivement par le high-water mark.
                        failureReason = $"Classification en repli pour l'item {item.SourceId} ({classification.Reason}).";
                        _logger.LogWarning(
                            "Classification en repli pour l'item {SourceId} ({Reason}) — rien n'est appliqué, cycle interrompu, reprise au prochain passage.",
                            item.SourceId,
                            classification.Reason);
                        break;
                    }

                    var matchedCollection = ResolveTargetCollection(classification, taxonomy);

                    if (_options.WriteBackToRaindrop)
                    {
                        var writeBack = await ApplyClassificationAsync(item, classification, matchedCollection, cancellationToken);
                        if (writeBack.Moved)
                        {
                            movedCount++;
                        }
                        tagsAppliedCount += writeBack.TagsAdded;
                    }

                    if (_notificationPolicy.ShouldNotifyImmediately(classification))
                    {
                        await _immediateNotifier.NotifyAsync(item, classification, cancellationToken);
                        await _articleRepository.MarkDiscordNotifiedAsync(ParseRaindropId(item), DateTimeOffset.UtcNow, cancellationToken);
                        notifiedCount++;
                    }

                    processedCount++;
                    lastProcessed = item;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    failureReason = $"Arrêt de l'application demandé avant l'item {item.SourceId}.";
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation(
                            "Arrêt de l'application demandé — cycle interrompu proprement avant l'item {SourceId}.",
                            item.SourceId);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    // Sans ce filet, l'exception remontait au catch du cycle et court-circuitait l'avancement
                    // du high-water mark : les articles déjà traités et écrits dans Raindrop étaient rejoués
                    // au cycle suivant. On s'arrête ici en conservant la progression acquise.
                    failureReason = ex.Message;
                    _logger.LogError(
                        ex,
                        "Échec du traitement de l'item {SourceId} — cycle interrompu, la progression acquise est conservée.",
                        item.SourceId);
                    break;
                }
            }

            if (lastProcessed is not null)
            {
                // Volontairement non annulable : à ce stade des raindrops réels ont déjà été modifiés.
                // Utiliser le token d'arrêt ferait échouer cette écriture pendant un shutdown et rejouerait
                // tout le batch au redémarrage. C'est une écriture SQLite locale, elle ne retarde pas l'arrêt.
                await _pollingStateRepository.UpdateAsync(
                    new PollingState(SourceType.Raindrop, lastProcessed.SourceId, lastProcessed.CapturedAtUtc, DateTimeOffset.UtcNow),
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
            forcedOutcome = CycleOutcome.Interrupted;
            failureReason ??= "Cycle de classification interrompu par l'arrêt de l'application.";
            _logger.LogInformation("Cycle de classification interrompu par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            // itemsSeen == 0 ici signifie qu'on ne sait même pas s'il y avait quelque chose à traiter
            // (échec avant/pendant GetNewItemsAsync) : Failed. Au-delà, l'existence des items est connue,
            // l'échec n'a fait qu'interrompre leur traitement : Interrupted.
            forcedOutcome = itemsSeen == 0 ? CycleOutcome.Failed : CycleOutcome.Interrupted;
            failureReason ??= ex.Message;
            _logger.LogError(ex, "Échec du cycle de classification de \"Non trié\".");
        }
        finally
        {
            var outcome = forcedOutcome ?? (itemsSeen == 0
                ? CycleOutcome.Empty
                : (processedCount == itemsSeen ? CycleOutcome.Ok : CycleOutcome.Interrupted));

            var run = new CycleRun(
                startedUtc,
                DateTimeOffset.UtcNow,
                outcome,
                itemsSeen,
                processedCount,
                movedCount,
                tagsAppliedCount,
                notifiedCount,
                failureReason);

            try
            {
                await _cycleRunRepository.RecordAsync(run, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Échec de l'enregistrement du journal de cycle.");
            }

            // Pas d'import, pas de notification (#31) : un cycle vide, ou un échec avant même de savoir
            // s'il y avait quelque chose à traiter (itemsSeen == 0 dans les deux cas), reste silencieux.
            if (run.ItemsSeen > 0)
            {
                try
                {
                    await _cycleReportNotifier.NotifyCycleCompletedAsync(run, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Échec de l'envoi du compte-rendu de cycle.");
                }
            }
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
    private async Task<WriteBackOutcome> ApplyClassificationAsync(
        Item item,
        ClassificationResult classification,
        RaindropCollection? matchedCollection,
        CancellationToken cancellationToken)
    {
        var raindropId = ParseRaindropId(item);

        try
        {
            var mergedTags = item.Tags
                .Concat(classification.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mergedNote = ClassificationNoteBuilder.Build(item.Note, classification);

            await _raindropClient.UpdateRaindropAsync(raindropId, mergedTags, mergedNote, matchedCollection?.Id, cancellationToken);
            await _articleRepository.RecordWriteBackAsync(raindropId, success: true, moved: matchedCollection is not null, matchedCollection?.Id, DateTimeOffset.UtcNow, cancellationToken);

            // Ne compte que les tags réellement nouveaux (fusion insensible à la casse) : un tag déjà
            // présent sur l'article ne doit pas gonfler le compteur du journal de cycle.
            var tagsAdded = mergedTags.Count - item.Tags.Count;
            return new WriteBackOutcome(matchedCollection is not null, tagsAdded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'application des tags/déplacement pour l'item {SourceId}", item.SourceId);
            await _articleRepository.RecordWriteBackAsync(raindropId, success: false, moved: false, writeBackCollectionId: null, DateTimeOffset.UtcNow, cancellationToken);
            return new WriteBackOutcome(Moved: false, TagsAdded: 0);
        }
    }

    /// <summary>
    /// S7 (lot 5) : n'alimente la base d'outils que pour une vraie classification ATester avec un nom
    /// d'outil renseigné — c'est la définition même de cette action (cf. le prompt de classification), pas
    /// une extension à ALire/Reference. Best-effort, comme le reste de la méthode : ne bloque jamais le cycle.
    /// </summary>
    private async Task UpsertToolAsync(Item item, ClassificationResult classification, CancellationToken cancellationToken)
    {
        if (classification.IsFallback || classification.Action != RecommendedAction.ATester || string.IsNullOrWhiteSpace(classification.ToolName))
        {
            return;
        }

        try
        {
            await _toolRepository.UpsertFromArticleAsync(classification.ToolName, classification.ToolCategory, ParseRaindropId(item), DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de la mise à jour de la base d'outils pour l'item {SourceId}", item.SourceId);
        }
    }

    /// <summary>Le write-back reste strictement Raindrop (ADR 0012) : on retrouve l'id numérique attendu par l'API.</summary>
    private static long ParseRaindropId(Item item) => long.Parse(item.SourceId, CultureInfo.InvariantCulture);

    private readonly record struct WriteBackOutcome(bool Moved, int TagsAdded);
}
