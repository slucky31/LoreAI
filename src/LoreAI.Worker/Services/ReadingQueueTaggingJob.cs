using System.Globalization;
using Coravel.Invocable;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Core.Services;
using LoreAI.Worker.Options;
using Microsoft.Extensions.Options;

namespace LoreAI.Worker.Services;

/// <summary>
/// L5 (lot 8) : pose un tag Raindrop dédié (<see cref="WorkerOptions.ReadingQueueTagName"/>, défaut
/// <c>cette-semaine</c>) sur les articles de la file de lecture (L1, <see cref="ReadingQueueScorer"/>),
/// et le retire de ceux qui en sont sortis — première écriture du projet hors « Non trié », validée
/// explicitement (roadmap, section Risques). Un tag plutôt qu'une vraie collection : un raindrop
/// n'appartient qu'à une seule collection à la fois, y déplacer l'article le retirerait de sa collection
/// thématique déjà assignée par la classification. Jamais de déplacement (<c>collectionId: null</c>),
/// jamais de réécriture de note (<see cref="RaindropSnapshot.Note"/> relu à chaque appel, jamais celui
/// stocké en base qui date de la classification). Best-effort par article, même philosophie que
/// <c>ReconciliationJob</c>/<c>FeedIngestionJob</c> : un échec n'interrompt jamais les autres.
/// </summary>
public sealed class ReadingQueueTaggingJob : IInvocable, ICancellableInvocable
{
    /// <summary>Même taille que le digest hebdomadaire (<c>WeeklyInsightsJob</c>) — cohérence entre ce qui est tagué et ce qui est rapporté sur Discord.</summary>
    private const int ReadingQueueSize = 10;

    private readonly IArticleRepository _articleRepository;
    private readonly IRaindropClient _raindropClient;
    private readonly WorkerOptions _options;
    private readonly ILogger<ReadingQueueTaggingJob> _logger;

    public ReadingQueueTaggingJob(
        IArticleRepository articleRepository,
        IRaindropClient raindropClient,
        IOptions<WorkerOptions> options,
        ILogger<ReadingQueueTaggingJob> logger)
    {
        _articleRepository = articleRepository;
        _raindropClient = raindropClient;
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
            var now = DateTimeOffset.UtcNow;
            var trackedArticles = await _articleRepository.GetTrackedArticlesAsync(cancellationToken);
            var queue = ReadingQueueScorer.Score(trackedArticles, now, ReadingQueueSize)
                .Where(e => e.SourceType == SourceType.Raindrop)
                .ToDictionary(e => e.SourceId);

            var currentlyTagged = await _articleRepository.GetReadingQueueTaggedAsync(cancellationToken);
            var currentlyTaggedBySourceId = currentlyTagged.ToDictionary(a => a.SourceId);

            var toUntag = currentlyTagged.Where(a => !queue.ContainsKey(a.SourceId)).ToList();
            var toTag = queue.Values.Where(e => !currentlyTaggedBySourceId.ContainsKey(e.SourceId)).ToList();

            var untaggedCount = 0;
            var taggedCount = 0;
            var failedCount = 0;

            foreach (var article in toUntag)
            {
                try
                {
                    await UntagAsync(article, cancellationToken);
                    untaggedCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Tag de la file de lecture interrompu par l'arrêt de l'application.");
                    return;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(ex, "Échec du retrait du tag « {Tag} » pour l'article {ArticleId} — repris à la prochaine passe.", _options.ReadingQueueTagName, article.ArticleId);
                }
            }

            foreach (var entry in toTag)
            {
                try
                {
                    await TagAsync(entry, now, cancellationToken);
                    taggedCount++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Tag de la file de lecture interrompu par l'arrêt de l'application.");
                    return;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(ex, "Échec de la pose du tag « {Tag} » pour l'article {ArticleId} — repris à la prochaine passe.", _options.ReadingQueueTagName, entry.Id);
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Tag de la file de lecture terminé : {TaggedCount} posés, {UntaggedCount} retirés, {FailedCount} échecs.",
                    taggedCount,
                    untaggedCount,
                    failedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Tag de la file de lecture interrompu par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du tag de la file de lecture.");
        }
    }

    private async Task TagAsync(ReadingQueueEntry entry, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var raindropId = long.Parse(entry.SourceId, CultureInfo.InvariantCulture);
        var snapshot = await _raindropClient.GetRaindropAsync(raindropId, cancellationToken);
        if (snapshot is null)
        {
            // Supprimé entre-temps côté Raindrop : rien à tagger, et rien à suivre non plus.
            return;
        }

        if (!snapshot.Tags.Contains(_options.ReadingQueueTagName, StringComparer.OrdinalIgnoreCase))
        {
            var newTags = snapshot.Tags.Append(_options.ReadingQueueTagName).ToList();
            await _raindropClient.UpdateRaindropAsync(raindropId, newTags, snapshot.Note ?? string.Empty, collectionId: null, cancellationToken);
        }

        await _articleRepository.SetReadingQueueTagAsync(entry.Id, now, cancellationToken);
    }

    private async Task UntagAsync(ReadingQueueTaggedArticle article, CancellationToken cancellationToken)
    {
        var raindropId = long.Parse(article.SourceId, CultureInfo.InvariantCulture);
        var snapshot = await _raindropClient.GetRaindropAsync(raindropId, cancellationToken);
        if (snapshot is null)
        {
            // Supprimé entre-temps : plus rien à détagger, mais le suivi local doit quand même être effacé.
            await _articleRepository.SetReadingQueueTagAsync(article.ArticleId, null, cancellationToken);
            return;
        }

        if (snapshot.Tags.Contains(_options.ReadingQueueTagName, StringComparer.OrdinalIgnoreCase))
        {
            var newTags = snapshot.Tags.Where(t => !string.Equals(t, _options.ReadingQueueTagName, StringComparison.OrdinalIgnoreCase)).ToList();
            await _raindropClient.UpdateRaindropAsync(raindropId, newTags, snapshot.Note ?? string.Empty, collectionId: null, cancellationToken);
        }

        await _articleRepository.SetReadingQueueTagAsync(article.ArticleId, null, cancellationToken);
    }
}
