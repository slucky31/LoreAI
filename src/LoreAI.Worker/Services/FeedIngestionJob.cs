using Coravel.Invocable;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;

namespace LoreAI.Worker.Services;

/// <summary>
/// Ingère les nouvelles entrées RSS/Atom via une instance Miniflux auto-hébergée (lot 7, #48) : Miniflux
/// gère les abonnements et le parsing, <see cref="IFeedIngester"/> ne fait que lire ses entrées, puis
/// classification + persistance via <see cref="ArticleClassificationStep"/> — jamais de write-back
/// Raindrop ni Miniflux, une source Feed n'étant jamais réécrite (ADR 0012). Même patron
/// qu'<c>EmailIngestionJob</c> : pas de <c>CycleRun</c> (réservé au cycle Raindrop, seul lu par le
/// healthcheck) et un repli de classification n'interrompt pas le lot — le curseur est déjà avancé par
/// <see cref="IFeedIngester"/>, indépendamment du sort de la classification de chaque entrée.
/// </summary>
public sealed class FeedIngestionJob : IInvocable, ICancellableInvocable
{
    private readonly IFeedIngester _feedIngester;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly IRaindropClient _raindropClient;
    private readonly ArticleClassificationStep _classificationStep;
    private readonly ILogger<FeedIngestionJob> _logger;

    public FeedIngestionJob(
        IFeedIngester feedIngester,
        IPollingStateRepository pollingStateRepository,
        IRaindropClient raindropClient,
        ArticleClassificationStep classificationStep,
        ILogger<FeedIngestionJob> logger)
    {
        _feedIngester = feedIngester;
        _pollingStateRepository = pollingStateRepository;
        _raindropClient = raindropClient;
        _classificationStep = classificationStep;
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;

        try
        {
            var lastState = await _pollingStateRepository.GetAsync(SourceType.Feed, cancellationToken);
            var newItems = await _feedIngester.GetNewItemsAsync(lastState, cancellationToken);

            if (newItems.Count == 0)
            {
                _logger.LogInformation("Aucune nouvelle entrée Feed à traiter.");
                return;
            }

            var taxonomy = await _raindropClient.GetTaxonomyAsync(cancellationToken);
            var processedCount = 0;
            var fallbackCount = 0;

            foreach (var item in newItems)
            {
                try
                {
                    var outcome = await _classificationStep.ProcessAsync(item, taxonomy, cancellationToken);
                    if (outcome.Classification.IsFallback)
                    {
                        fallbackCount++;
                        _logger.LogWarning(
                            "Classification en repli pour l'entrée Feed {SourceId} ({Reason}) — persistée pour audit, lot non interrompu (curseur déjà avancé par l'ingesteur).",
                            item.SourceId,
                            outcome.Classification.Reason);
                    }

                    processedCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Arrêt de l'application demandé — ingestion Feed interrompue proprement.");
                    break;
                }
                catch (Exception ex)
                {
                    // Contrairement à UnsortedClassificationJob, un échec ici ne remet pas en cause le
                    // curseur (déjà avancé, indépendant des Item retournés) : on journalise et on continue
                    // avec l'entrée suivante plutôt que d'interrompre tout le lot.
                    _logger.LogError(ex, "Échec du traitement de l'entrée Feed {SourceId} — poursuite avec la suivante.", item.SourceId);
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Ingestion Feed terminée : {ProcessedCount}/{NewCount} entrées traitées, {FallbackCount} en repli.",
                    processedCount,
                    newItems.Count,
                    fallbackCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Ingestion Feed interrompue par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'ingestion Feed.");
        }
    }
}
