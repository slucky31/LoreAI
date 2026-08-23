using Coravel.Invocable;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Core.Services;
using LoreAI.Infrastructure.Notifications;

namespace LoreAI.Worker.Services;

/// <summary>
/// Rapport hebdomadaire d'hygiène et de signaux (#43) : doublons d'URL (N1), tags à nettoyer (N2),
/// collections déséquilibrées (N5), tendances (S3) et coût LLM (S6). Zéro appel LLM, zéro écriture —
/// lecture seule sur <c>LibraryItems</c>/<c>Articles</c> et la taxonomie Raindrop, envoi Markdown en
/// pièce jointe via le webhook Discord existant.
/// </summary>
public sealed class WeeklyInsightsJob : IInvocable, ICancellableInvocable
{
    private static readonly TimeSpan TrendWindow = TimeSpan.FromDays(30);

    private readonly ILibraryItemRepository _libraryItemRepository;
    private readonly IArticleRepository _articleRepository;
    private readonly IRaindropClient _raindropClient;
    private readonly IReportNotifier _reportNotifier;
    private readonly ILogger<WeeklyInsightsJob> _logger;

    public WeeklyInsightsJob(
        ILibraryItemRepository libraryItemRepository,
        IArticleRepository articleRepository,
        IRaindropClient raindropClient,
        IReportNotifier reportNotifier,
        ILogger<WeeklyInsightsJob> logger)
    {
        _libraryItemRepository = libraryItemRepository;
        _articleRepository = articleRepository;
        _raindropClient = raindropClient;
        _reportNotifier = reportNotifier;
        _logger = logger;
    }

    /// <summary>Alimenté par Coravel, annulé à l'arrêt de l'application (SIGTERM, <c>docker compose down</c>).</summary>
    public CancellationToken CancellationToken { get; set; }

    public async Task Invoke()
    {
        var cancellationToken = CancellationToken;
        var generatedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            var libraryItems = await _libraryItemRepository.GetAllForInsightsAsync(cancellationToken);
            var taxonomy = await _raindropClient.GetTaxonomyAsync(cancellationToken);
            var startOfMonthUtc = new DateTimeOffset(generatedAtUtc.Year, generatedAtUtc.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var rawResponses = await _articleRepository.GetClassificationRawResponsesSinceAsync(startOfMonthUtc, cancellationToken);

            var collectionTitles = taxonomy.Collections.ToDictionary(c => c.Id, c => c.Title);
            var recentItems = libraryItems.Where(i => generatedAtUtc - i.CapturedAtUtc <= TrendWindow).ToList();

            var report = new WeeklyInsightsReport(
                DuplicateUrlDetector.Detect(libraryItems),
                TagHygieneAnalyzer.Analyze(taxonomy.Tags),
                CollectionBalanceAnalyzer.Detect(libraryItems, collectionTitles),
                TrendAnalyzer.TopDomains(recentItems),
                TrendAnalyzer.TopTags(recentItems),
                LlmUsageAnalyzer.Analyze(rawResponses),
                generatedAtUtc);

            var markdown = MarkdownReportBuilder.Build(report);
            var fileName = $"loreai-insights-{generatedAtUtc:yyyy-MM-dd}.md";
            await _reportNotifier.SendReportAsync(fileName, markdown, cancellationToken);

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Rapport hebdomadaire envoyé : {DuplicateCount} doublons, {ClusterCount} grappes de tags, {UnbalancedCount} collections déséquilibrées, {ClassificationCount} classifications ce mois-ci.",
                    report.DuplicateUrls.Count,
                    report.TagHygiene.Clusters.Count,
                    report.UnbalancedCollections.Count,
                    report.LlmUsage.ClassificationCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Rapport hebdomadaire interrompu par l'arrêt de l'application.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec du calcul ou de l'envoi du rapport hebdomadaire.");
        }
    }
}
