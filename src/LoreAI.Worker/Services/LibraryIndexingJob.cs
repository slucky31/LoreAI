using Coravel.Invocable;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Worker.Services;

/// <summary>
/// Indexation en lecture seule de toute la bibliothèque Raindrop (collection 0, hors corbeille — lot 1, #42) :
/// remplit <c>LibraryItems</c> sans jamais classifier ni écrire chez Raindrop. Volontairement sans dépendance
/// à <see cref="IClassifier"/>/<see cref="IArticleRepository"/>/<see cref="ICycleRunRepository"/> — le
/// caractère lecture seule est garanti par la forme du constructeur, pas seulement par convention.
/// </summary>
public sealed class LibraryIndexingJob : IInvocable, ICancellableInvocable
{
    /// <summary>
    /// Garde-fou contre une pagination qui ne se terminerait jamais (même esprit que
    /// <c>RaindropClient.MaxPagesPerCycle</c>) — pas un découpage volontaire du travail : ce job ne fait
    /// aucun appel LLM, une passe complète tient normalement en une seule invocation même sur un Pi. La
    /// reprise via <c>ResumePage</c> sert à survivre à une interruption (SIGTERM, coupure), pas à étaler
    /// une passe normale sur plusieurs cycles.
    /// </summary>
    private const int MaxPagesPerInvocation = 500;

    /// <summary>
    /// Évite qu'un redémarrage rapproché (déploiement, crash-loop) avec <c>Worker__IndexLibraryOnStartup=true</c>
    /// ne relance une passe complète à chaque fois — décision produit du lot 1.
    /// </summary>
    private static readonly TimeSpan MinReindexInterval = TimeSpan.FromHours(24);

    private readonly IRaindropClient _raindropClient;
    private readonly ILibraryIndexStateRepository _indexStateRepository;
    private readonly ILibraryItemRepository _libraryItemRepository;
    private readonly ILogger<LibraryIndexingJob> _logger;

    public LibraryIndexingJob(
        IRaindropClient raindropClient,
        ILibraryIndexStateRepository indexStateRepository,
        ILibraryItemRepository libraryItemRepository,
        ILogger<LibraryIndexingJob> logger)
    {
        _raindropClient = raindropClient;
        _indexStateRepository = indexStateRepository;
        _libraryItemRepository = libraryItemRepository;
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;

        var state = await _indexStateRepository.GetAsync(SourceType.Raindrop, cancellationToken);

        if (state.ResumePage is null && state.LastFullPassCompletedUtc is not null
            && DateTimeOffset.UtcNow - state.LastFullPassCompletedUtc.Value < MinReindexInterval)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Indexation de la bibliothèque ignorée : dernière passe complète terminée le {LastCompleted}, moins de {MinInterval} auparavant.",
                    state.LastFullPassCompletedUtc,
                    MinReindexInterval);
            }
            return;
        }

        var startedUtc = state.ResumePage is null ? DateTimeOffset.UtcNow : state.LastFullPassStartedUtc ?? DateTimeOffset.UtcNow;
        var page = state.ResumePage ?? 0;

        try
        {
            for (var pagesFetched = 0; pagesFetched < MaxPagesPerInvocation; pagesFetched++)
            {
                var items = await _raindropClient.GetLibraryPageAsync(page, cancellationToken);

                if (items.Count == 0)
                {
                    // Volontairement non annulable, comme le curseur de UnsortedClassificationJob : la
                    // dernière page vient d'être confirmée, perdre cette écriture rejouerait toute la passe.
                    await _indexStateRepository.UpdateAsync(
                        new LibraryIndexState(SourceType.Raindrop, null, startedUtc, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                        CancellationToken.None);
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Indexation de la bibliothèque terminée : passe complète jusqu'à la page {Page}.", page);
                    }
                    return;
                }

                await _libraryItemRepository.UpsertPageAsync(items, DateTimeOffset.UtcNow, cancellationToken);
                page++;

                await _indexStateRepository.UpdateAsync(
                    new LibraryIndexState(SourceType.Raindrop, page, startedUtc, null, DateTimeOffset.UtcNow),
                    CancellationToken.None);
            }

            _logger.LogWarning(
                "Plafond de {MaxPages} pages atteint pour l'indexation de la bibliothèque : reprise à la page {Page} au prochain déclenchement.",
                MaxPagesPerInvocation,
                page);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Indexation de la bibliothèque interrompue par l'arrêt de l'application — reprise à la page {Page}.", page);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de l'indexation de la bibliothèque — reprise à la page {Page} au prochain déclenchement.", page);
        }
    }
}
