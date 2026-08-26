using Coravel.Invocable;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;

namespace LoreAI.Worker.Services;

/// <summary>
/// Ingère les newsletters Gmail portant le label configuré (lot 8, #49) : filtre heuristique + extraction
/// LLM des vrais liens (dans <see cref="IGmailIngester"/>), puis classification + persistance via
/// <see cref="ArticleClassificationStep"/> — jamais de write-back Raindrop, une source Newsletter n'étant
/// jamais réécrite (ADR 0012). Plus simple qu'<c>UnsortedClassificationJob</c> : pas de <c>CycleRun</c>
/// (réservé au cycle Raindrop, seul lu par le healthcheck) et un repli de classification n'interrompt pas
/// le lot — le curseur <c>historyId</c> est déjà avancé par <see cref="IGmailIngester"/>, indépendamment du
/// sort de la classification de chaque lien.
/// </summary>
public sealed class EmailIngestionJob : IInvocable, ICancellableInvocable
{
    private readonly IGmailIngester _gmailIngester;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly IRaindropClient _raindropClient;
    private readonly ArticleClassificationStep _classificationStep;
    private readonly ILogger<EmailIngestionJob> _logger;

    public EmailIngestionJob(
        IGmailIngester gmailIngester,
        IPollingStateRepository pollingStateRepository,
        IRaindropClient raindropClient,
        ArticleClassificationStep classificationStep,
        ILogger<EmailIngestionJob> logger)
    {
        _gmailIngester = gmailIngester;
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
            var lastState = await _pollingStateRepository.GetAsync(SourceType.Newsletter, cancellationToken);
            var newItems = await _gmailIngester.GetNewItemsAsync(lastState, cancellationToken);

            if (newItems.Count == 0)
            {
                _logger.LogInformation("Aucun nouveau lien Newsletter à traiter.");
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
                            "Classification en repli pour le lien Newsletter {SourceId} ({Reason}) — persisté pour audit, lot non interrompu (curseur déjà avancé par l'ingesteur).",
                            item.SourceId,
                            outcome.Classification.Reason);
                    }

                    processedCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Arrêt de l'application demandé — ingestion Newsletter interrompue proprement.");
                    break;
                }
                catch (Exception ex)
                {
                    // Contrairement à UnsortedClassificationJob, un échec ici ne remet pas en cause le
                    // curseur (déjà avancé, indépendant des Item retournés) : on journalise et on continue
                    // avec le lien suivant plutôt que d'interrompre tout le lot.
                    _logger.LogError(ex, "Échec du traitement du lien Newsletter {SourceId} — poursuite avec le suivant.", item.SourceId);
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Ingestion Newsletter terminée : {ProcessedCount}/{NewCount} liens traités, {FallbackCount} en repli.",
                    processedCount,
                    newItems.Count,
                    fallbackCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Ingestion Newsletter interrompue par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'ingestion Newsletter.");
        }
    }
}
