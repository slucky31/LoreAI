using Coravel.Invocable;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Feed;

namespace LoreAI.Worker.Services;

/// <summary>
/// Veille automatique sur sujets (C4, lot 9, #50) : lit les entrées de la catégorie Miniflux dédiée
/// (<see cref="ISourceIngester"/> sur <see cref="SourceType.Watch"/>), les compare au corpus déjà connu via
/// la recherche plein texte (<see cref="ICorpusQueryRepository.SearchAsync"/>, Q2/lot 5), puis fait trancher
/// le LLM (<see cref="ITopicWatchFilter"/>) sur la pertinence et la nouveauté. Jamais de write-back, jamais
/// d'<see cref="Item"/> persisté (ADR 0012) — seul un log d'audit alimente S6. Même patron que
/// <c>FeedIngestionJob</c>/<c>EmailIngestionJob</c> : pas de <c>CycleRun</c> (réservé au cycle Raindrop), et
/// un échec sur une entrée n'interrompt pas le lot, le curseur étant déjà avancé par l'ingesteur.
/// </summary>
public sealed class TopicWatchJob : IInvocable, ICancellableInvocable
{
    private readonly ISourceIngester _watchIngester;
    private readonly IPollingStateRepository _pollingStateRepository;
    private readonly ICorpusQueryRepository _corpusQueryRepository;
    private readonly ITopicWatchFilter _topicWatchFilter;
    private readonly ITopicWatchNotifier _topicWatchNotifier;
    private readonly IWatchEvaluationLogRepository _watchEvaluationLogRepository;
    private readonly IReadOnlyList<WatchTopic> _topics;
    private readonly ILogger<TopicWatchJob> _logger;

    private const int MaxRelatedCorpusItems = 5;

    public TopicWatchJob(
        ISourceIngester watchIngester,
        IPollingStateRepository pollingStateRepository,
        ICorpusQueryRepository corpusQueryRepository,
        ITopicWatchFilter topicWatchFilter,
        ITopicWatchNotifier topicWatchNotifier,
        IWatchEvaluationLogRepository watchEvaluationLogRepository,
        IOptions<WatchOptions> watchOptions,
        ILogger<TopicWatchJob> logger)
    {
        _watchIngester = watchIngester;
        _pollingStateRepository = pollingStateRepository;
        _corpusQueryRepository = corpusQueryRepository;
        _topicWatchFilter = topicWatchFilter;
        _topicWatchNotifier = topicWatchNotifier;
        _watchEvaluationLogRepository = watchEvaluationLogRepository;
        _topics = watchOptions.Value.Topics.Select(t => new WatchTopic(t.Name, t.Description)).ToList();
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;

        try
        {
            var lastState = await _pollingStateRepository.GetAsync(SourceType.Watch, cancellationToken);
            var candidates = await _watchIngester.GetNewItemsAsync(lastState, cancellationToken);

            if (candidates.Count == 0)
            {
                _logger.LogInformation("Aucune nouvelle entrée de veille à évaluer.");
                return;
            }

            var notifiedCount = 0;
            var fallbackCount = 0;

            foreach (var candidate in candidates)
            {
                try
                {
                    var related = await _corpusQueryRepository.SearchAsync(candidate.Title, MaxRelatedCorpusItems, cancellationToken);
                    var evaluation = await _topicWatchFilter.EvaluateAsync(candidate, _topics, related, cancellationToken);

                    await RecordUsageAsync(evaluation, cancellationToken);

                    if (evaluation.IsFallback)
                    {
                        fallbackCount++;
                        _logger.LogWarning(
                            "Évaluation en repli pour l'entrée de veille {SourceId} ({Reason}) — pas d'alerte, lot non interrompu.",
                            candidate.SourceId,
                            evaluation.Reason);
                        continue;
                    }

                    if (evaluation is { IsRelevant: true, IsNew: true })
                    {
                        await _topicWatchNotifier.NotifyAsync(candidate, evaluation, cancellationToken);
                        notifiedCount++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Arrêt de l'application demandé — veille interrompue proprement.");
                    break;
                }
                catch (Exception ex)
                {
                    // Même raisonnement que FeedIngestionJob/EmailIngestionJob : le curseur est déjà avancé,
                    // indépendant du sort de chaque candidat — on journalise et on continue.
                    _logger.LogError(ex, "Échec de l'évaluation de l'entrée de veille {SourceId} — poursuite avec la suivante.", candidate.SourceId);
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Veille terminée : {CandidateCount} entrées évaluées, {NotifiedCount} alertes envoyées, {FallbackCount} en repli.",
                    candidates.Count,
                    notifiedCount,
                    fallbackCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Veille interrompue par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la veille automatique.");
        }
    }

    /// <summary>Best-effort (S6) : un échec d'écriture du journal d'usage ne doit jamais faire perdre une évaluation par ailleurs réussie.</summary>
    private async Task RecordUsageAsync(WatchEvaluation evaluation, CancellationToken cancellationToken)
    {
        try
        {
            await _watchEvaluationLogRepository.RecordAsync(evaluation.RawResponse, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'enregistrement du journal d'usage LLM pour la veille.");
        }
    }
}
