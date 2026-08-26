using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker.Options;

namespace LoreAI.Worker.Services;

/// <summary>
/// Bloc commun de traitement d'un <see cref="Item"/>, quelle que soit sa source (lot 8, #49) : fetch de
/// contenu optionnel, classification, persistance, alimentation de la base d'outils (S7) et notification
/// immédiate (Discord). Extrait de <c>UnsortedClassificationJob</c>, seul appelant jusqu'ici — le
/// write-back Raindrop (déplacement/tags écrits chez Raindrop) reste spécifique à ce job, une source
/// Newsletter/Feed n'étant jamais réécrite (ADR 0012).
/// </summary>
public sealed class ArticleClassificationStep
{
    private readonly IClassifier _classifier;
    private readonly IContentFetcher _contentFetcher;
    private readonly IArticleRepository _articleRepository;
    private readonly IImmediateNotifier _immediateNotifier;
    private readonly INotificationPolicy _notificationPolicy;
    private readonly IToolRepository _toolRepository;
    private readonly WorkerOptions _options;
    private readonly ILogger<ArticleClassificationStep> _logger;

    public ArticleClassificationStep(
        IClassifier classifier,
        IContentFetcher contentFetcher,
        IArticleRepository articleRepository,
        IImmediateNotifier immediateNotifier,
        INotificationPolicy notificationPolicy,
        IToolRepository toolRepository,
        IOptions<WorkerOptions> options,
        ILogger<ArticleClassificationStep> logger)
    {
        _classifier = classifier;
        _contentFetcher = contentFetcher;
        _articleRepository = articleRepository;
        _immediateNotifier = immediateNotifier;
        _notificationPolicy = notificationPolicy;
        _toolRepository = toolRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ArticleClassificationOutcome> ProcessAsync(Item item, RaindropTaxonomy taxonomy, CancellationToken cancellationToken)
    {
        var content = _options.FetchArticleContent
            ? await _contentFetcher.FetchAsync(item.Url, cancellationToken)
            : ContentFetchResult.Skipped;

        var classification = await _classifier.ClassifyAsync(item, taxonomy, content.Text, cancellationToken);
        var articleId = await _articleRepository.UpsertAsync(item, classification, content, DateTimeOffset.UtcNow, cancellationToken);

        await UpsertToolAsync(item, articleId, classification, cancellationToken);

        var notified = false;
        // Un repli n'est jamais surfacé comme un vrai signal (cf. ClassificationResult.Fallback) : la
        // garde est explicite ici, pas incidente à une politique de notification par défaut qui
        // exclurait déjà Action=Reference/Priority=Basse — une configuration différente ne doit pas
        // pouvoir transformer un échec de classification en alerte Discord.
        if (!classification.IsFallback && _notificationPolicy.ShouldNotifyImmediately(classification))
        {
            await _immediateNotifier.NotifyAsync(item, classification, cancellationToken);
            await _articleRepository.MarkDiscordNotifiedAsync(articleId, DateTimeOffset.UtcNow, cancellationToken);
            notified = true;
        }

        return new ArticleClassificationOutcome(articleId, classification, notified);
    }

    /// <summary>
    /// S7 (lot 5) : n'alimente la base d'outils que pour une vraie classification ATester avec un nom
    /// d'outil renseigné. Best-effort : ne bloque jamais le traitement de l'item.
    /// </summary>
    private async Task UpsertToolAsync(Item item, long articleId, ClassificationResult classification, CancellationToken cancellationToken)
    {
        if (classification.IsFallback || classification.Action != RecommendedAction.ATester || string.IsNullOrWhiteSpace(classification.ToolName))
        {
            return;
        }

        try
        {
            await _toolRepository.UpsertFromArticleAsync(classification.ToolName, classification.ToolCategory, classification.ToolUrl, articleId, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de la mise à jour de la base d'outils pour l'item {SourceId}", item.SourceId);
        }
    }
}

/// <summary>Résultat de <see cref="ArticleClassificationStep.ProcessAsync"/> — assez pour un write-back Raindrop en aval, ou juste du logging.</summary>
public sealed record ArticleClassificationOutcome(long ArticleId, ClassificationResult Classification, bool Notified);
