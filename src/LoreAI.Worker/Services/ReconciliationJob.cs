using System.Globalization;
using Coravel.Invocable;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Worker.Services;

/// <summary>
/// Le chaînon manquant (L3, lot 6) : re-fetch des items Raindrop suivis pour détecter ce qu'aucun
/// autre job ne voit — tags/collection modifiés par l'utilisateur (signal « lu »), articles supprimés,
/// liens cassés. Alimente aussi la relance L4 dans la même passe : pas de raison de reparcourir les
/// mêmes articles deux fois pour deux décisions qui dépendent du même état.
/// </summary>
public sealed class ReconciliationJob : IInvocable, ICancellableInvocable
{
    /// <summary>Plafond d'une passe (même esprit que <c>LibraryIndexingJob.MaxPagesPerInvocation</c>) : le cron quotidien reprend le lendemain là où la précédente passe s'est arrêtée (tri par <c>LastSeenAtUtc</c> croissant).</summary>
    private const int MaxArticlesPerInvocation = 200;

    private static readonly TimeSpan ReminderThreshold = TimeSpan.FromDays(14);

    private readonly IRaindropClient _raindropClient;
    private readonly IArticleRepository _articleRepository;
    private readonly IReminderNotifier _reminderNotifier;
    private readonly ILogger<ReconciliationJob> _logger;

    public ReconciliationJob(
        IRaindropClient raindropClient,
        IArticleRepository articleRepository,
        IReminderNotifier reminderNotifier,
        ILogger<ReconciliationJob> logger)
    {
        _raindropClient = raindropClient;
        _articleRepository = articleRepository;
        _reminderNotifier = reminderNotifier;
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;

        try
        {
            var candidates = await _articleRepository.GetReconciliationCandidatesAsync(MaxArticlesPerInvocation, cancellationToken);
            var deletedCount = 0;
            var brokenCount = 0;
            var humanHandledCount = 0;
            var remindedCount = 0;

            foreach (var candidate in candidates)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var (linkStatus, humanHandledAtUtc) = await ReconcileOneAsync(candidate, now, cancellationToken);

                    if (linkStatus == LinkStatus.Deleted)
                    {
                        deletedCount++;
                    }
                    else if (linkStatus == LinkStatus.Broken)
                    {
                        brokenCount++;
                    }

                    if (humanHandledAtUtc is not null && candidate.HumanHandledAtUtc is null)
                    {
                        humanHandledCount++;
                    }

                    var remindedAtUtc = candidate.RemindedAtUtc;
                    if (ShouldRemind(candidate, humanHandledAtUtc, now))
                    {
                        await _reminderNotifier.NotifyAsync(candidate.Title, candidate.Url, (int)(now - candidate.ClassifiedAtUtc!.Value).TotalDays, cancellationToken);
                        remindedAtUtc = now;
                        remindedCount++;
                    }

                    await _articleRepository.RecordReconciliationAsync(candidate.Id, now, humanHandledAtUtc, remindedAtUtc, linkStatus, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Réconciliation interrompue par l'arrêt de l'application avant l'article {ArticleId}.", candidate.Id);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    // Un échec réseau isolé sur un article ne doit pas priver les suivants de leur passe :
                    // contrairement à LibraryIndexingJob (pages séquentielles), chaque article est indépendant.
                    _logger.LogWarning(ex, "Échec de la réconciliation de l'article {ArticleId} — repris à la prochaine passe.", candidate.Id);
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Réconciliation terminée : {Count} articles revus, {DeletedCount} supprimés, {BrokenCount} cassés, {HumanHandledCount} marqués traités, {RemindedCount} relancés.",
                    candidates.Count,
                    deletedCount,
                    brokenCount,
                    humanHandledCount,
                    remindedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Réconciliation interrompue par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de la passe de réconciliation.");
        }
    }

    private async Task<(LinkStatus LinkStatus, DateTimeOffset? HumanHandledAtUtc)> ReconcileOneAsync(
        ReconciliationCandidate candidate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // candidate.Id est l'id technique Articles.Id (généré par la base depuis le lot 8, #49) : jamais
        // l'id Raindrop, qui vit dans SourceId depuis le passage au modèle multi-sources (ADR 0012).
        var raindropId = long.Parse(candidate.SourceId, CultureInfo.InvariantCulture);
        var snapshot = await _raindropClient.GetRaindropAsync(raindropId, cancellationToken);
        if (snapshot is null)
        {
            return (LinkStatus.Deleted, candidate.HumanHandledAtUtc);
        }

        var linkStatus = snapshot.Broken ? LinkStatus.Broken : LinkStatus.Ok;

        if (candidate.HumanHandledAtUtc is not null)
        {
            return (linkStatus, candidate.HumanHandledAtUtc);
        }

        var expectedTags = candidate.OriginalTags
            .Concat(candidate.SuggestedTags)
            .Select(t => t.ToUpperInvariant())
            .ToHashSet();
        var actualTags = snapshot.Tags.Select(t => t.ToUpperInvariant()).ToHashSet();
        var tagsChanged = !expectedTags.SetEquals(actualTags);

        var expectedCollectionId = candidate.WriteBackCollectionId ?? -1L;
        var actualCollectionId = snapshot.CollectionId ?? -1L;
        var collectionChanged = expectedCollectionId != actualCollectionId;

        var humanHandledAtUtc = tagsChanged || collectionChanged ? now : (DateTimeOffset?)null;
        return (linkStatus, humanHandledAtUtc);
    }

    /// <summary>L4 : une seule relance par article, seulement pour ce qui aurait dû être notifié immédiatement (<c>ATester</c>/<c>Haute</c>) et reste non traité 14 jours après classification.</summary>
    private static bool ShouldRemind(ReconciliationCandidate candidate, DateTimeOffset? humanHandledAtUtc, DateTimeOffset now) =>
        candidate.Action == RecommendedAction.ATester
        && candidate.Priority == Priority.Haute
        && humanHandledAtUtc is null
        && candidate.RemindedAtUtc is null
        && candidate.ClassifiedAtUtc is not null
        && now - candidate.ClassifiedAtUtc.Value >= ReminderThreshold;
}
