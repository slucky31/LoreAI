using Coravel.Invocable;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Worker.Services;

/// <summary>
/// Veille automatique sur sujets (C4, lot 9, #50, redesign) : pour chaque sujet persisté
/// (<see cref="IWatchTopicRepository"/>), lit les nouvelles entrées de sa catégorie Miniflux dédiée
/// (<see cref="IMinifluxCategoryReader"/>, curseur propre au sujet), les compare au corpus déjà connu via
/// la recherche plein texte (<see cref="ICorpusQueryRepository.SearchAsync"/>, Q2/lot 5), puis fait trancher
/// le LLM (<see cref="ITopicWatchFilter"/>). Une entrée jugée pertinente et nouvelle est créée directement
/// dans la collection Raindrop du sujet (<see cref="IRaindropClient.CreateRaindropAsync"/>, tag
/// <c>veille</c> + tags proposés) — seul cas du projet où une source non-Raindrop provoque une création
/// dans Raindrop (jamais de modification d'un item existant, ADR 0012 intact sur ce point). Un résumé
/// groupé est envoyé en une seule notification Discord par exécution (<see cref="IWatchDigestNotifier"/>),
/// jamais une par article — remplace la notification détaillée du design initial. Un échec sur un candidat
/// ou un sujet n'interrompt pas les suivants, même raisonnement que <c>FeedIngestionJob</c>.
/// </summary>
public sealed class TopicWatchJob : IInvocable, ICancellableInvocable
{
    private const int MaxRelatedCorpusItems = 5;
    private const string WatchTag = "veille";

    private readonly IWatchTopicRepository _watchTopicRepository;
    private readonly IMinifluxCategoryReader _minifluxCategoryReader;
    private readonly ICorpusQueryRepository _corpusQueryRepository;
    private readonly ITopicWatchFilter _topicWatchFilter;
    private readonly IRaindropClient _raindropClient;
    private readonly IWatchDigestNotifier _watchDigestNotifier;
    private readonly IWatchEvaluationLogRepository _watchEvaluationLogRepository;
    private readonly ILogger<TopicWatchJob> _logger;

    public TopicWatchJob(
        IWatchTopicRepository watchTopicRepository,
        IMinifluxCategoryReader minifluxCategoryReader,
        ICorpusQueryRepository corpusQueryRepository,
        ITopicWatchFilter topicWatchFilter,
        IRaindropClient raindropClient,
        IWatchDigestNotifier watchDigestNotifier,
        IWatchEvaluationLogRepository watchEvaluationLogRepository,
        ILogger<TopicWatchJob> logger)
    {
        _watchTopicRepository = watchTopicRepository;
        _minifluxCategoryReader = minifluxCategoryReader;
        _corpusQueryRepository = corpusQueryRepository;
        _topicWatchFilter = topicWatchFilter;
        _raindropClient = raindropClient;
        _watchDigestNotifier = watchDigestNotifier;
        _watchEvaluationLogRepository = watchEvaluationLogRepository;
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;

        try
        {
            var topics = await _watchTopicRepository.GetAllAsync(cancellationToken);
            if (topics.Count == 0)
            {
                _logger.LogInformation("Aucun sujet de veille configuré — rien à faire.");
                return;
            }

            var taxonomy = await _raindropClient.GetTaxonomyAsync(cancellationToken);
            var results = new List<WatchTopicRunResult>();

            foreach (var topic in topics)
            {
                try
                {
                    var result = await ProcessTopicAsync(topic, taxonomy, cancellationToken);
                    if (result is not null)
                    {
                        results.Add(result);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Échec du traitement du sujet de veille {TopicName} — poursuite avec le suivant.", topic.Name);
                }
            }

            if (results.Count > 0)
            {
                await _watchDigestNotifier.NotifyAsync(new WatchRunSummary(results), cancellationToken);
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Veille terminée : {TopicCount} sujets traités, {AddedCount} articles ajoutés.",
                    results.Count,
                    results.Sum(r => r.AddedCount));
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

    /// <summary>Retourne <c>null</c> si le sujet n'a rien eu à évaluer ce cycle (aucune entrée, ou curseur absent).</summary>
    private async Task<WatchTopicRunResult?> ProcessTopicAsync(WatchTopic topic, RaindropTaxonomy taxonomy, CancellationToken cancellationToken)
    {
        if (topic.LastMinifluxEntryId is null)
        {
            _logger.LogWarning(
                "Sujet de veille {TopicName} sans curseur — ignoré ce cycle (devrait être seedé à sa création par --add-watch-topic).",
                topic.Name);
            return null;
        }

        var (candidates, lastEntryId) = await _minifluxCategoryReader.GetNewEntriesAsync(topic.MinifluxCategoryId, topic.LastMinifluxEntryId, cancellationToken);
        if (candidates.Count == 0)
        {
            return null;
        }

        var addedCount = 0;

        foreach (var candidate in candidates)
        {
            try
            {
                addedCount += await EvaluateAndCreateIfMatchAsync(candidate, topic, taxonomy, cancellationToken) ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Le curseur (mis à jour ci-dessous) est indépendant du sort de chaque candidat — on
                // journalise et on continue avec le suivant plutôt que d'interrompre tout le sujet.
                _logger.LogError(ex, "Échec de l'évaluation de l'entrée de veille {SourceId} — poursuite avec la suivante.", candidate.SourceId);
            }
        }

        if (lastEntryId is not null)
        {
            await _watchTopicRepository.UpdateCursorAsync(topic.Id, lastEntryId, cancellationToken);
        }

        return new WatchTopicRunResult(topic.Name, candidates.Count, addedCount);
    }

    private async Task<bool> EvaluateAndCreateIfMatchAsync(Item candidate, WatchTopic topic, RaindropTaxonomy taxonomy, CancellationToken cancellationToken)
    {
        var related = await _corpusQueryRepository.SearchAsync(candidate.Title, MaxRelatedCorpusItems, cancellationToken);
        var evaluation = await _topicWatchFilter.EvaluateAsync(candidate, topic, taxonomy, related, cancellationToken);

        await RecordUsageAsync(evaluation, cancellationToken);

        if (evaluation.IsFallback)
        {
            _logger.LogWarning(
                "Évaluation en repli pour l'entrée de veille {SourceId} ({Reason}) — pas de création, lot non interrompu.",
                candidate.SourceId,
                evaluation.Reason);
            return false;
        }

        if (evaluation is not { IsRelevant: true, IsNew: true })
        {
            return false;
        }

        var tags = evaluation.Tags.Append(WatchTag).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _raindropClient.CreateRaindropAsync(candidate.Url, candidate.Title, topic.RaindropCollectionId, tags, evaluation.Reason, cancellationToken);
        return true;
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
